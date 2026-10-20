using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jackett.Mobile.Core.Http
{
    // Used in front of the native iOS transport (NSUrlSessionHandler), which runs with its own cookie
    // handling turned off so cookies never leak between indexers:
    //  - sends the cookies Jackett put in the CookieContainer as a Cookie header
    //  - splits Set-Cookie headers that the native stack may have merged into one comma separated value
    public class CookieBridgeHandler : DelegatingHandler
    {
        private readonly CookieContainer _cookies;

        public CookieBridgeHandler(CookieContainer cookies) => _cookies = cookies;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var containerCookies = _cookies.GetCookieHeader(request.RequestUri);
            if (!string.IsNullOrEmpty(containerCookies))
            {
                var existing = request.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : null;
                request.Headers.Remove("Cookie");
                request.Headers.TryAddWithoutValidation(
                    "Cookie", string.IsNullOrEmpty(existing) ? containerCookies : existing + "; " + containerCookies);
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                var original = setCookies.ToList();
                var split = original.SelectMany(SetCookieSplitter.Split).ToList();
                if (split.Count != original.Count)
                {
                    response.Headers.Remove("Set-Cookie");
                    foreach (var value in split)
                        response.Headers.TryAddWithoutValidation("Set-Cookie", value);
                }
            }

            return response;
        }
    }
}
