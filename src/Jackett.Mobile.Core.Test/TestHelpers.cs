using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jackett.Mobile.Core.Cloudflare;

namespace Jackett.Mobile.Core.Test
{
    internal class FakeTransport : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeTransport(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }

    internal class FakeBrowser : IBrowserSolver
    {
        public string UserAgent => "Mozilla/5.0 (iPhone) Safari";

        public Func<Uri, BrowserClearance> Solve { get; set; }

        public int SolveCalls { get; private set; }

        public Task<BrowserClearance> SolveChallengeAsync(Uri url, CancellationToken cancellationToken)
        {
            SolveCalls++;
            return Task.FromResult(Solve?.Invoke(url));
        }

        public Task<BrowserClearance> LoginAsync(Uri url, CancellationToken cancellationToken) =>
            Task.FromResult(Solve?.Invoke(url));
    }

    internal static class Responses
    {
        public const string ChallengeHtml =
            "<!DOCTYPE html><html><head><title>Just a moment...</title></head><body><script>window._cf_chl_opt={}</script></body></html>";

        public static HttpResponseMessage CloudflareChallenge()
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(ChallengeHtml) };
            response.Headers.TryAddWithoutValidation("Server", "cloudflare");
            return response;
        }

        public static HttpResponseMessage Ok(string body = "<html>results</html>") =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };

        public static BrowserClearance Clearance(string host, DateTime? expires = null) =>
            new BrowserClearance
            {
                Host = host,
                UserAgent = "Mozilla/5.0 (iPhone) Safari",
                Cookies =
                {
                    new BrowserCookie { Name = "cf_clearance", Value = "solved", Domain = "." + host, ExpiresUtc = expires ?? DateTime.UtcNow.AddHours(1) }
                }
            };
    }

    internal sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jackett-mobile-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
