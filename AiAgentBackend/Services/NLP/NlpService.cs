using System.Text.RegularExpressions;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiAgentBackend.Services.NLP
{
    public class NlpResult
    {
        public string Intent { get; set; } = "Unknown";
        public Dictionary<string, string> Entities { get; set; } = new();
        public double Confidence { get; set; } = 0.0;
    }

    public interface INlpService
    {
        Task<NlpResult> ParseAsync(string text, string timezone);
    }

    public class OpenAiNlpService : INlpService
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly ILogger<OpenAiNlpService> _logger;

        public OpenAiNlpService(HttpClient http, IConfiguration config, ILogger<OpenAiNlpService> logger)
        {
            _http = http;
            _apiKey = config["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
            _model = config["OpenAI:Model"] ?? "gpt-4o-mini";
            _logger = logger;
            
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogWarning("OpenAI API key is missing. Using fallback NLP.");
            }
        }

        private string BuildSystemPrompt(string timezone)
        {
            return $@"
You are an AI assistant that extracts intent and entities from user messages for a productivity system.
The system integrates with Google Calendar, Trello, Gmail, and WhatsApp.

Possible intents:
- CreateEvent: Schedule meetings, appointments, events
- CreateTask: Create todo items, tasks, reminders
- UpdateTask: Update task status (To Do, In Progress, Done)
- CreateReminder: Set reminders, alerts
- CheckCalendar: View upcoming events, check availability
- CheckTasks: View task list, check pending items
- EmailAction: Handle email-related requests
- Unknown: When intent is unclear

Possible entities to extract:
- title: string (name of event/task)
- datetime: DateTime (when something should happen)
- date: Date (specific date)
- time: Time (specific time)
- duration_minutes: integer (how long something takes)
- status: string (task status: To Do, In Progress, Done)
- due: DateTime (when something is due)
- attendees: comma-separated list of emails
- location: string (where something happens)
- description: string (additional details)
- labels: comma-separated list of tags

Always return valid JSON with this structure:
{{
    ""intent"": ""IntentName"",
    ""entities"": {{
        ""key1"": ""value1"",
        ""key2"": ""value2""
    }},
    ""confidence"": 0.95
}}

Interpret relative dates with user timezone: {timezone}
Current UTC time: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}
";
        }

        public async Task<NlpResult> ParseAsync(string text, string timezone)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new NlpResult
                {
                    Intent = "Unknown",
                    Confidence = 0.0,
                    Entities = { ["error"] = "Empty message" }
                };
            }

            // Enhanced entity recognition for better parsing
            var entities = new Dictionary<string, string>();

            // Date/time parsing
            var dateMatches = Regex.Matches(text, 
                @"(today|tomorrow|next week|next month|on \w+day|at \d+:\d+)", 
                RegexOptions.IgnoreCase);

            // Priority detection
            if (text.Contains("urgent") || text.Contains("asap") || text.Contains("important"))
            {
                entities["priority"] = "high";
            }

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                return await FallbackParseAsync(text, timezone);
            }

            try
            {
                var body = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = BuildSystemPrompt(timezone) },
                        new { role = "user", content = text }
                    },
                    temperature = 0.1,
                    response_format = new { type = "json_object" }
                };

                var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    var error = await resp.Content.ReadAsStringAsync();
                    _logger.LogError("OpenAI API error: {StatusCode} - {Error}", resp.StatusCode, error);
                    return await FallbackParseAsync(text, timezone);
                }

                var responseJson = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                
                var content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return ParseNlpResponse(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI NLP processing failed");
                return await FallbackParseAsync(text, timezone);
            }
        }

