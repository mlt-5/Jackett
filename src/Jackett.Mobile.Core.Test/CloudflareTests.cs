using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Jackett.Mobile.Core.Cloudflare;
using Jackett.Mobile.Core.Http;
using NUnit.Framework;

namespace Jackett.Mobile.Core.Test
{
    [TestFixture]
    public class CloudflareTests
    {
        private TempFolder _temp;

        [SetUp]
        public void SetUp() => _temp = new TempFolder();

        [TearDown]
        public void TearDown() => _temp.Dispose();

        private ClearanceStore NewStore() => new ClearanceStore(Path.Combine(_temp.Path, "cookies.json"));

        [Test]
        public void DetectsChallengePage()
        {
            var response = Responses.CloudflareChallenge();
            Assert.That(ChallengeDetector.IsChallenge(response, Responses.ChallengeHtml), Is.True);
        }

        [Test]
        public void DetectsChallengeFromCfMitigatedHeader()
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.TryAddWithoutValidation("cf-mitigated", "challenge");
            Assert.That(ChallengeDetector.IsChallenge(response, ""), Is.True);
        }

        [Test]
        public void IgnoresNormalCloudflarePages()
        {
            // normal pages also load scripts from /cdn-cgi/challenge-platform/
            const string body = "<html><title>Login</title><script src=\"/cdn-cgi/challenge-platform/scripts/jsd/main.js\"></script></html>";
            var forbidden = new HttpResponseMessage(HttpStatusCode.Forbidden);
            forbidden.Headers.TryAddWithoutValidation("Server", "cloudflare");
            Assert.That(ChallengeDetector.IsChallenge(forbidden, body), Is.False);

            var ok = Responses.Ok(Responses.ChallengeHtml);
            ok.Headers.TryAddWithoutValidation("Server", "cloudflare");
            Assert.That(ChallengeDetector.IsChallenge(ok, Responses.ChallengeHtml), Is.False);
        }

        [Test]
        public void StoreFindsClearanceOfParentDomainAndPersistsIt()
        {
            var store = NewStore();
            store.Save(Responses.Clearance("example.org"));

            Assert.That(store.Find("www.example.org"), Is.Not.Null);
            Assert.That(store.Find("example.org"), Is.Not.Null);
            Assert.That(store.Find("other.org"), Is.Null);

            var reloaded = NewStore();
            Assert.That(reloaded.Find("www.example.org")?.UserAgent, Is.EqualTo("Mozilla/5.0 (iPhone) Safari"));
        }

        [Test]
        public void StoreIgnoresExpiredCookies()
        {
            var store = NewStore();
            store.Save(Responses.Clearance("example.org", DateTime.UtcNow.AddMinutes(-1)));
            Assert.That(store.Find("example.org"), Is.Null);
        }

