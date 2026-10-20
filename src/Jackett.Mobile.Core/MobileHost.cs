using System;
using System.IO;
using System.Text;
using Autofac;
using Jackett.Common.Models.Config;
using Jackett.Common.Plumbing;
using Jackett.Common.Services;
using Jackett.Common.Services.Interfaces;
using Jackett.Common.Utils;
using Jackett.Common.Utils.Clients;
using Jackett.Mobile.Core.Cloudflare;
using Jackett.Mobile.Core.Http;
using Jackett.Mobile.Core.Services;
using NLog;

namespace Jackett.Mobile.Core
{
    // Runs Jackett.Common inside the app: the same services as Jackett.Server, minus ASP.NET.
    public sealed class MobileHost : IDisposable
    {
        private readonly IContainer _container;

        private MobileHost(IContainer container, MobileHostOptions options, MobileSettings settings, BrowserChallengeCoordinator challenges)
        {
            _container = container;
            Options = options;
            Settings = settings;
            Challenges = challenges;
            Logger = container.Resolve<Logger>();
            ServerConfig = container.Resolve<ServerConfig>();
            ConfigService = container.Resolve<IConfigurationService>();
            IndexerManager = container.Resolve<IIndexerManagerService>();
            CacheService = container.Resolve<ICacheService>();
            Search = new SearchService(IndexerManager, Logger);
            Downloads = new DownloadService(IndexerManager);
            Setup = new IndexerSetupService(IndexerManager, CacheService, challenges.Store, options.BrowserSolver, Logger);
        }

        public MobileHostOptions Options { get; }
        public MobileSettings Settings { get; }
        public BrowserChallengeCoordinator Challenges { get; }
        public Logger Logger { get; }
        public ServerConfig ServerConfig { get; }
        public IConfigurationService ConfigService { get; }
        public IIndexerManagerService IndexerManager { get; }
        public ICacheService CacheService { get; }
        public SearchService Search { get; }
        public DownloadService Downloads { get; }
        public IndexerSetupService Setup { get; }

        public string CustomDefinitionsFolder => ((MobileConfigurationService)ConfigService).CustomDefinitionsFolder;

        // Loads the ~650 indexers, so call it off the UI thread.
        public static MobileHost Start(MobileHostOptions options)
        {
            if (string.IsNullOrEmpty(options.DataFolder))
                throw new ArgumentException("DataFolder is required", nameof(options));

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Directory.CreateDirectory(options.DataFolder);

            var runtimeSettings = new RuntimeSettings
            {
                CustomDataFolder = options.DataFolder,
                ClientOverride = "httpclient2",
                TracingEnabled = options.TracingEnabled,
                NoUpdates = true,
                NoRestart = true
            };

            LogManager.Configuration = LoggingSetup.GetLoggingConfiguration(runtimeSettings, fileOnly: true);
            var logger = LogManager.GetLogger("Jackett");
            logger.Info($"Starting Jackett mobile {options.AppVersion}");

            var settings = MobileSettings.Load(Path.Combine(options.DataFolder, "mobile-settings.json"));
            var store = new ClearanceStore(Path.Combine(options.DataFolder, "browser-cookies.json"));
            var challenges = new BrowserChallengeCoordinator(store, () => options.BrowserSolver, () => settings.SolveChallengesInBrowser);

            // must be set before the first web client is created
            MobileNetworking.Challenges = challenges;
            MobileNetworking.NativeTransportFactory = options.NativeTransportFactory;
            MobileNetworking.UseNativeTransport = settings.UseNativeNetworking;

            var configService = new MobileConfigurationService(
                new ConfigurationService(new SerializeService(), new ProcessService(logger), logger, runtimeSettings), options);
            configService.CreateOrMigrateSettings();

            var protectionKey = options.ProtectionKey
                                ?? MobileProtectionService.LoadOrCreateKeyFile(Path.Combine(options.DataFolder, "protection.key"));

            var builder = new ContainerBuilder();
            builder.RegisterModule(new JackettModule(runtimeSettings));
            // registered after the module, so these win over the server implementations
            builder.RegisterInstance(logger).SingleInstance();
            builder.RegisterInstance(configService).As<IConfigurationService>().SingleInstance();
            builder.RegisterInstance(new MobileProtectionService(protectionKey)).As<IProtectionService>().SingleInstance();
            builder.RegisterType<CacheService>().As<ICacheService>().SingleInstance();
            builder.RegisterType<MobileWebClient>().As<WebClient>();
            var container = builder.Build();

            var indexerManager = container.Resolve<IIndexerManagerService>();
            indexerManager.InitIndexers(configService.GetCardigannDefinitionsFolders());

            return new MobileHost(container, options, settings, challenges);
        }

        public void Dispose()
        {
            _container.Dispose();
            LogManager.Shutdown();
        }
    }
}
