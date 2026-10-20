using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Jackett.Mobile.Core.Cloudflare
{
    // Sits in front of the transport of every indexer:
    //  - adds the cookies and User-Agent saved from the in-app browser for the host
    //  - when the response is a challenge page, asks the browser to pass it and retries once
    public class BrowserChallengeHandler : DelegatingHandler
    {
        private readonly CookieContainer _cookies;
        private readonly BrowserChallengeCoordinator _coordinator;
        private readonly Logger _logger;

        public BrowserChallengeHandler(CookieContainer cookies, BrowserChallengeCoordinator coordinator, Logger logger)
        {
            _cookies = cookies;
            _coordinator = coordinator;
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var startedUtc = DateTime.UtcNow;
            ApplyClearance(request, _coordinator.Store.Find(request.RequestUri.Host));

            // keep the body so the request can be sent again
            if (request.Content != null)
                await request.Content.LoadIntoBufferAsync();

            var response = await base.SendAsync(request, cancellationToken);
            if (!ChallengeDetector.MightBeChallenge(response))
                return response;

            await response.Content.LoadIntoBufferAsync();
            var body = await response.Content.ReadAsStringAsync();
            if (!ChallengeDetector.IsChallenge(response, body))
                return response;

            _logger?.Info($"Challenge page detected for {request.RequestUri.Host}, opening the in-app browser");

            var clearance = await _coordinator.GetClearanceAsync(request.RequestUri, startedUtc, cancellationToken);
            if (clearance == null)
            {
                _logger?.Info($"Challenge for {request.RequestUri.Host} was not solved");
                return response;
            }

            var retry = await CloneAsync(request);
            ApplyClearance(retry, clearance);
            response.Dispose();
            var retryResponse = await base.SendAsync(retry, cancellationToken);

            // The site refused the browser's cookies (e.g. it fingerprints the connection, not only the cookie).
            // Stop here, or every following request would open the browser again.
            if (ChallengeDetector.MightBeChallenge(retryResponse))
            {
                await retryResponse.Content.LoadIntoBufferAsync();
                if (ChallengeDetector.IsChallenge(retryResponse, await retryResponse.Content.ReadAsStringAsync()))
                {
                    _coordinator.MarkRejected(request.RequestUri.Host);
                    _logger?.Warn($"{request.RequestUri.Host} still answers with a challenge after it was solved in the browser. " +
                                  "Try switching \"Use iOS networking\" in the settings, or set up FlareSolverr.");
                }
            }

            return retryResponse;
        }

        private void ApplyClearance(HttpRequestMessage request, BrowserClearance clearance)
        {
            if (clearance == null)
                return;

            var uri = request.RequestUri;
            var cookieUri = new Uri(uri.Scheme + "://" + uri.Host);
            foreach (var cookie in clearance.ValidCookiesFor(uri.Host, DateTime.UtcNow))
                _cookies.Add(cookieUri, new Cookie(cookie.Name, cookie.Value));

            // cf_clearance only works with the User-Agent that obtained it
            if (!string.IsNullOrEmpty(clearance.UserAgent))
            {
                request.Headers.Remove("User-Agent");
                request.Headers.TryAddWithoutValidation("User-Agent", clearance.UserAgent);
            }
        }

        internal static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version
            };

            // the Cookie header is rebuilt from the CookieContainer, copying it would resend stale cookies
            foreach (var header in request.Headers.Where(h => !h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)))
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (request.Content != null)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync();
                clone.Content = new ByteArrayContent(bytes);
                foreach (var header in request.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            foreach (var option in request.Options)
                ((IDictionary<string, object>)clone.Options)[option.Key] = option.Value;

            return clone;
        }
    }
}
