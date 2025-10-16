using System.Text.RegularExpressions;

namespace AiAgentBackend.Utils
{
    public static class TextCleaner
    {
        private static readonly Regex HtmlRegex = new Regex("<.*?>", RegexOptions.Compiled);
        private static readonly Regex UrlRegex = new Regex(@"http[^\s]+", RegexOptions.Compiled);
        private static readonly Regex ExtraSpacesRegex = new Regex(@"\s+", RegexOptions.Compiled);

        public static string CleanForNlp(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Remove HTML tags
            text = HtmlRegex.Replace(text, string.Empty);
            
            // Remove URLs
            text = UrlRegex.Replace(text, string.Empty);
            
            // Remove excessive whitespace
            text = ExtraSpacesRegex.Replace(text, " ").Trim();
            
            // Limit length for API calls
            return text.Length > 500 ? text.Substring(0, 500) : text;
        }

        public static bool IsValidForAnalysis(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Check minimum length
            if (text.Trim().Length < 3)
                return false;

            // Check if it's just special characters
            var cleanText = Regex.Replace(text, @"[^\w]", "");
            if (cleanText.Length < 2)
                return false;

            return true;
        }

        public static string Truncate(this string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength - 3) + "...";
        }
    }
}