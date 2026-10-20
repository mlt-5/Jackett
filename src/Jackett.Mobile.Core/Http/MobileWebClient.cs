using System;
using System.Net;
using System.Net.Http;
using Jackett.Common.Models.Config;
using Jackett.Common.Services.Interfaces;
using Jackett.Common.Utils.Clients;
using Jackett.Mobile.Core.Cloudflare;
using NLog;

namespace Jackett.Mobile.Core.Http
{
    // Platform pieces the web clients need. Static because IndexerManagerService creates one web client per
    // indexer through Activator with a fixed constructor, so they cannot be injected.
    public static class MobileNetworking
    {
        public static BrowserChallengeCoordinator Challenges { get; set; }

        // Creates the native transport (NSUrlSessionHandler on iOS). Null means the managed .NET handler is used.
        public static Func<HttpMessageHandler> NativeTransportFactory { get; set; }

        public static bool UseNativeTransport { get; set; }
    }

    // HttpWebClient2 with the in-app browser challenge solving and, optionally, the native iOS network stack.
    public class MobileWebClient : HttpWebClient2
    {
        public MobileWebClient(IProcessService p, Logger l, IConfigurationService c, ServerConfig sc)
            : base(p, l, c, sc)
        {
        }

        protected override HttpMessageHandler CreateInnerHandler(HttpClientHandler handler, CookieContainer cookieContainer)
        {
            HttpMessageHandler transport = handler;
            if (MobileNetworking.UseNativeTransport && MobileNetworking.NativeTransportFactory != null)
                transport = new CookieBridgeHandler(cookieContainer) { InnerHandler = MobileNetworking.NativeTransportFactory() };

            if (MobileNetworking.Challenges == null)
                return transport;

            return new BrowserChallengeHandler(cookieContainer, MobileNetworking.Challenges, logger) { InnerHandler = transport };
        }
    }
}
