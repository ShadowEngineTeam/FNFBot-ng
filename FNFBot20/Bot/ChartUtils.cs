using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FridayNightFunkin
{
    public static class ChartUtils
    {
        public static JsonDocument LoadJson(string path)
        {
            string raw = StripTrailingGarbage(File.ReadAllText(path));
            return JsonDocument.Parse(raw, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }

        public static string StripTrailingGarbage(string raw)
        {
            raw = raw.Trim();
            int lastBrace = raw.LastIndexOf('}');
            if (lastBrace >= 0 && lastBrace < raw.Length - 1)
                raw = raw[..(lastBrace + 1)];
            return raw;
        }

        public static double GetDouble(JsonElement obj, string name, double fallback = 0)
        {
            if (obj.TryGetProperty(name, out var v))
                return ElementToDouble(v, fallback);
            return fallback;
        }

        public static string GetString(JsonElement obj, string name, string fallback = "")
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? fallback;
            return fallback;
        }

        public static double ElementToDouble(JsonElement e, double fallback = 0)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Number: return e.GetDouble();
                case JsonValueKind.String:
                    return double.TryParse(e.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : fallback;
                case JsonValueKind.True: return 1;
                case JsonValueKind.False: return 0;
                default: return fallback;
            }
        }
    }
}
