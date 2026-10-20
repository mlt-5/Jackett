using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jackett.Common.Services.Interfaces;
using Jackett.Mobile.Core.Models;

namespace Jackett.Mobile.Core.Services
{
    public class DownloadOutcome
    {
        public Uri MagnetUri { get; set; }
        public byte[] TorrentBytes { get; set; }
        public string FileName { get; set; }

        public bool IsMagnet => MagnetUri != null;
    }

    // Fetches the .torrent of a result with the indexer's own session (cookies, login),
    // like DownloadController in the server.
    public class DownloadService
    {
        private readonly IIndexerManagerService _indexerManager;

        public DownloadService(IIndexerManagerService indexerManager) => _indexerManager = indexerManager;

        public async Task<DownloadOutcome> DownloadAsync(SearchResult result)
        {
            if (result.Link == null || result.Link.Scheme == "magnet")
            {
                var magnet = result.BestMagnet ?? throw new InvalidOperationException("This result has no download link");
                return new DownloadOutcome { MagnetUri = magnet };
            }

            var indexer = _indexerManager.GetWebIndexer(result.IndexerId);
            var bytes = await indexer.Download(result.Link);

            // some trackers redirect the download link to a magnet
            if (StartsWithMagnet(bytes))
                return new DownloadOutcome { MagnetUri = new Uri(Encoding.UTF8.GetString(bytes).Trim()) };

            return new DownloadOutcome { TorrentBytes = bytes, FileName = ToFileName(result.Title) + ".torrent" };
        }

        internal static bool StartsWithMagnet(byte[] bytes) =>
            bytes != null && bytes.Length >= 7 && Encoding.ASCII.GetString(bytes, 0, 7) == "magnet:";

        internal static string ToFileName(string title)
        {
            var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
            var name = new string((title ?? "download").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            if (name.Length > 120)
                name = name.Substring(0, 120);
            return name.Length == 0 ? "download" : name;
        }
    }
}
