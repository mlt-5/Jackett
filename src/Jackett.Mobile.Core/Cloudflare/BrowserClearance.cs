using System;
using System.Collections.Generic;
using System.Linq;

namespace Jackett.Mobile.Core.Cloudflare
{
    // A cookie captured from the in-app browser.
    public class BrowserCookie
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Domain { get; set; }
        public string Path { get; set; } = "/";
        public DateTime? ExpiresUtc { get; set; }

        public bool IsExpired(DateTime nowUtc) => ExpiresUtc.HasValue && ExpiresUtc.Value <= nowUtc;

        // Same rules as browsers: ".example.com" (or "example.com") matches example.com and its subdomains.
        public bool MatchesHost(string host)
        {
            if (string.IsNullOrEmpty(Domain) || string.IsNullOrEmpty(host))
                return false;

            var domain = Domain.TrimStart('.');
            return host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Everything the in-app browser hands back after passing a challenge or a login:
    // the cookies of the site and the User-Agent they were issued to (cf_clearance is bound to it).
    public class BrowserClearance
    {
        public const string CloudflareCookieName = "cf_clearance";

        public string Host { get; set; }
        public string UserAgent { get; set; }
        public List<BrowserCookie> Cookies { get; set; } = new List<BrowserCookie>();
        public DateTime ObtainedUtc { get; set; } = DateTime.UtcNow;

        public bool HasCloudflareCookie(DateTime nowUtc) =>
            Cookies.Any(c => c.Name == CloudflareCookieName && !c.IsExpired(nowUtc));

        public IEnumerable<BrowserCookie> ValidCookiesFor(string host, DateTime nowUtc) =>
            Cookies.Where(c => !c.IsExpired(nowUtc) && c.MatchesHost(host));

        // "name=value; name2=value2", the format used by the "cookie" setting of the indexers
        public string ToCookieHeader(string host, DateTime nowUtc) =>
            string.Join("; ", ValidCookiesFor(host, nowUtc).Select(c => c.Name + "=" + c.Value));
    }
}
