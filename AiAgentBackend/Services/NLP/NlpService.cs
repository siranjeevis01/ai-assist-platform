using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiAgentBackend.Services.NLP
{
    public interface INlpService
    {
        Task<NlpResult> ParseAsync(string text, string timezone);
    }

    public class NlpResult
    {
        public string Intent { get; set; } = "Unknown";
        public Dictionary<string, string> Entities { get; set; } = new Dictionary<string, string>();
        public double Confidence { get; set; } = 0.0;
    }

    public class NlpService : INlpService
    {
        private readonly ILogger<NlpService> _logger;

        public NlpService(ILogger<NlpService> logger, IConfiguration config)
        {
            _logger = logger;
        }

        public async Task<NlpResult> ParseAsync(string text, string timezone)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new NlpResult
                {
                    Intent = "Unknown",
                    Confidence = 0.0,
                    Entities = new Dictionary<string, string> { ["error"] = "Empty message" }
                };
            }

            var cleanText = CleanTextForAnalysis(text);
            
            if (!IsValidForAnalysis(cleanText))
            {
                return new NlpResult
                {
                    Intent = "Unknown",
                    Confidence = 0.0,
                    Entities = new Dictionary<string, string> { ["error"] = "Invalid input" }
                };
            }

            // Use enhanced local keyword parser (FREE)
            return await EnhancedLocalParser(cleanText, timezone);
        }

        private Task<NlpResult> EnhancedLocalParser(string text, string timezone)
        {
            var result = new NlpResult { Confidence = 0.8 };
            var lowerText = text.ToLowerInvariant();
            
            // Enhanced intent detection
            if (ContainsAny(lowerText, new[] {"schedule", "meeting", "appointment", "calendar", "event"}))
                result.Intent = "CreateEvent";
            else if (ContainsAny(lowerText, new[] {"task", "todo", "trello", "card", "reminder"}))
                result.Intent = "CreateTask";
            else if (ContainsAny(lowerText, new[] {"remind", "alert", "notify", "remember"}))
                result.Intent = "CreateReminder";
            else if (ContainsAny(lowerText, new[] {"done", "complete", "finish", "update"}))
                result.Intent = "UpdateTask";
            else if (ContainsAny(lowerText, new[] {"email", "mail", "inbox", "send"}))
                result.Intent = "EmailAction";
            else if (ContainsAny(lowerText, new[] {"show", "check", "view", "what is"}))
                result.Intent = "CheckCalendar";
            else
                result.Intent = "Unknown";

            // Enhanced entity extraction
            result.Entities = ExtractEnhancedEntities(text, timezone);
            
            _logger.LogInformation("Local NLP parsed: Intent={Intent}, Confidence={Confidence}", 
                result.Intent, result.Confidence);

            return Task.FromResult(result);
        }

        private Dictionary<string, string> ExtractEnhancedEntities(string text, string timezone)
        {
            var entities = new Dictionary<string, string>();
            var lowerText = text.ToLower();
            
            // Title extraction (first meaningful words)
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2)
                .Take(5)
                .ToArray();
            entities["title"] = string.Join(" ", words);

            // Date extraction
            entities["date"] = ExtractDate(lowerText, timezone);
            
            // Time extraction
            var timeMatch = Regex.Match(text, @"(\d{1,2}:\d{2}\s*(?:am|pm)?|\d{1,2}\s*(?:am|pm))", RegexOptions.IgnoreCase);
            if (timeMatch.Success)
                entities["time"] = timeMatch.Value;

            // Duration detection
            if (lowerText.Contains("hour") || lowerText.Contains("minute"))
            {
                var durationMatch = Regex.Match(text, @"(\d+)\s*(hour|minute)");
                if (durationMatch.Success)
                {
                    var value = int.Parse(durationMatch.Groups[1].Value);
                    var unit = durationMatch.Groups[2].Value;
                    entities["duration_minutes"] = unit == "hour" ? (value * 60).ToString() : value.ToString();
                }
            }

            // Priority detection
            if (ContainsAny(lowerText, new[] {"urgent", "asap", "important", "critical"}))
                entities["priority"] = "high";

            return entities;
        }

        private string ExtractDate(string text, string timezone)
        {
            if (text.Contains("tomorrow"))
                return DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
            else if (text.Contains("today"))
                return DateTime.Today.ToString("yyyy-MM-dd");
            else if (text.Contains("monday")) return GetNextDay("Monday");
            else if (text.Contains("tuesday")) return GetNextDay("Tuesday");
            else if (text.Contains("wednesday")) return GetNextDay("Wednesday");
            else if (text.Contains("thursday")) return GetNextDay("Thursday");
            else if (text.Contains("friday")) return GetNextDay("Friday");
            else if (text.Contains("saturday")) return GetNextDay("Saturday");
            else if (text.Contains("sunday")) return GetNextDay("Sunday");
            else
                return DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"); // Default tomorrow
        }

        private string GetNextDay(string dayName)
        {
            var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
            var today = DateTime.Today.DayOfWeek.ToString();
            var todayIndex = Array.IndexOf(days, today);
            var targetIndex = Array.IndexOf(days, dayName);
            
            var daysToAdd = targetIndex >= todayIndex ? 
                targetIndex - todayIndex : 
                7 - (todayIndex - targetIndex);
                
            return DateTime.Today.AddDays(daysToAdd).ToString("yyyy-MM-dd");
        }

        private bool ContainsAny(string text, string[] keywords)
        {
            return keywords.Any(keyword => text.Contains(keyword));
        }

        private string CleanTextForAnalysis(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            text = Regex.Replace(text, "<.*?>", string.Empty);
            text = Regex.Replace(text, @"http[^\s]+", string.Empty);
            text = Regex.Replace(text, @"\s+", " ").Trim();
            
            return text.Length > 200 ? text.Substring(0, 200) : text;
        }

        private bool IsValidForAnalysis(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Trim().Length < 3) return false;
            
            var cleanText = Regex.Replace(text, @"[^\w]", "");
            return cleanText.Length >= 2;
        }
    }
}