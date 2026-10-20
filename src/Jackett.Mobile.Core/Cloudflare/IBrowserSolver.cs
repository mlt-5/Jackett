using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jackett.Mobile.Core.Cloudflare
{
    // Implemented by the platform (WKWebView on iOS). Shows a real browser to the user.
    public interface IBrowserSolver
    {
        // User-Agent of the in-app browser. Requests reusing its cookies must send the same one.
        string UserAgent { get; }

        // Opens the url and closes automatically once the Cloudflare check is passed.
        // Returns null if the user cancelled or the check could not be passed.
        Task<BrowserClearance> SolveChallengeAsync(Uri url, CancellationToken cancellationToken);

        // Opens the url so the user can log in (captchas included) and returns the site cookies
        // when the user taps Done. Returns null if the user cancelled.
        Task<BrowserClearance> LoginAsync(Uri url, CancellationToken cancellationToken);
    }
}
