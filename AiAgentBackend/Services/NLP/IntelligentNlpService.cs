using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgentBackend.Services.NLP
{
    public class IntelligentNlpService : INlpService
    {
        private readonly ILogger<IntelligentNlpService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly NlpService _fallbackParser;
        private readonly string _openAIApiKey;
        private readonly string _openAIModel;

        // Conversation context cache (per-user recent context)
        private static readonly Dictionary<int, List<ConversationContext>> _conversationHistory = new();
        private static readonly object _historyLock = new();
        private const int MaxHistoryPerUser = 10;

        public IntelligentNlpService(ILogger<IntelligentNlpService> logger, IConfiguration config, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
            _apiKey = config["Gemini:ApiKey"] ?? "";
            _openAIApiKey = config["OpenAI:ApiKey"] ?? "";
            _openAIModel = config["OpenAI:Model"] ?? "gpt-4o-mini";
            _fallbackParser = new NlpService(NullLogger<NlpService>.Instance, config);
        }

        public async Task<NlpResult> ParseAsync(string text, string timezone)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new NlpResult { Intent = "Unknown", Confidence = 0.0 };

            // Try Gemini first, then OpenAI, then fallback keyword parser
            if (!string.IsNullOrEmpty(_apiKey))
            {
                try
                {
                    var result = await CallGeminiAsync(text, timezone);
                    if (result != null && !string.IsNullOrEmpty(result.Intent) && result.Intent != "Unknown")
                    {
                        _logger.LogInformation("Gemini NLP: Intent={Intent}, Confidence={Confidence}", result.Intent, result.Confidence);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gemini NLP failed, trying OpenAI...");
                }
            }

            // Fallback to OpenAI if Gemini fails or isn't configured
            if (!string.IsNullOrEmpty(_openAIApiKey))
            {
                try
                {
                    var result = await CallOpenAIAsync(text, timezone);
                    if (result != null && !string.IsNullOrEmpty(result.Intent) && result.Intent != "Unknown")
                    {
                        _logger.LogInformation("OpenAI NLP: Intent={Intent}, Confidence={Confidence}", result.Intent, result.Confidence);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OpenAI NLP failed, falling back to keyword parser");
                }
            }

            return await _fallbackParser.ParseAsync(text, timezone);
        }

        public void AddConversationContext(int userId, string userMessage, string botResponse)
        {
            lock (_historyLock)
            {
                if (!_conversationHistory.ContainsKey(userId))
                    _conversationHistory[userId] = new List<ConversationContext>();

                _conversationHistory[userId].Add(new ConversationContext
                {
                    UserMessage = userMessage,
                    BotResponse = botResponse,
                    Timestamp = DateTime.UtcNow
                });

                // Keep only last N messages per user
                if (_conversationHistory[userId].Count > MaxHistoryPerUser)
                    _conversationHistory[userId] = _conversationHistory[userId].TakeLast(MaxHistoryPerUser).ToList();
            }
        }

        private string GetConversationContext(int userId)
        {
            lock (_historyLock)
            {
                if (!_conversationHistory.ContainsKey(userId) || !_conversationHistory[userId].Any())
                    return "";

                var recent = _conversationHistory[userId].TakeLast(5);
                var context = new StringBuilder();
                context.AppendLine("Recent conversation context:");
                foreach (var msg in recent)
                {
                    context.AppendLine($"User: {msg.UserMessage}");
                    context.AppendLine($"Assistant: {msg.BotResponse}");
                }
                return context.ToString();
            }
        }

        private async Task<NlpResult?> CallGeminiAsync(string text, string timezone)
        {
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

            var systemPrompt = "You are an AI assistant that extracts structured information from user messages for a personal assistant app.\n";
            systemPrompt += "Current time: " + now + "\n";
            systemPrompt += "User timezone: " + timezone + "\n";
            systemPrompt += "User message: \"" + text + "\"\n\n";
            systemPrompt += "CRITICAL: Analyze the user's INTENT correctly. Never confuse queries with creation commands.\n\n";
            systemPrompt += "Return JSON with:\n";
            systemPrompt += "- intent: one of [CreateEvent, CreateTask, CreateReminder, UpdateTask, CheckCalendar, CheckTasks, EmailAction, Unknown]\n";
            systemPrompt += "- confidence: number 0.0-1.0\n";
            systemPrompt += "- entities: object with extracted information (omit title for queries)\n\n";
            systemPrompt += "EXAMPLES — learn these:\n";
            systemPrompt += "- \"Show my events this week\" → CheckCalendar (NOT CreateEvent!)\n";
            systemPrompt += "- \"What's on my calendar tomorrow\" → CheckCalendar\n";
            systemPrompt += "- \"Show my tasks\" → CheckTasks\n";
            systemPrompt += "- \"Check my emails\" → EmailAction\n";
            systemPrompt += "- \"Schedule a meeting tomorrow at 3pm\" → CreateEvent, title=\"meeting\"\n";
            systemPrompt += "- \"Create a task buy groceries\" → CreateTask, title=\"buy groceries\"\n";
            systemPrompt += "- \"Add todo review project\" → CreateTask, title=\"review project\"\n";
            systemPrompt += "- \"Set reminder for meeting at 2pm\" → CreateReminder\n";
            systemPrompt += "- \"creat saturday 4am meeting\" → CreateEvent (handle typos), title=\"meeting\"\n";
            systemPrompt += "- \"tommoroww task for sharepoint\" → CreateTask (handle typos), title=\"sharepoint\"\n";
            systemPrompt += "- \"Remind me to call John tomorrow at 2pm\" → CreateReminder, title=\"call John\"\n";
            systemPrompt += "- \"Mark task done\" → UpdateTask\n";
            systemPrompt += "- \"what do i have today\" → CheckCalendar (shows events)\n\n";
            systemPrompt += "Intent rules:\n";
            systemPrompt += "- CheckCalendar: user wants to SEE events (show/list/view/what/check + calendar/events/schedule)\n";
            systemPrompt += "- CheckTasks: user wants to SEE tasks (show/list/view + tasks/todos)\n";
            systemPrompt += "- EmailAction: user wants to read/send/check emails\n";
            systemPrompt += "- CreateEvent: user wants to CREATE a new event/meeting/appointment\n";
            systemPrompt += "- CreateTask: user wants to CREATE a new task/todo\n";
            systemPrompt += "- CreateReminder: user wants to SET a reminder/alert\n";
            systemPrompt += "- UpdateTask: user wants to mark done/complete/update a task\n\n";
            systemPrompt += "Title extraction rules:\n";
            systemPrompt += "- STRIP all command words: create, schedule, add, new, show, list, view, check, my, set, make\n";
            systemPrompt += "- STRIP all temporal words: tomorrow, today, next, this, upcoming\n";
            systemPrompt += "- Keep only the meaningful content words\n";
            systemPrompt += "- For \"schedule a meeting tomorrow at 3pm\" → title=\"meeting\"\n";
            systemPrompt += "- For \"create a task buy groceries tomorrow\" → title=\"buy groceries\"\n";
            systemPrompt += "- For \"creat saturday 4am meeting\" → title=\"meeting\"\n";
            systemPrompt += "- For \"tommoroww task for sharepoint\" → title=\"sharepoint\"\n";
            systemPrompt += "- If no meaningful title, use \"Untitled\"\n\n";
            systemPrompt += "Respond ONLY with valid JSON, no other text.";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = systemPrompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    maxOutputTokens = 256
                }
            };

            var url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key=" + _apiKey;
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var textResponse = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "";

            textResponse = textResponse.Trim();
            if (textResponse.StartsWith("```json"))
                textResponse = textResponse.Substring(7);
            else if (textResponse.StartsWith("```"))
                textResponse = textResponse.Substring(3);
            if (textResponse.EndsWith("```"))
                textResponse = textResponse.Substring(0, textResponse.Length - 3);
            textResponse = textResponse.Trim();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var geminiResult = JsonSerializer.Deserialize<GeminiResponse>(textResponse, options);
            if (geminiResult == null) return null;

            return new NlpResult
            {
                Intent = geminiResult.Intent,
                Confidence = geminiResult.Confidence,
                Entities = geminiResult.Entities ?? new Dictionary<string, string>()
            };
        }

        private async Task<NlpResult?> CallOpenAIAsync(string text, string timezone)
        {
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

            var systemPrompt = "You are an AI assistant that extracts structured information from user messages.\n";
            systemPrompt += "Current time: " + now + "\n";
            systemPrompt += "User timezone: " + timezone + "\n";
            systemPrompt += "Return JSON with intent, confidence (0.0-1.0), and entities.\n";
            systemPrompt += "Intent must be one of: CreateEvent, CreateTask, CreateReminder, UpdateTask, CheckCalendar, CheckTasks, EmailAction, Unknown\n";
            systemPrompt += "Handle typos gracefully. Respond ONLY with valid JSON.";

            var requestBody = new
            {
                model = _openAIModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = text }
                },
                temperature = 0.1,
                max_tokens = 256
            };

            var url = "https://api.openai.com/v1/chat/completions";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", "Bearer " + _openAIApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var textResponse = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            textResponse = textResponse.Trim();
            if (textResponse.StartsWith("```json"))
                textResponse = textResponse.Substring(7);
            else if (textResponse.StartsWith("```"))
                textResponse = textResponse.Substring(3);
            if (textResponse.EndsWith("```"))
                textResponse = textResponse.Substring(0, textResponse.Length - 3);
            textResponse = textResponse.Trim();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var openAIResult = JsonSerializer.Deserialize<GeminiResponse>(textResponse, options);
            if (openAIResult == null) return null;

            return new NlpResult
            {
                Intent = openAIResult.Intent,
                Confidence = openAIResult.Confidence,
                Entities = openAIResult.Entities ?? new Dictionary<string, string>()
            };
        }

        private class GeminiResponse
        {
            public string Intent { get; set; } = "Unknown";
            public double Confidence { get; set; }
            public Dictionary<string, string>? Entities { get; set; }
        }

        private class ConversationContext
        {
            public string UserMessage { get; set; } = string.Empty;
            public string BotResponse { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
        }
    }
}
