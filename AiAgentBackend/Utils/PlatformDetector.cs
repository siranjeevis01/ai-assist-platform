namespace AiAgentBackend.Utils
{
    public static class PlatformDetector
    {
        public static string DetectPlatform(string source)
        {
            return source?.ToLower() switch
            {
                "whatsapp" => "WhatsApp",
                "dashboard" => "Web",
                "gmail" => "Email",
                "api" => "API",
                _ => "Unknown"
            };
        }

        public static Dictionary<string, string> FormatPlatformResponses(Dictionary<string, object> results, string platform)
        {
            var responses = new Dictionary<string, string>();
            
            foreach (var result in results)
            {
                responses[result.Key] = platform switch
                {
                    "WhatsApp" => FormatForWhatsApp(result.Value),
                    "Web" => FormatForWeb(result.Value),
                    "Email" => FormatForEmail(result.Value),
                    _ => result.Value.ToString() ?? string.Empty
                };
            }
            
            return responses;
        }

        private static string FormatForWhatsApp(object value)
        {
            return $"📱 {value}";
        }

        private static string FormatForWeb(object value)
        {
            return $"🌐 {value}";
        }

        private static string FormatForEmail(object value)
        {
            return $"📧 {value}";
        }
    }
}