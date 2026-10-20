using System.Net.Http;
using Foundation;
using Jackett.Mobile.Core.Cloudflare;

namespace Jackett.Mobile;

public class IosPlatformServices : IPlatformServices
{
    // MauiAsset files land in the root of the app bundle, under their LogicalName
    public string BundledDefinitionsFolder => Path.Combine(NSBundle.MainBundle.BundlePath, "JackettDefinitions");

    // the app's Documents folder, shown in the Files app (UIFileSharingEnabled)
    public string DocumentsFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    // The iOS network stack, like Safari. Its own cookie handling is off: Jackett keeps separate cookies
    // per indexer and CookieBridgeHandler sends them. Redirects are followed by Jackett itself.
    public Func<HttpMessageHandler> NativeTransportFactory => () => new NSUrlSessionHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        DisableCaching = true
    };

    public Task<IBrowserSolver> CreateBrowserSolverAsync() => Task.FromResult<IBrowserSolver>(new WebViewBrowserSolver());
}
