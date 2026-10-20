using System.Collections.Generic;
using System.IO;
using Jackett.Common.Models.Config;
using Jackett.Common.Services.Interfaces;

namespace Jackett.Mobile.Core.Services
{
    // Wraps the server ConfigurationService and replaces the parts that rely on the install folder,
    // which does not exist as such inside an iOS app bundle.
    public class MobileConfigurationService : IConfigurationService
    {
        private readonly IConfigurationService _inner;
        private readonly MobileHostOptions _options;

        public MobileConfigurationService(IConfigurationService inner, MobileHostOptions options)
        {
            _inner = inner;
            _options = options;
        }

        // Definitions the user copied with the Files app. Listed first so they override the bundled ones.
        public string CustomDefinitionsFolder =>
            string.IsNullOrEmpty(_options.CustomDefinitionsFolder)
                ? Path.Combine(GetAppDataFolder(), "Definitions")
                : _options.CustomDefinitionsFolder;

        public List<string> GetCardigannDefinitionsFolders() =>
            new List<string> { CustomDefinitionsFolder, _options.BundledDefinitionsFolder };

        public string GetVersion() => _options.AppVersion;

        public string ApplicationFolder() => Path.GetDirectoryName(_options.BundledDefinitionsFolder);

        public string GetContentFolder() => Path.Combine(ApplicationFolder(), "Content");

        public void CreateOrMigrateSettings()
        {
            Directory.CreateDirectory(GetAppDataFolder());
            Directory.CreateDirectory(CustomDefinitionsFolder);
        }

        // there is nothing to migrate from inside the app sandbox
        public void PerformMigration(string oldDirectory)
        {
        }

        public string GetIndexerConfigDir() => _inner.GetIndexerConfigDir();
        public string GetAppDataFolder() => _inner.GetAppDataFolder();
        public string GetSonarrConfigFile() => _inner.GetSonarrConfigFile();
        public T GetConfig<T>() => _inner.GetConfig<T>();
        public void SaveConfig<T>(T config) => _inner.SaveConfig(config);
        public ServerConfig BuildServerConfig(RuntimeSettings runtimeSettings) => _inner.BuildServerConfig(runtimeSettings);
    }
}
