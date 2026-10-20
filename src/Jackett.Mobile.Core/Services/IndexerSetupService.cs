using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jackett.Common.Indexers;
using Jackett.Common.Models;
using Jackett.Common.Services.Interfaces;
using Jackett.Mobile.Core.Cloudflare;
using Jackett.Mobile.Core.Models;
using NLog;

namespace Jackett.Mobile.Core.Services
{
    // Adding, configuring, testing and removing indexers, like IndexerApiController in the server.
    public class IndexerSetupService
    {
        private readonly IIndexerManagerService _indexerManager;
        private readonly ICacheService _cacheService;
        private readonly ClearanceStore _clearanceStore;
        private readonly IBrowserSolver _browser;
        private readonly Logger _logger;

        public IndexerSetupService(IIndexerManagerService indexerManager, ICacheService cacheService,
                                   ClearanceStore clearanceStore, IBrowserSolver browser, Logger logger)
        {
            _indexerManager = indexerManager;
            _cacheService = cacheService;
            _clearanceStore = clearanceStore;
            _browser = browser;
            _logger = logger;
        }

        public bool CanLogInWithBrowser => _browser != null;

        public IReadOnlyList<IndexerSummary> GetIndexers() =>
            _indexerManager.GetAllIndexers().Select(IndexerSummary.From).ToList();

        public IndexerSummary GetIndexer(string id) => IndexerSummary.From(_indexerManager.GetIndexer(id));

        public async Task<List<ConfigField>> GetFormAsync(string id)
        {
            var config = await _indexerManager.GetIndexer(id).GetConfigurationForSetup();
            return ConfigField.ParseAll(config.ToJson(null));
        }

        // Applies the form and tests the indexer. Throws with a readable message when it fails.
        public async Task SaveAsync(string id, IEnumerable<ConfigField> fields)
        {
            var indexer = _indexerManager.GetIndexer(id);
            _cacheService.CleanIndexerCache(indexer);

            try
            {
                var status = await indexer.ApplyConfiguration(ConfigField.ToSubmission(fields));
                if (status == IndexerConfigurationStatus.RequiresTesting)
                    await _indexerManager.TestIndexer(indexer.Id);
                indexer.LastError = null;
            }
            catch (Exception e)
            {
                (indexer as BaseIndexer)?.ResetBaseConfig();
                _logger.Error(e, $"Configuration of {id} failed");
                throw new InvalidOperationException(SearchService.DescribeError(e), e);
            }
        }

        public async Task TestAsync(string id)
        {
            var indexer = _indexerManager.GetIndexer(id);
            try
            {
                await _indexerManager.TestIndexer(id);
                indexer.LastError = null;
            }
            catch (Exception e)
            {
                indexer.LastError = SearchService.DescribeError(e);
                throw new InvalidOperationException(indexer.LastError, e);
            }
        }

        public void Delete(string id)
        {
            _cacheService.CleanIndexerCache(_indexerManager.GetIndexer(id));
            _indexerManager.DeleteIndexer(id);
        }

        // For indexers that log in with a cookie: the user logs in on the real site (captchas included)
        // and the cookie and User-Agent fields are filled from the in-app browser.
        // Returns false if the user cancelled.
        public async Task<bool> LogInWithBrowserAsync(string id, IList<ConfigField> fields, CancellationToken cancellationToken = default)
        {
            if (_browser == null)
                throw new InvalidOperationException("The in-app browser is not available");

            var indexer = _indexerManager.GetIndexer(id);
            var siteLink = fields.FirstOrDefault(f => f.Id == "sitelink")?.Value;
            var url = new Uri(string.IsNullOrWhiteSpace(siteLink) ? indexer.SiteLink : siteLink);

            var clearance = await _browser.LoginAsync(url, cancellationToken);
            if (clearance == null)
                return false;

            if (string.IsNullOrEmpty(clearance.Host))
                clearance.Host = url.Host;
            if (string.IsNullOrEmpty(clearance.UserAgent))
                clearance.UserAgent = _browser.UserAgent;

            ApplyBrowserLogin(fields, clearance, url.Host, DateTime.UtcNow);

            // keeps any cf_clearance for the searches, together with its User-Agent
            if (clearance.HasCloudflareCookie(DateTime.UtcNow))
                _clearanceStore.Save(clearance);

            return true;
        }

        public static void ApplyBrowserLogin(IList<ConfigField> fields, BrowserClearance clearance, string host, DateTime nowUtc)
        {
            var cookieField = fields.FirstOrDefault(f => f.IsCookie);
            if (cookieField != null)
                cookieField.Value = clearance.ToCookieHeader(host, nowUtc);

            var userAgentField = fields.FirstOrDefault(f => f.IsUserAgent);
            if (userAgentField != null && !string.IsNullOrEmpty(clearance.UserAgent))
                userAgentField.Value = clearance.UserAgent;
        }
    }
}
