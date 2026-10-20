using System;
using System.Collections.Generic;
using System.Linq;
using Jackett.Common.Indexers;
using Newtonsoft.Json.Linq;

namespace Jackett.Mobile.Core.Models
{
    public class IndexerSummary
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public string Language { get; set; }
        public string SiteLink { get; set; }
        public bool IsConfigured { get; set; }
        public string LastError { get; set; }
        public string[] Tags { get; set; }

        public bool HasError => !string.IsNullOrEmpty(LastError);

        public static IndexerSummary From(IIndexer indexer) =>
            new IndexerSummary
            {
                Id = indexer.Id,
                Name = indexer.Name,
                Description = indexer.Description,
                Type = indexer.Type,
                Language = indexer.Language,
                SiteLink = indexer.SiteLink,
                IsConfigured = indexer.IsConfigured,
                LastError = indexer.LastError,
                Tags = indexer.Tags ?? Array.Empty<string>()
            };
    }

    // One field of an indexer's setup form, as produced by ConfigurationData.ToJson.
    public class ConfigField
    {
        public const string TypeString = "inputstring";
        public const string TypePassword = "password";
        public const string TypeBool = "inputbool";
        public const string TypeSelect = "inputselect";
        public const string TypeCheckbox = "inputcheckbox";
        public const string TypeTags = "inputtags";
        public const string TypeInfo = "displayinfo";
        public const string TypeImage = "displayimage";

        public string Id { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
        public bool BoolValue { get; set; }
        public List<string> Values { get; set; } = new List<string>();

        // value => label
        public List<KeyValuePair<string, string>> Options { get; set; } = new List<KeyValuePair<string, string>>();

        public byte[] ImageBytes { get; set; }

        public bool IsDisplayOnly => Type == TypeInfo || Type == TypeImage;

        public bool IsCookie => Id == "cookie";

        public bool IsUserAgent => Id == "user-agent" || Id == "useragent";

        public static List<ConfigField> ParseAll(JToken json) =>
            ((JArray)json).OfType<JObject>().Select(Parse).ToList();

        public static ConfigField Parse(JObject item)
        {
            var field = new ConfigField
            {
                Id = item.Value<string>("id"),
                Type = item.Value<string>("type"),
                Name = item.Value<string>("name")
            };

            var value = item["value"];
            if (field.Type == TypeBool)
                field.BoolValue = value != null && value.Type == JTokenType.Boolean && value.Value<bool>();
            else if (field.Type == TypeImage)
                field.ImageBytes = DecodeDataUri(value?.Value<string>());
            else if (value != null && value.Type != JTokenType.Null)
                field.Value = value.Value<string>();

            if (item["values"] is JArray values)
                field.Values = values.Values<string>().ToList();

            if (item["options"] is JObject options)
                field.Options = options.Properties().Select(p => new KeyValuePair<string, string>(p.Name, p.Value.Value<string>())).ToList();

            return field;
        }

        // The shape the web UI posts back (see Models/DTO/ConfigItem): id + value, or id + values.
        public static JArray ToSubmission(IEnumerable<ConfigField> fields)
        {
            var array = new JArray();
            foreach (var field in fields.Where(f => !f.IsDisplayOnly))
            {
                var item = new JObject { ["id"] = field.Id };
                if (field.Type == TypeBool)
                    item["value"] = field.BoolValue;
                else if (field.Type == TypeCheckbox)
                    item["values"] = new JArray(field.Values);
                else
                    item["value"] = field.Value ?? string.Empty;
                array.Add(item);
            }
            return array;
        }

        private static byte[] DecodeDataUri(string dataUri)
        {
            if (string.IsNullOrEmpty(dataUri))
                return null;
            var comma = dataUri.IndexOf(',');
            try
            {
                return Convert.FromBase64String(comma >= 0 ? dataUri.Substring(comma + 1) : dataUri);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
