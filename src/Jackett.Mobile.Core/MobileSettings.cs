using System;
using System.IO;
using Newtonsoft.Json;

namespace Jackett.Mobile.Core
{
    // App specific settings. Jackett's own settings (FlareSolverr, proxy, cache) stay in ServerConfig.
    public class MobileSettings
    {
        private string _filePath;

        // Send requests with the iOS network stack (closer to Safari, which helps with Cloudflare).
        // Takes effect on the next app launch.
        public bool UseNativeNetworking { get; set; } = true;

        // Show the in-app browser when a site answers with a Cloudflare / DDoS-Guard check.
        public bool SolveChallengesInBrowser { get; set; } = true;

        // Stop waiting for slow indexers after this many seconds.
        public int SearchTimeoutSeconds { get; set; } = 60;

        public static MobileSettings Load(string filePath)
        {
            MobileSettings settings = null;
            try
            {
                if (File.Exists(filePath))
                    settings = JsonConvert.DeserializeObject<MobileSettings>(File.ReadAllText(filePath));
            }
            catch (Exception)
            {
                // fall back to the defaults
            }

            settings ??= new MobileSettings();
            settings._filePath = filePath;
            return settings;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }
}
