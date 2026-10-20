using System;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace Jackett.Mobile.Core.Cloudflare
{
    // Recognises the "checking your browser" pages of Cloudflare (and DDoS-Guard, which works the same way).
    public static class ChallengeDetector
    {
        // Cloudflare injects scripts from /cdn-cgi/challenge-platform/ into normal pages too,
        // so only markers that appear exclusively on the interstitial pages are used.
        private static readonly string[] BodyMarkers =
        {
            "<title>Just a moment...</title>",
            "<title>Attention Required! | Cloudflare</title>",
            "<title>DDoS-Guard</title>",
            "cf_chl_opt",
            "cf-browser-verification",
            "Checking your browser before accessing"
        };

        // Same check, run inside the in-app browser to know when the challenge page is gone.
        // Returns "1" while the page is still a challenge.
        public const string ChallengePageScript =
            "(function(){var t=document.title||'';var h=document.documentElement?document.documentElement.innerHTML:'';" +
            "return (/just a moment|attention required|ddos-guard|checking your browser/i.test(t)" +
            "||h.indexOf('cf_chl_opt')>=0||h.indexOf('cf-browser-verification')>=0)?'1':'0';})()";

        // Cheap check on the status and headers, to avoid reading every body.
        public static bool MightBeChallenge(HttpResponseMessage response)
        {
            var status = response.StatusCode;
            if (status != HttpStatusCode.Forbidden && status != HttpStatusCode.ServiceUnavailable && (int)status != 429)
                return false;

            if (response.Headers.TryGetValues("cf-mitigated", out _))
                return true;

            var server = response.Headers.Server?.ToString() ?? string.Empty;
            return server.IndexOf("cloudflare", StringComparison.OrdinalIgnoreCase) >= 0
                || server.IndexOf("ddos-guard", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsChallenge(HttpResponseMessage response, string body)
        {
            if (!MightBeChallenge(response))
                return false;

            if (response.Headers.TryGetValues("cf-mitigated", out var mitigated) &&
                mitigated.Any(v => v.Equals("challenge", StringComparison.OrdinalIgnoreCase)))
                return true;

            return IsChallengeBody(body);
        }

        public static bool IsChallengeBody(string body) =>
            !string.IsNullOrEmpty(body) && BodyMarkers.Any(m => body.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
