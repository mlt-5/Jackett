using System;
using System.Collections.Generic;
using System.Linq;
using Jackett.Common.Models;

namespace Jackett.Mobile.Core.Models
{
    public class SearchRequest
    {
        public string Query { get; set; }

        // Torznab category ids (2000 = Movies, 5000 = TV...). Empty searches every category.
        public int[] Categories { get; set; } = Array.Empty<int>();

        // Indexer ids to search. Null searches every configured indexer.
        public IReadOnlyCollection<string> IndexerIds { get; set; }
    }

    public class IndexerSearchStatus
    {
        public string IndexerId { get; set; }
        public string IndexerName { get; set; }
        public bool Succeeded { get; set; }
        public int ResultCount { get; set; }
        public long ElapsedMs { get; set; }
        public bool FromCache { get; set; }
        public string Error { get; set; }
    }

    public class SearchResult
    {
        public string Title { get; set; }
        public string IndexerId { get; set; }
        public string IndexerName { get; set; }
        public string IndexerType { get; set; }
        public long? Size { get; set; }
        public long? Seeders { get; set; }
        public long? Peers { get; set; }
        public long? Grabs { get; set; }
        public DateTime PublishDate { get; set; }
        public string Category { get; set; }
        public Uri Link { get; set; }
        public Uri MagnetUri { get; set; }
        public Uri Details { get; set; }
        public Uri Poster { get; set; }
        public string InfoHash { get; set; }
        public double? DownloadVolumeFactor { get; set; }

        public bool IsFreeleech => DownloadVolumeFactor == 0;

        public bool HasMagnet => MagnetUri != null || (Link != null && Link.Scheme == "magnet");

        public bool HasTorrentFile => Link != null && Link.Scheme != "magnet";

        public Uri BestMagnet => MagnetUri ?? (Link != null && Link.Scheme == "magnet" ? Link : null);

        public string SizeText => FormatSize(Size);

        public static string FormatSize(long? bytes)
        {
            if (!bytes.HasValue)
                return "?";

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes.Value;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.##} {units[unit]}";
        }

        public static SearchResult FromRelease(ReleaseInfo release, string indexerId, string indexerName, string indexerType) =>
            new SearchResult
            {
                Title = release.Title,
                IndexerId = indexerId,
                IndexerName = indexerName,
                IndexerType = indexerType,
                Size = release.Size,
                Seeders = release.Seeders,
                Peers = release.Peers,
                Grabs = release.Grabs,
                PublishDate = release.PublishDate,
                Category = release.Category == null
                    ? null
                    : string.Join(", ", release.Category.Where(c => c < 100000).Select(TorznabCatType.GetCatDesc).Where(d => !string.IsNullOrEmpty(d)).Distinct()),
                Link = release.Link,
                MagnetUri = release.MagnetUri,
                Details = release.Details,
                Poster = release.Poster,
                InfoHash = release.InfoHash,
                DownloadVolumeFactor = release.DownloadVolumeFactor
            };
    }

    public class SearchResponse
    {
        public List<SearchResult> Results { get; set; } = new List<SearchResult>();
        public List<IndexerSearchStatus> Indexers { get; set; } = new List<IndexerSearchStatus>();
    }

    public enum ResultSort
    {
        Seeders,
        Date,
        Size,
        Title
    }
}
