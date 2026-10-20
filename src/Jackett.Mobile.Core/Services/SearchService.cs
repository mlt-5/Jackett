using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jackett.Common;
using Jackett.Common.Indexers;
using Jackett.Common.Models;
using Jackett.Common.Services.Interfaces;
using Jackett.Mobile.Core.Models;
using NLog;

namespace Jackett.Mobile.Core.Services
{
    // Searches several indexers at once, like the manual search of the web UI (ResultsController).
    public class SearchService
    {
        private readonly IIndexerManagerService _indexerManager;
        private readonly Logger _logger;

        public SearchService(IIndexerManagerService indexerManager, Logger logger)
        {
            _indexerManager = indexerManager;
            _logger = logger;
        }

        public async Task<SearchResponse> SearchAsync(SearchRequest request, TimeSpan timeout,
                                                      IProgress<IndexerSearchStatus> progress = null,
                                                      CancellationToken cancellationToken = default)
        {
            var query = new TorznabQuery
            {
                QueryType = "search",
                SearchTerm = request.Query ?? string.Empty,
                Categories = request.Categories ?? Array.Empty<int>(),
                InteractiveSearch = true
            };

            var indexers = _indexerManager.GetAllIndexers()
                                          .Where(i => i.IsConfigured)
                                          .Where(i => request.IndexerIds == null || request.IndexerIds.Contains(i.Id))
                                          .Where(i => i.CanHandleQuery(query))
                                          .ToList();

            var isMetaSearch = indexers.Count > 1;
            var searches = indexers.Select(i => SearchIndexerAsync(i, query, isMetaSearch, timeout, progress, cancellationToken)).ToList();
            var outcomes = await Task.WhenAll(searches);

            var response = new SearchResponse();
            foreach (var (status, results) in outcomes)
            {
                response.Indexers.Add(status);
                response.Results.AddRange(results);
            }

            response.Results = Sort(response.Results, ResultSort.Seeders).ToList();
            _logger.Info($"Mobile search for '{query.SearchTerm}' in {indexers.Count} indexers => {response.Results.Count} releases");
            return response;
        }

        public static IEnumerable<SearchResult> Sort(IEnumerable<SearchResult> results, ResultSort sort) =>
            sort switch
            {
                ResultSort.Date => results.OrderByDescending(r => r.PublishDate),
                ResultSort.Size => results.OrderByDescending(r => r.Size ?? 0),
                ResultSort.Title => results.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase),
                _ => results.OrderByDescending(r => r.Seeders ?? -1).ThenByDescending(r => r.PublishDate)
            };

        private async Task<(IndexerSearchStatus, List<SearchResult>)> SearchIndexerAsync(
            IIndexer indexer, TorznabQuery query, bool isMetaSearch, TimeSpan timeout,
            IProgress<IndexerSearchStatus> progress, CancellationToken cancellationToken)
        {
            var status = new IndexerSearchStatus { IndexerId = indexer.Id, IndexerName = indexer.Name };
            var results = new List<SearchResult>();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var search = indexer.ResultsForQuery(query, isMetaSearch);
                var finished = await Task.WhenAny(search, Task.Delay(timeout, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();

                if (finished != search)
                {
                    // the indexer keeps running in the background, its results are ignored
                    status.Error = $"No answer after {timeout.TotalSeconds:0} seconds";
                }
                else
                {
                    var result = await search;
                    results.AddRange(result.Releases.Select(r => SearchResult.FromRelease(r, indexer.Id, indexer.Name, indexer.Type)));
                    status.Succeeded = true;
                    status.FromCache = result.IsFromCache;
                    status.ResultCount = results.Count;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                status.Error = DescribeError(e);
                _logger.Error(e, $"Search failed in {indexer.Id}");
            }

            status.ElapsedMs = stopwatch.ElapsedMilliseconds;
            progress?.Report(status);
            return (status, results);
        }

        internal static string DescribeError(Exception e)
        {
            var inner = e is IndexerException && e.InnerException != null ? e.InnerException : e;
            var message = inner.Message;
            if (inner.InnerException != null)
                message += ": " + inner.InnerException.Message;
            return message;
        }
    }
}
