using System.Text.RegularExpressions;

namespace AiAgentBackend.Utils
{
    public static class TextCleaner
    {
        public static string CleanForNlp(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Remove HTML tags
            text = Regex.Replace(text, "<.*?>", string.Empty);
            
            // Remove URLs
            text = Regex.Replace(text, @"http[^\s]+", string.Empty);
            
            // Remove email headers and metadata
            text = Regex.Replace(text, @"(From:|Subject:|To:|Date:).*?\n", string.Empty);
            
            // Remove excessive whitespace
            text = Regex.Replace(text, @"\s+", " ").Trim();
            
            // Limit length for API calls
            return text.Length > 300 ? text.Substring(0, 300) + "..." : text;
        }

        public static bool IsValidForAnalysis(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var cleanText = CleanForNlp(text);
            
            // Check if text has meaningful content (not just HTML/URLs)
            return cleanText.Length >= 10 && 
                   cleanText.Split(' ').Length >= 2;
        }
    }
}