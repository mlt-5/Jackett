using System;
using System.Net.Http;
using Jackett.Mobile.Core.Cloudflare;

namespace Jackett.Mobile.Core
{
    public class MobileHostOptions
    {
        // Writable folder for configuration, indexer settings, logs and cookies.
        public string DataFolder { get; set; }

        // Read only folder with the YAML definitions shipped in the app.
        public string BundledDefinitionsFolder { get; set; }

        // Folder where the user can add or override YAML definitions (Documents/Definitions on iOS,
        // visible in the Files app). Defaults to DataFolder/Definitions.
        public string CustomDefinitionsFolder { get; set; }

        public string AppVersion { get; set; } = "v0.0.0";

        // 64 bytes, see MobileProtectionService. Null stores a key file in DataFolder.
        public byte[] ProtectionKey { get; set; }

        // In-app browser used for challenges and logins. Null disables both features.
        public IBrowserSolver BrowserSolver { get; set; }

        // Native transport (NSUrlSessionHandler on iOS). Null uses the managed .NET handler.
        public Func<HttpMessageHandler> NativeTransportFactory { get; set; }

        public bool TracingEnabled { get; set; }
    }
}
