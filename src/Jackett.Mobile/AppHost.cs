using System.Net.Http;
using Jackett.Mobile.Core;
using Jackett.Mobile.Core.Cloudflare;
using Jackett.Mobile.Core.Services;

namespace Jackett.Mobile;

// Implemented in Platforms/iOS.
public interface IPlatformServices
{
    // Read only folder in the app bundle with the YAML definitions.
    string BundledDefinitionsFolder { get; }

    // Folder visible in the Files app.
    string DocumentsFolder { get; }

    // NSUrlSessionHandler factory, null to always use the managed handler.
    Func<HttpMessageHandler> NativeTransportFactory { get; }

    Task<IBrowserSolver> CreateBrowserSolverAsync();
}

// Owns the running Jackett instance of the app.
public static class AppHost
{
    private const string ProtectionKeyName = "jackett-protection-key";

    public static IPlatformServices Platform { get; set; }

    public static MobileHost Current { get; private set; }

    public static async Task StartAsync()
    {
        if (Current != null)
            return;

        var browser = Platform == null ? null : await Platform.CreateBrowserSolverAsync();
        var key = await GetProtectionKeyAsync();

        var options = new MobileHostOptions
        {
            DataFolder = Path.Combine(FileSystem.AppDataDirectory, "Jackett"),
            BundledDefinitionsFolder = Platform?.BundledDefinitionsFolder ?? Path.Combine(AppContext.BaseDirectory, "JackettDefinitions"),
            CustomDefinitionsFolder = Platform == null ? null : Path.Combine(Platform.DocumentsFolder, "Definitions"),
            AppVersion = "v" + AppInfo.Current.VersionString,
            ProtectionKey = key,
            BrowserSolver = browser,
            NativeTransportFactory = Platform?.NativeTransportFactory
        };

        // loading ~650 indexers takes a few seconds, keep it off the UI thread
        Current = await Task.Run(() => MobileHost.Start(options));
    }

    // The key that encrypts saved passwords lives in the Keychain. Returns null if the Keychain is not
    // available, then MobileHost falls back to a key file in the app data folder.
    private static async Task<byte[]> GetProtectionKeyAsync()
    {
        try
        {
            var stored = await SecureStorage.Default.GetAsync(ProtectionKeyName);
            if (!string.IsNullOrEmpty(stored))
                return Convert.FromBase64String(stored);

            var key = MobileProtectionService.GenerateKey();
            await SecureStorage.Default.SetAsync(ProtectionKeyName, Convert.ToBase64String(key));
            return key;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
