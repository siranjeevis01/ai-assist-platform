// Utils/JsonExtensions.cs
using System.Text.Json;

namespace AiAgentBackend.Utils
{
    public static class JsonExtensions
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static string ToJson(this object obj)
        {
            return JsonSerializer.Serialize(obj, _options);
        }

        public static T FromJson<T>(this string json)
        {
            return JsonSerializer.Deserialize<T>(json, _options) ?? throw new InvalidOperationException("Failed to deserialize JSON");
        }

        public static bool TryParseJson<T>(this string json, out T result)
        {
            try
            {
                result = JsonSerializer.Deserialize<T>(json, _options)!;
                return result != null;
            }
            catch
            {
                result = default!;
                return false;
            }
        }

        public static string PrettyPrint(this string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return json;
            }
        }
    }
}