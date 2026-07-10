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

        private static readonly string[] _skipWords = 
            "create schedule add new show list view check whats what's my the a an for with on at in to is are me please set make".Split();
        
        private static readonly string[] _temporalWords =
            "today tomorrow yesterday now next this coming upcoming monday tuesday wednesday thursday friday saturday sunday week month year morning afternoon evening night".Split();

        private static readonly string[] _commandWords =
            "create schedule add new show list view check whats what's my please set make".Split();

        // Common typo corrections for better matching
        private static readonly Dictionary<string, string> _typoCorrections = new()
        {
            { "tommorow", "tomorrow" }, { "tomorow", "tomorrow" }, { "tmrw", "tomorrow" },
            { "todays", "today" }, { "toady", "today" },
            { "yesturday", "yesterday" },
            { "remindr", "reminder" }, { "remnder", "reminder" },
            { "schedual", "schedule" }, { "scheduel", "schedule" }, { "schdule", "schedule" },
            { "calender", "calendar" }, { "calandar", "calendar" },
            { "creat", "create" }, { "crete", "create" }, { "creeate", "create" },
            { "delet", "delete" }, { "dlete", "delete" },
            { "mesage", "message" }, { "messge", "message" }, { "msg", "message" },
            { "evnt", "event" }, { "evnet", "event" },
            { "taks", "task" }, { "tsak", "task" }, { "tsk", "task" },
            { "emial", "email" }, { "eamil", "email" }, { "mail", "email" },
            { "ntoification", "notification" }, { "notifcation", "notification" },
            { "snozze", "snooze" }, { "snoz", "snooze" },
            { "complte", "complete" }, { "complet", "complete" }, { "done", "complete" },
            { "meeding", "meeting" }, { "meetign", "meeting" },
            { "appointmnt", "appointment" }, { "apointment", "appointment" },
            { "remind me", "remind" },
            { "show my evnts", "show my events" }, { "show my evnt", "show my events" },
            { "what do i hve", "what do i have" }, { "what do i hav", "what do i have" },
            { "hwat", "what" }, { "whats", "what's" }, { "wt", "what" },
            { "shw", "show" }, { "sho", "show" },
            { "lkst", "list" }, { "lsit", "list" },
            { "chekc", "check" }, { "chec", "check" },
            { "adn", "and" }, { "nad", "and" },
            { "wiht", "with" }, { "wtih", "with" },
            { "fro", "for" },
            { "thsi", "this" }, { "tihs", "this" },
            { "nto", "not" }, { "ot", "to" },
        };

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
            cleanText = CorrectTypos(cleanText);
            
            if (!IsValidForAnalysis(cleanText))
            {
                return new NlpResult
                {
                    Intent = "Unknown",
                    Confidence = 0.0,
                    Entities = new Dictionary<string, string> { ["error"] = "Invalid input" }
                };
            }

            return await EnhancedLocalParser(cleanText, timezone);
        }

        private string CorrectTypos(string text)
        {
            var lower = text.ToLowerInvariant();
            var corrected = lower;

            foreach (var typo in _typoCorrections)
            {
                if (corrected.Contains(typo.Key))
                {
                    corrected = corrected.Replace(typo.Key, typo.Value);
                }
            }

            // Fix common letter swaps (e.g., "tihs" -> "this")
            corrected = Regex.Replace(corrected, @"\b(\w)(\w)\2\1\b", m =>
            {
                var word = m.Value;
                // Simple double-letter correction
                if (word.Length == 4 && word[0] == word[3] && word[1] == word[2])
                    return word;
                return word;
            });

            return corrected;
        }

        private Task<NlpResult> EnhancedLocalParser(string text, string timezone)
        {
            var result = new NlpResult { Confidence = 0.8 };
            var lowerText = text.ToLowerInvariant();

            // Intent detection: most specific patterns first
            if (IsEmailQuery(lowerText))
                result.Intent = "EmailAction";
            else if (IsCalendarQuery(lowerText))
                result.Intent = "CheckCalendar";
            else if (IsTasksQuery(lowerText))
                result.Intent = "CheckTasks";
            else if (IsCreateEvent(lowerText))
                result.Intent = "CreateEvent";
            else if (IsCreateTask(lowerText))
                result.Intent = "CreateTask";
            else if (IsCreateReminder(lowerText))
                result.Intent = "CreateReminder";
            else if (IsUpdateTask(lowerText))
                result.Intent = "UpdateTask";
            else if (ContainsAny(lowerText, new[] {"event", "calendar"}))
                result.Intent = "CreateEvent";
            else if (ContainsAny(lowerText, new[] {"task"}))
                result.Intent = "CreateTask";
            else
                result.Intent = "Unknown";

            result.Entities = ExtractEntities(text, timezone, result.Intent);
            
            // Reduce confidence for fuzzy matches
            if (result.Intent != "Unknown" && IsFuzzyMatch(lowerText))
                result.Confidence = 0.6;

            _logger.LogInformation("Local NLP parsed: Intent={Intent}, Confidence={Confidence}", 
                result.Intent, result.Confidence);

            return Task.FromResult(result);
        }

        private bool IsFuzzyMatch(string text)
        {
            // Check if the original text had typos that were corrected
            foreach (var typo in _typoCorrections)
            {
                if (text.Contains(typo.Key)) return true;
            }
            return false;
        }

        private bool IsEmailQuery(string lowerText)
        {
            if (ContainsAny(lowerText, new[] {"email", "mail", "inbox"}))
                return true;
            if (ContainsAny(lowerText, new[] {"check email", "check mail", "show email", "show mail",
                "read email", "read mail", "my email", "my mail", "unread"}))
                return true;
            return false;
        }

        private bool IsCalendarQuery(string lowerText)
        {
            if (ContainsAny(lowerText, new[] {"show my events", "list my events", "view my events",
                "what are my events", "what events do i have", "show events", "list events", "view events",
                "whats on my calendar", "what's on my calendar", "what is on my calendar",
                "my calendar", "open calendar", "show calendar",
                "show my schedule", "what's my schedule", "whats my schedule",
                "what do i have", "whats coming up", "what's coming up"}))
                return true;
            if (ContainsAny(lowerText, new[] {"show", "check", "view", "show me", "what is", "what are"})
                && ContainsAny(lowerText, new[] {"calendar", "event", "schedule"}))
                return true;
            return false;
        }

        private bool IsTasksQuery(string lowerText)
        {
            if (ContainsAny(lowerText, new[] {"show my tasks", "list my tasks", "view my tasks",
                "what are my tasks", "what tasks do i have", "show tasks", "list tasks", "view tasks",
                "my to-do", "my todos", "show my to-do", "show my todo",
                "what do i need to do", "whats pending", "what's pending"}))
                return true;
            if (ContainsAny(lowerText, new[] {"show", "check", "view", "show me", "what is", "what are"})
                && ContainsAny(lowerText, new[] {"task", "todo"}))
                return true;
            return false;
        }

        private bool IsCreateEvent(string lowerText)
        {
            if (ContainsAny(lowerText, new[] {"schedule", "meeting", "appointment", "create event",
                "new event", "add event", "set up a", "book a"}))
                return true;
            if (ContainsAny(lowerText, new[] {"create", "add", "new", "make"})
                && ContainsAny(lowerText, new[] {"event", "calendar", "meeting", "appointment"}))
                return true;
            return false;
        }

        private bool IsCreateTask(string lowerText)
        {
            if (ContainsAny(lowerText, new[] {"create task", "new task", "add task", "todo", "trello",
                "create a task", "add a task", "new to-do", "create to-do"}))
                return true;
            if (ContainsAny(lowerText, new[] {"create", "add", "new", "make"})
                && ContainsAny(lowerText, new[] {"task", "to-do"}))
                return true;
            if (ContainsAny(lowerText, new[] {"to do", "to-do"}) && !IsTasksQuery(lowerText))
                return true;
            return false;
        }

        private bool IsCreateReminder(string lowerText)
        {
            return ContainsAny(lowerText, new[] {"remind", "alert", "notify", "remember", "reminder",
                "set a reminder", "create reminder", "remind me"});
        }

        private bool IsUpdateTask(string lowerText)
        {
            return ContainsAny(lowerText, new[] {"done", "complete", "finish", "update task", "mark",
                "mark as", "set as done", "set done", "completed"});
        }

        private Dictionary<string, string> ExtractEntities(string text, string timezone, string intent)
        {
            var entities = new Dictionary<string, string>();

            // For query intents, don't extract a title
            if (intent is "CheckCalendar" or "CheckTasks" or "EmailAction")
            {
                entities["date"] = ExtractDate(text.ToLower(), timezone);
                return entities;
            }

            // Title extraction for creation intents
            entities["title"] = ExtractTitle(text, intent);
            
            // Date extraction
            entities["date"] = ExtractDate(text.ToLower(), timezone);
            
            // Time extraction
            var timeMatch = Regex.Match(text, @"(\d{1,2}:\d{2}\s*(?:am|pm)?|\d{1,2}\s*(?:am|pm))", RegexOptions.IgnoreCase);
            if (timeMatch.Success)
                entities["time"] = timeMatch.Value;

            // Combined datetime
            var dateStr = entities.GetValueOrDefault("date", "");
            var timeStr = entities.GetValueOrDefault("time", "");
            if (!string.IsNullOrEmpty(dateStr) && !string.IsNullOrEmpty(timeStr))
            {
                if (DateTime.TryParse($"{dateStr} {timeStr}", out var dt))
                    entities["datetime"] = dt.ToString("o");
            }

            // Duration detection
            if (text.ToLower().Contains("hour") || text.ToLower().Contains("minute"))
            {
                var durationMatch = Regex.Match(text, @"(\d+)\s*(hour|minute)", RegexOptions.IgnoreCase);
                if (durationMatch.Success)
                {
                    var value = int.Parse(durationMatch.Groups[1].Value);
                    var unit = durationMatch.Groups[2].Value.ToLower();
                    entities["duration_minutes"] = unit == "hour" ? (value * 60).ToString() : value.ToString();
                }
            }

            // Priority detection
            if (ContainsAny(text.ToLower(), new[] {"urgent", "asap", "important", "critical", "high priority"}))
                entities["priority"] = "high";

            // Location detection
            var locationMatch = Regex.Match(text, @"(?:at|in|@)\s+([A-Za-z0-9\s]+?)(?:\s+(?:tomorrow|today|at|on|with|for|by|$)|$)", RegexOptions.IgnoreCase);
            if (locationMatch.Success)
            {
                var loc = locationMatch.Groups[1].Value.Trim();
                if (loc.Length > 1 && loc.Length < 50)
                    entities["location"] = loc;
            }

            return entities;
        }

        private string ExtractTitle(string text, string intent)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (words.Count == 0) return "Untitled";

            // Remove command words from the start
            while (words.Count > 0 && _commandWords.Contains(words[0].ToLower()))
                words.RemoveAt(0);

            // For CreateTask, also strip temporal words from start
            if (intent == "CreateTask")
            {
                while (words.Count > 0 && _temporalWords.Contains(words[0].ToLower()))
                    words.RemoveAt(0);
            }

            // Remove trailing temporal words and prepositions
            var filterEnd = new[] {"on", "at", "in", "for", "by", "with", "tomorrow", "today", "tonight", "next", "this"};
            while (words.Count > 0 && filterEnd.Contains(words[words.Count - 1].ToLower()))
                words.RemoveAt(words.Count - 1);

            if (words.Count == 0) return "Untitled";

            return string.Join(" ", words.Take(6));
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
            else if (text.Contains("next week"))
                return DateTime.Today.AddDays(7).ToString("yyyy-MM-dd");
            else
                return "";
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
