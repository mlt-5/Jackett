using CoreGraphics;
using Foundation;
using Jackett.Mobile.Core.Cloudflare;
using UIKit;
using WebKit;

namespace Jackett.Mobile;

public enum BrowserSheetMode
{
    // closes by itself once the check page is gone
    Challenge,

    // the user logs in and taps Done
    Login
}

public class BrowserSheetController : UIViewController
{
    private static readonly TimeSpan ChallengeTimeout = TimeSpan.FromMinutes(3);

    private readonly Uri _url;
    private readonly BrowserSheetMode _mode;
    private readonly WKWebViewConfiguration _configuration;
    private readonly TaskCompletionSource<BrowserClearance> _completion =
        new TaskCompletionSource<BrowserClearance>(TaskCreationOptions.RunContinuationsAsynchronously);

    private WKWebView _webView;
    private NSTimer _timer;
    private DateTime _startedUtc;
    private bool _checking;

    public BrowserSheetController(Uri url, BrowserSheetMode mode, WKWebViewConfiguration configuration)
    {
        _url = url;
        _mode = mode;
        _configuration = configuration;
    }

    public Task<BrowserClearance> Completion => _completion.Task;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View.BackgroundColor = UIColor.SystemBackground;
        Title = _url.Host;

        NavigationItem.LeftBarButtonItem = new UIBarButtonItem(UIBarButtonSystemItem.Cancel, (_, _) => Finish(null));
        if (_mode == BrowserSheetMode.Login)
            NavigationItem.RightBarButtonItem = new UIBarButtonItem(UIBarButtonSystemItem.Done, async (_, _) => Finish(await CollectAsync()));

        var hint = new UILabel
        {
            Text = _mode == BrowserSheetMode.Login
                ? "Log in to the site (solve the captcha if there is one), then tap Done."
                : "This site checks that you are a person. Wait a moment, or tap the checkbox if one appears.",
            Lines = 0,
            Font = UIFont.PreferredFootnote,
            TextColor = UIColor.SecondaryLabel,
            TranslatesAutoresizingMaskIntoConstraints = false
        };

        _webView = new WKWebView(CGRect.Empty, _configuration) { TranslatesAutoresizingMaskIntoConstraints = false };

        View.AddSubview(hint);
        View.AddSubview(_webView);
        var guide = View.SafeAreaLayoutGuide;
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            hint.TopAnchor.ConstraintEqualTo(guide.TopAnchor, 8),
            hint.LeadingAnchor.ConstraintEqualTo(guide.LeadingAnchor, 16),
            hint.TrailingAnchor.ConstraintEqualTo(guide.TrailingAnchor, -16),
            _webView.TopAnchor.ConstraintEqualTo(hint.BottomAnchor, 8),
            _webView.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            _webView.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            _webView.BottomAnchor.ConstraintEqualTo(View.BottomAnchor)
        });

        _webView.LoadRequest(new NSUrlRequest(new NSUrl(_url.AbsoluteUri)));

        if (_mode == BrowserSheetMode.Challenge)
        {
            _startedUtc = DateTime.UtcNow;
            _timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromSeconds(1.5), timer => { _ = CheckChallengeAsync(); });
        }
    }

    public override void ViewDidDisappear(bool animated)
    {
        base.ViewDidDisappear(animated);
        Finish(null);
    }

    public void Finish(BrowserClearance result)
    {
        _timer?.Invalidate();
        _timer = null;
        _completion.TrySetResult(result);
    }

    // Polls the page: once it is no longer a check page, the site cookies are taken.
    private async Task CheckChallengeAsync()
    {
        if (_checking || _completion.Task.IsCompleted)
            return;
        _checking = true;
        try
        {
            if (DateTime.UtcNow - _startedUtc > ChallengeTimeout)
            {
                Finish(null);
                return;
            }

            if (_webView.IsLoading || _webView.Url == null || _webView.Url.AbsoluteString == "about:blank")
                return;

            var isChallenge = await _webView.EvaluateJavaScriptAsync(ChallengeDetector.ChallengePageScript);
            if (isChallenge?.ToString() != "0")
                return;

            var clearance = await CollectAsync();
            if (clearance.Cookies.Count > 0)
                Finish(clearance);
        }
        catch (Exception)
        {
            // the page navigated while the script ran, try again on the next tick
        }
        finally
        {
            _checking = false;
        }
    }

    private async Task<BrowserClearance> CollectAsync()
    {
        string userAgent = null;
        try
        {
            userAgent = (await _webView.EvaluateJavaScriptAsync("navigator.userAgent"))?.ToString();
        }
        catch (Exception)
        {
            // the solver falls back to its own value
        }

        var finalHost = _webView.Url?.Host ?? _url.Host;
        var clearance = new BrowserClearance { Host = _url.Host, UserAgent = userAgent };

        var cookies = await _configuration.WebsiteDataStore.HttpCookieStore.GetAllCookiesAsync();
        foreach (var cookie in cookies)
        {
            var browserCookie = new BrowserCookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = cookie.Path,
                ExpiresUtc = cookie.ExpiresDate == null ? null : ((DateTime)cookie.ExpiresDate).ToUniversalTime()
            };

            // cookies of the site, of the host it redirected to, and of its subdomains
            var domain = browserCookie.Domain?.TrimStart('.') ?? "";
            if (browserCookie.MatchesHost(_url.Host) || browserCookie.MatchesHost(finalHost)
                || domain.EndsWith("." + _url.Host, StringComparison.OrdinalIgnoreCase))
                clearance.Cookies.Add(browserCookie);
        }

        return clearance;
    }
}
