using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Jackett.Mobile.Core.Cloudflare
{
    // Keeps the cookies obtained in the in-app browser, per host, and persists them across app launches.
    public class ClearanceStore
    {
        private readonly string _filePath;
        private readonly Func<DateTime> _utcNow;
        private readonly object _lock = new object();
        private Dictionary<string, BrowserClearance> _byHost;

        public ClearanceStore(string filePath, Func<DateTime> utcNow = null)
        {
            _filePath = filePath;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _byHost = Load();
        }

        public event EventHandler Changed;

        // Finds the clearance saved for the host itself or for one of its parent domains.
        public BrowserClearance Find(string host)
        {
            if (string.IsNullOrEmpty(host))
                return null;

            lock (_lock)
            {
                var now = _utcNow();
                foreach (var candidate in HostAndParents(host))
                {
                    if (_byHost.TryGetValue(candidate, out var clearance) && clearance.ValidCookiesFor(host, now).Any())
                        return clearance;
                }
                return null;
            }
        }

        public void Save(BrowserClearance clearance)
        {
            if (clearance == null || string.IsNullOrEmpty(clearance.Host))
                throw new ArgumentException("The clearance must have a host", nameof(clearance));

            lock (_lock)
            {
                _byHost[clearance.Host.ToLowerInvariant()] = clearance;
                Persist();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Remove(string host)
        {
            lock (_lock)
            {
                if (!_byHost.Remove(host.ToLowerInvariant()))
                    return;
                Persist();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            lock (_lock)
            {
                _byHost.Clear();
                Persist();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public IReadOnlyList<BrowserClearance> All()
        {
            lock (_lock)
                return _byHost.Values.OrderBy(c => c.Host).ToList();
        }

        private static IEnumerable<string> HostAndParents(string host)
        {
            var parts = host.ToLowerInvariant().Split('.');
            // stop before the bare TLD
            for (var i = 0; i < parts.Length - 1; i++)
                yield return string.Join(".", parts.Skip(i));
        }

        private Dictionary<string, BrowserClearance> Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var list = JsonConvert.DeserializeObject<List<BrowserClearance>>(File.ReadAllText(_filePath));
                    if (list != null)
                        return list.Where(c => !string.IsNullOrEmpty(c.Host))
                                   .GroupBy(c => c.Host.ToLowerInvariant())
                                   .ToDictionary(g => g.Key, g => g.Last());
                }
            }
            catch (Exception)
            {
                // a corrupt file only means the user has to pass the checks again
            }
            return new Dictionary<string, BrowserClearance>();
        }

        private void Persist()
        {
            var now = _utcNow();
            // drop sites where every cookie has expired
            foreach (var host in _byHost.Where(kv => kv.Value.Cookies.All(c => c.IsExpired(now))).Select(kv => kv.Key).ToList())
                _byHost.Remove(host);

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(_byHost.Values.ToList(), Formatting.Indented));
        }
    }
}
