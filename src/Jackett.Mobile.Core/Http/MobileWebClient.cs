using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
        private static readonly object TransportLock = new object();
        private static Func<HttpMessageHandler> _nativeTransportFactory;
        private static HttpMessageHandler _sharedNativeTransport;

        public static BrowserChallengeCoordinator Challenges { get; private set; }

        public static bool UseNativeTransport { get; private set; }

        public static bool HasNativeTransport => _nativeTransportFactory != null;

        // nativeTransportFactory creates the native transport (NSUrlSessionHandler on iOS).
        // Null means the managed .NET handler is used.
        public static void Configure(BrowserChallengeCoordinator challenges, Func<HttpMessageHandler> nativeTransportFactory, bool useNativeTransport)
        {
            lock (TransportLock)
            {
                Challenges = challenges;
                _nativeTransportFactory = nativeTransportFactory;
                UseNativeTransport = useNativeTransport;
                _sharedNativeTransport = null;
            }
        }

        // One native session for every indexer: it keeps no cookies (see CookieBridgeHandler), so sharing it
        // is safe, and it avoids opening ~650 NSUrlSessions at startup. Created on the first request.
        internal static HttpMessageHandler SharedNativeTransport()
        {
            lock (TransportLock)
                return _sharedNativeTransport ??= _nativeTransportFactory();
        }
    }

    // Sends through a handler shared by many HttpClients, without disposing it with them.
    internal class SharedHandlerAdapter : HttpMessageHandler
    {
        private readonly Func<HttpMessageHandler> _getShared;
        private HttpMessageInvoker _invoker;

        public SharedHandlerAdapter(Func<HttpMessageHandler> getShared) => _getShared = getShared;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _invoker ??= new HttpMessageInvoker(_getShared(), disposeHandler: false);
            return _invoker.SendAsync(request, cancellationToken);
        }
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
            if (MobileNetworking.UseNativeTransport && MobileNetworking.HasNativeTransport)
                transport = new CookieBridgeHandler(cookieContainer) { InnerHandler = new SharedHandlerAdapter(MobileNetworking.SharedNativeTransport) };

            if (MobileNetworking.Challenges == null)
                return transport;

            return new BrowserChallengeHandler(cookieContainer, MobileNetworking.Challenges, logger) { InnerHandler = transport };
        }
    }
}