private NlpResult ParseNlpResponse(string? content)
{
    try
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new NlpResult
            {
                Intent = "Unknown",
                Confidence = 0.5,
                Entities = { ["title"] = "Empty response" }
            };
        }

        // Extract JSON from response (sometimes it's wrapped in markdown)
        var jsonStart = content.IndexOf('{');
        var jsonEnd = content.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            content = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        var parsed = JsonSerializer.Deserialize<JsonElement>(content);
        var result = new NlpResult();

        if (parsed.ValueKind == JsonValueKind.Object)
        {
            if (parsed.TryGetProperty("intent", out var intentElem))
                result.Intent = intentElem.GetString() ?? "Unknown";

            if (parsed.TryGetProperty("confidence", out var confElem) &&
                confElem.TryGetDouble(out var confidence))
                result.Confidence = confidence;

            if (parsed.TryGetProperty("entities", out var entitiesElem) &&
                entitiesElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in entitiesElem.EnumerateObject())
                {
                    result.Entities[prop.Name] = prop.Value.GetString() ?? "";
                }
            }
        }

        // Ensure we have at least a title
        if (!result.Entities.ContainsKey("title"))
        {
            result.Entities["title"] = content.Length > 100 ? content.Substring(0, 100) : content;
        }

        return result;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to parse NLP response");

        return new NlpResult
        {
            Intent = "Unknown",
            Confidence = 0.5,
            Entities = { ["title"] = "Unparsable command" }
        };
    }
}

        private Task<NlpResult> FallbackParseAsync(string text, string timezone)
        {
            var result = new NlpResult { Confidence = 0.6 };
            var t = text.ToLowerInvariant();
            
            // Simple keyword-based intent detection
            if (t.Contains("schedule") || t.Contains("meeting") || t.Contains("event") || t.Contains("appointment"))
                result.Intent = "CreateEvent";
            else if (t.Contains("task") || t.Contains("todo") || t.Contains("trello") || t.Contains("card"))
                result.Intent = "CreateTask";
            else if (t.Contains("done") || t.Contains("complete") || t.Contains("finish") || t.Contains("update"))
                result.Intent = "UpdateTask";
            else if (t.Contains("remind") || t.Contains("alert") || t.Contains("notify"))
                result.Intent = "CreateReminder";
            else if (t.Contains("calendar") || t.Contains("availability") || t.Contains("when"))
                result.Intent = "CheckCalendar";
            else if (t.Contains("task list") || t.Contains("what's pending") || t.Contains("to do"))
                result.Intent = "CheckTasks";
            else if (t.Contains("email") || t.Contains("mail") || t.Contains("send"))
                result.Intent = "EmailAction";
            else
                result.Intent = "Unknown";

            // Simple entity extraction
            if (t.Contains("tomorrow"))
                result.Entities["date"] = "tomorrow";
            else if (t.Contains("monday") || t.Contains("tuesday") || t.Contains("wednesday") || 
                     t.Contains("thursday") || t.Contains("friday") || t.Contains("saturday") || t.Contains("sunday"))
                result.Entities["date"] = GetNextDay(t);
                
            if (t.Contains("at ") && t.IndexOf("at ") < t.Length - 3)
            {
                var timePart = t.Substring(t.IndexOf("at ") + 3).Split(' ')[0];
                if (timePart.Contains(":") || timePart.Contains("pm") || timePart.Contains("am"))
                    result.Entities["time"] = timePart;
            }

            // Use the first few words as title
            var words = text.Split(' ').Take(5).ToArray();
            result.Entities["title"] = string.Join(" ", words);

            return Task.FromResult(result);
        }

        private string GetNextDay(string text)
        {
            var days = new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };
            var today = DateTime.Today.DayOfWeek.ToString().ToLower();
            var targetDay = days.FirstOrDefault(d => text.Contains(d));
            
            if (targetDay != null)
            {
                var todayIndex = Array.IndexOf(days, today);
                var targetIndex = Array.IndexOf(days, targetDay);
                
                var daysToAdd = targetIndex >= todayIndex ? 
                    targetIndex - todayIndex : 
                    7 - (todayIndex - targetIndex);
                    
                return DateTime.Today.AddDays(daysToAdd).ToString("yyyy-MM-dd");
            }
            
            return "tomorrow";
        }

        public async Task<string> GenerateResponseAsync(string prompt, string context)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                return "I'm here to help! How can I assist you today?";
            }

            try
            {
                var body = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = "You are a helpful AI assistant that helps with productivity tasks. Be concise and helpful." },
                        new { role = "user", content = $"{context}\n\n{prompt}" }
                    },
                    temperature = 0.7,
                    max_tokens = 150
                };

                var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    var error = await resp.Content.ReadAsStringAsync();
                    _logger.LogError("OpenAI API error: {StatusCode} - {Error}", resp.StatusCode, error);
                    return "I encountered an error. Please try again.";
                }

                var responseJson = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                
                return doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "I'm here to help!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI response generation failed");
                return "I encountered an error. Please try again.";
            }
        }

public async Task<NlpResult> ParseWithContextAsync(string text, string timezone, string context)
{
    if (string.IsNullOrWhiteSpace(_apiKey))
    {
        return await FallbackParseAsync(text, timezone);
    }

    try
    {
        var systemPrompt = BuildSystemPrompt(timezone);
        var contextPrompt = $"{systemPrompt}\n\nCurrent context: {context}";

        var body = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = contextPrompt },
                new { role = "user", content = text }
            },
            temperature = 0.1,
            response_format = new { type = "json_object" }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var error = await resp.Content.ReadAsStringAsync();
            _logger.LogError("OpenAI API error: {StatusCode} - {Error}", resp.StatusCode, error);
            return await FallbackParseAsync(text, timezone);
        }

        var responseJson = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return ParseNlpResponse(content);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "OpenAI NLP processing with context failed");
        return await FallbackParseAsync(text, timezone);
    }
}        
    }
}