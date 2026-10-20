using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Jackett.Mobile.Core.Cloudflare
{
    // Shared by every indexer web client: makes sure only one browser challenge is shown at a time
    // and that a site the user dismissed is not asked again during this session.
    public class BrowserChallengeCoordinator
    {
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, bool> _declinedHosts = new ConcurrentDictionary<string, bool>();
        private readonly Func<IBrowserSolver> _solverProvider;
        private readonly Func<bool> _isEnabled;

        public BrowserChallengeCoordinator(ClearanceStore store, Func<IBrowserSolver> solverProvider, Func<bool> isEnabled)
        {
            Store = store;
            _solverProvider = solverProvider;
            _isEnabled = isEnabled;
        }

        public ClearanceStore Store { get; }

        public void ForgetDeclinedHosts() => _declinedHosts.Clear();

        // Returns cookies that pass the challenge for the url, showing the browser if needed.
        // Returns null when solving is disabled, unavailable or was cancelled by the user.
        public async Task<BrowserClearance> GetClearanceAsync(Uri url, DateTime requestStartedUtc, CancellationToken cancellationToken)
        {
            var host = url.Host.ToLowerInvariant();
            var solver = _solverProvider?.Invoke();
            if (solver == null || !_isEnabled() || _declinedHosts.ContainsKey(host))
                return null;

            await _gate.WaitAsync(cancellationToken);
            try
            {
                // another request may have solved it while this one was waiting
                var existing = Store.Find(host);
                if (existing != null && existing.ObtainedUtc >= requestStartedUtc)
                    return existing;

                if (_declinedHosts.ContainsKey(host))
                    return null;

                var clearance = await solver.SolveChallengeAsync(url, cancellationToken);
                if (clearance == null || clearance.Cookies.Count == 0)
                {
                    _declinedHosts[host] = true;
                    return null;
                }

                if (string.IsNullOrEmpty(clearance.Host))
                    clearance.Host = host;
                if (string.IsNullOrEmpty(clearance.UserAgent))
                    clearance.UserAgent = solver.UserAgent;

                Store.Save(clearance);
                return clearance;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