        [Test]
        public async Task HandlerSolvesChallengeAndRetriesWithCookieAndUserAgent()
        {
            var browser = new FakeBrowser { Solve = url => Responses.Clearance(url.Host) };
            var coordinator = new BrowserChallengeCoordinator(NewStore(), () => browser, () => true);
            var cookies = new CookieContainer();
            var transport = new FakeTransport(request =>
                cookies.GetCookieHeader(request.RequestUri).Contains("cf_clearance=solved")
                    ? Responses.Ok()
                    : Responses.CloudflareChallenge());
            var client = new HttpClient(new BrowserChallengeHandler(cookies, coordinator, null) { InnerHandler = transport });

            var response = await client.GetAsync("https://tracker.example/search?q=test");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(browser.SolveCalls, Is.EqualTo(1));
            Assert.That(transport.Requests.Count, Is.EqualTo(2));
            Assert.That(transport.Requests[1].Headers.UserAgent.ToString(), Is.EqualTo("Mozilla/5.0 (iPhone) Safari"));

            // the next request reuses the saved clearance without asking again
            transport.Requests.Clear();
            response = await client.GetAsync("https://tracker.example/search?q=other");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(transport.Requests.Count, Is.EqualTo(1));
            Assert.That(browser.SolveCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task HandlerDoesNotAskAgainAfterUserCancelled()
        {
            var browser = new FakeBrowser { Solve = _ => null };
            var coordinator = new BrowserChallengeCoordinator(NewStore(), () => browser, () => true);
            var transport = new FakeTransport(_ => Responses.CloudflareChallenge());
            var client = new HttpClient(new BrowserChallengeHandler(new CookieContainer(), coordinator, null) { InnerHandler = transport });

            var first = await client.GetAsync("https://tracker.example/");
            var second = await client.GetAsync("https://tracker.example/");

            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await first.Content.ReadAsStringAsync(), Does.Contain("Just a moment"));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(browser.SolveCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task HandlerDoesNothingWhenDisabled()
        {
            var browser = new FakeBrowser { Solve = url => Responses.Clearance(url.Host) };
            var coordinator = new BrowserChallengeCoordinator(NewStore(), () => browser, () => false);
            var client = new HttpClient(new BrowserChallengeHandler(new CookieContainer(), coordinator, null)
            {
                InnerHandler = new FakeTransport(_ => Responses.CloudflareChallenge())
            });

            var response = await client.GetAsync("https://tracker.example/");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(browser.SolveCalls, Is.EqualTo(0));
        }

        [Test]
        public async Task HandlerResendsPostBody()
        {
            var browser = new FakeBrowser { Solve = url => Responses.Clearance(url.Host) };
            var coordinator = new BrowserChallengeCoordinator(NewStore(), () => browser, () => true);
            var cookies = new CookieContainer();
            string retriedBody = null;
            var transport = new FakeTransport(request =>
            {
                if (!cookies.GetCookieHeader(request.RequestUri).Contains("cf_clearance"))
                    return Responses.CloudflareChallenge();
                retriedBody = request.Content.ReadAsStringAsync().Result;
                return Responses.Ok();
            });
            var client = new HttpClient(new BrowserChallengeHandler(cookies, coordinator, null) { InnerHandler = transport });

            await client.PostAsync("https://tracker.example/login", new StringContent("user=a&pass=b"));

            Assert.That(retriedBody, Is.EqualTo("user=a&pass=b"));
        }

        [Test]
        public async Task RetryDoesNotResendStaleCookieHeader()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://tracker.example/");
            request.Headers.TryAddWithoutValidation("Cookie", "cf_clearance=expired");
            request.Headers.TryAddWithoutValidation("Referer", "https://tracker.example/");

            var clone = await BrowserChallengeHandler.CloneAsync(request);

            Assert.That(clone.Headers.Contains("Cookie"), Is.False);
            Assert.That(clone.Headers.Referrer, Is.EqualTo(new Uri("https://tracker.example/")));
        }

        [Test]
        public async Task HandlerStopsAskingWhenTheSiteRejectsTheBrowserCookies()
        {
            var browser = new FakeBrowser { Solve = url => Responses.Clearance(url.Host) };
            var coordinator = new BrowserChallengeCoordinator(NewStore(), () => browser, () => true);
            var transport = new FakeTransport(_ => Responses.CloudflareChallenge());
            var client = new HttpClient(new BrowserChallengeHandler(new CookieContainer(), coordinator, null) { InnerHandler = transport });

            await client.GetAsync("https://tracker.example/");
            await client.GetAsync("https://tracker.example/");
            await client.GetAsync("https://tracker.example/");

            Assert.That(browser.SolveCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task TimedOutRequestDoesNotCountAsDeclined()
        {
            var browser = new FakeBrowser { Solve = _ => null };
            var coordinator = new BrowserChallengeCoordinator(NewStore(), () => browser, () => true);
            using var cancelled = new System.Threading.CancellationTokenSource();

            browser.Solve = _ =>
            {
                cancelled.Cancel();
                return null;
            };
            await coordinator.GetClearanceAsync(new Uri("https://tracker.example/"), DateTime.UtcNow, cancelled.Token);

            browser.Solve = url => Responses.Clearance(url.Host);
            var clearance = await coordinator.GetClearanceAsync(new Uri("https://tracker.example/"), DateTime.UtcNow, default);

            Assert.That(clearance, Is.Not.Null);
            Assert.That(browser.SolveCalls, Is.EqualTo(2));
        }

        [Test]
        public void CookieHeaderIncludesSubdomainsAndPrefersTheMostSpecificDomain()
        {
            var clearance = new BrowserClearance
            {
                Host = "tracker.example",
                Cookies =
                {
                    new BrowserCookie { Name = "uid", Value = "parent", Domain = ".tracker.example" },
                    new BrowserCookie { Name = "uid", Value = "www", Domain = "www.tracker.example" },
                    new BrowserCookie { Name = "pass", Value = "p", Domain = "www.tracker.example" },
                    new BrowserCookie { Name = "ad", Value = "x", Domain = "ads.example" }
                }
            };

            Assert.That(clearance.ToCookieHeader("tracker.example", DateTime.UtcNow), Is.EqualTo("uid=www; pass=p"));
        }

        [Test]
        public async Task SharedTransportIsCreatedOnceAndNotDisposedWithClients()
        {
            var created = 0;
            FakeTransport shared = null;
            Func<HttpMessageHandler> getShared = () =>
            {
                if (shared == null)
                {
                    created++;
                    shared = new FakeTransport(_ => Responses.Ok());
                }
                return shared;
            };

            var first = new HttpClient(new SharedHandlerAdapter(getShared));
            var second = new HttpClient(new SharedHandlerAdapter(getShared));
            Assert.That(created, Is.EqualTo(0), "created lazily");

            await first.GetAsync("https://a.example/");
            first.Dispose();
            await second.GetAsync("https://b.example/");

            Assert.That(created, Is.EqualTo(1));
            Assert.That(shared.Requests.Count, Is.EqualTo(2));
        }

        [Test]
        public void SplitsMergedSetCookieHeaders()
        {
            const string merged = "a=1; expires=Wed, 21 Oct 2026 07:28:00 GMT; path=/, b=2; HttpOnly,c=3";
            var cookies = SetCookieSplitter.Split(merged).ToList();
            Assert.That(cookies, Is.EqualTo(new[] { "a=1; expires=Wed, 21 Oct 2026 07:28:00 GMT; path=/", "b=2; HttpOnly", "c=3" }));
        }

        [Test]
        public async Task CookieBridgeSendsContainerCookiesAndSplitsSetCookie()
        {
            var cookies = new CookieContainer();
            cookies.Add(new Uri("https://tracker.example"), new Cookie("session", "abc"));
            var transport = new FakeTransport(_ =>
            {
                var response = Responses.Ok();
                response.Headers.TryAddWithoutValidation("Set-Cookie", "a=1; path=/, b=2");
                return response;
            });
            var client = new HttpClient(new CookieBridgeHandler(cookies) { InnerHandler = transport });

            var result = await client.GetAsync("https://tracker.example/");

            Assert.That(transport.Requests[0].Headers.GetValues("Cookie").Single(), Is.EqualTo("session=abc"));
            Assert.That(result.Headers.GetValues("Set-Cookie"), Is.EqualTo(new[] { "a=1; path=/", "b=2" }));
        }
    }
}
