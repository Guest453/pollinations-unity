// PollinationsModelCatalog — the model-list data type plus parsing of the live
// /{text,image,audio}/models responses. UnityEngine-free for plain .NET testing.
//
// Verified against the live API (2026-09). Example entry:
// { "name":"tongyi-mai/z-image-turbo", "title":"Z-Image Turbo", "publisher":"Alibaba",
//   "category":"image", "community":false,
//   "pricing":{"currency":"pollen","completionImageTokens":"0.004"},
//   "health":{"status":"healthy","success_rate":99.958} }
using System;
using System.Collections.Generic;

namespace Pollinations.Unity
{
    /// <summary>Which Pollinations model list to fetch.</summary>
    public enum PollinationsModality
    {
        Text,
        Image,
        Audio
    }

    /// <summary>
    /// One entry from a Pollinations model list. Pricing values are Pollen per
    /// token/image as strings from the API (multiply by 1_000_000 for "per million").
    /// </summary>
    public sealed class PollinationsModel
    {
        public string Name;                // e.g. "openai/gpt-5.4-nano" — use for API calls
        public string Title;               // human label, e.g. "GPT-5.4 Nano"
        public string Publisher;           // e.g. "OpenAI"
        public string Description;
        public string Category;            // "text" | "image" | "audio"
        public bool Community;             // community-hosted model
        public double? ContextLength;      // text models
        public double? SuccessRate;        // health.success_rate, e.g. 99.95

        public static PollinationsModel FromJson(object o)
        {
            if (!(o is IDictionary<string, object> d)) return null;
            double? successRate = null;
            if (Internal.MiniJson.Get(d, "health") is IDictionary<string, object> health)
                successRate = Internal.MiniJson.GetNumber(health, "success_rate");
            return new PollinationsModel
            {
                Name = Internal.MiniJson.GetString(d, "name"),
                Title = Internal.MiniJson.GetString(d, "title"),
                Publisher = Internal.MiniJson.GetString(d, "publisher"),
                Description = Internal.MiniJson.GetString(d, "description"),
                Category = Internal.MiniJson.GetString(d, "category"),
                Community = Internal.MiniJson.Get(d, "community") is bool b && b,
                ContextLength = Internal.MiniJson.GetNumber(d, "context_length"),
                SuccessRate = successRate
            };
        }

        /// <summary>Parse the full /{modality}/models JSON array.</summary>
        public static List<PollinationsModel> ListFromJson(string json)
        {
            object parsed;
            try { parsed = Internal.MiniJson.Parse(json); }
            catch (FormatException e) { throw new PollinationsException("Model list response was not valid JSON: " + e.Message, e); }
            if (!(parsed is IEnumerable<object> list))
                throw new PollinationsException("Model list response was not a JSON array.");
            var result = new List<PollinationsModel>();
            foreach (var item in list)
            {
                var m = FromJson(item);
                if (m != null && !string.IsNullOrEmpty(m.Name)) result.Add(m);
            }
            return result;
        }
    }
}
