using Jackett.Mobile.Core.Cloudflare;
using UIKit;
using WebKit;

namespace Jackett.Mobile;

// Shows sites in a WKWebView sheet to pass Cloudflare / DDoS-Guard checks or to log in,
// then hands their cookies and the browser User-Agent to Jackett.
public class WebViewBrowserSolver : IBrowserSolver
{
    public WebViewBrowserSolver()
    {
        // Until a page reports the exact value, use what WebKit builds with our application name.
        var version = UIDevice.CurrentDevice.SystemVersion.Split('.');
        var os = string.Join("_", version.Take(2));
        UserAgent = $"Mozilla/5.0 (iPhone; CPU iPhone OS {os} like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) {ApplicationName()}";
    }

    public string UserAgent { get; private set; }

    public Task<BrowserClearance> SolveChallengeAsync(Uri url, CancellationToken cancellationToken) =>
        ShowAsync(url, BrowserSheetMode.Challenge, cancellationToken);

    public Task<BrowserClearance> LoginAsync(Uri url, CancellationToken cancellationToken) =>
        ShowAsync(url, BrowserSheetMode.Login, cancellationToken);

    // Same suffix as Safari, so sites see a normal Safari User-Agent and not an embedded browser.
    internal static string ApplicationName()
    {
        var version = UIDevice.CurrentDevice.SystemVersion.Split('.');
        var safariVersion = version.Length > 1 ? $"{version[0]}.{version[1]}" : $"{version[0]}.0";
        return $"Version/{safariVersion} Mobile/15E148 Safari/604.1";
    }

    internal static WKWebViewConfiguration CreateConfiguration() =>
        new WKWebViewConfiguration
        {
            // persistent, so a check passed once is remembered by the browser too
            WebsiteDataStore = WKWebsiteDataStore.DefaultDataStore,
            ApplicationNameForUserAgent = ApplicationName()
        };

    private Task<BrowserClearance> ShowAsync(Uri url, BrowserSheetMode mode, CancellationToken cancellationToken) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var presenter = Microsoft.Maui.ApplicationModel.Platform.GetCurrentUIViewController();
            if (presenter == null)
                return null;

            var sheet = new BrowserSheetController(url, mode, CreateConfiguration());
            var navigation = new UINavigationController(sheet) { ModalInPresentation = true };

            using var registration = cancellationToken.Register(() => MainThread.BeginInvokeOnMainThread(() => sheet.Finish(null)));
            await presenter.PresentViewControllerAsync(navigation, true);
            var result = await sheet.Completion;
            await navigation.DismissViewControllerAsync(true);

            if (!string.IsNullOrEmpty(result?.UserAgent))
                UserAgent = result.UserAgent;
            return result;
        });
}
