using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiAgentBackend.Services.NLP
{
    public class FreeNlpService : INlpService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<FreeNlpService> _logger;
        private readonly string _apiUrl = "https://api-inference.huggingface.co/models";

        public FreeNlpService(HttpClient httpClient, IConfiguration config, ILogger<FreeNlpService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            
            // Set timeout to prevent hanging requests
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
            
            var token = config["HuggingFace:Token"] ?? "";
            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
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

            // Clean the text first
            var cleanText = CleanTextForAnalysis(text);
            
            try
            {
                // Use a more reliable model for classification
                var modelUrl = $"{_apiUrl}/typeform/distilbert-base-uncased-mnli";
                
                var requestData = new
                {
                    inputs = cleanText,
                    parameters = new 
                    { 
                        candidate_labels = new[] 
                        {
                            "schedule meeting", 
                            "create task", 
                            "set reminder", 
                            "check calendar", 
                            "send email", 
                            "update task",
                            "unknown"
                        }
                    }
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(modelUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    
                    // Handle different response formats
                    var result = ParseHuggingFaceResponse(responseContent);
                    
                    if (result != null)
                    {
                        return new NlpResult
                        {
                            Intent = MapIntent(result.Labels?[0] ?? "unknown"),
                            Confidence = result.Scores?[0] ?? 0.5,
                            Entities = ExtractEntities(cleanText)
                        };
                    }
                    else
                    {
                        _logger.LogWarning("Could not parse Hugging Face response: {Content}", responseContent);
                        return FallbackParse(cleanText, timezone);
                    }
                }
                else
                {
                    _logger.LogWarning("Hugging Face API error: {StatusCode} - {Reason}", 
                        response.StatusCode, response.ReasonPhrase);
                    return FallbackParse(cleanText, timezone);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during NLP parsing for text: {Text}", cleanText);
                return FallbackParse(cleanText, timezone);
            }
        }

        private HuggingFaceResponse? ParseHuggingFaceResponse(string content)
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Handle different response formats
                if (root.ValueKind == JsonValueKind.Array)
                {
                    // Array format
                    var firstItem = root.EnumerateArray().FirstOrDefault();
                    if (firstItem.ValueKind == JsonValueKind.Object)
                    {
                        return ParseObjectResponse(firstItem);
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    // Object format
                    return ParseObjectResponse(root);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse Hugging Face response: {Error}", ex.Message);
                return null;
            }
        }

        private HuggingFaceResponse? ParseObjectResponse(JsonElement element)
        {
            try
            {
                var response = new HuggingFaceResponse();

                if (element.TryGetProperty("labels", out var labelsElem) && 
                    labelsElem.ValueKind == JsonValueKind.Array)
                {
                    response.Labels = labelsElem.EnumerateArray()
                        .Select(x => x.GetString() ?? "")
                        .ToArray();
                }

                if (element.TryGetProperty("scores", out var scoresElem) && 
                    scoresElem.ValueKind == JsonValueKind.Array)
                {
                    response.Scores = scoresElem.EnumerateArray()
                        .Select(x => x.GetSingle())
                        .ToArray();
                }

                // Alternative property names
                if ((response.Labels == null || response.Labels.Length == 0) &&
                    element.TryGetProperty("sequence", out var sequenceElem))
                {
                    response.Labels = new[] { "unknown" };
                    response.Scores = new[] { 1.0f };
                }

                return response.Labels != null && response.Scores != null ? response : null;
            }
            catch
            {
                return null;
            }
        }

        private string CleanTextForAnalysis(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            // Remove HTML tags
            text = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", string.Empty);
            
            // Remove URLs
            text = System.Text.RegularExpressions.Regex.Replace(text, @"http[^\s]+", string.Empty);
            
            // Remove excessive whitespace
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            
            // Take only first 200 characters for API calls
            return text.Length > 200 ? text.Substring(0, 200) : text;
        }

        private string MapIntent(string label)
        {
            return label.ToLower() switch
            {
                "schedule meeting" => "CreateEvent",
                "create task" => "CreateTask",
                "set reminder" => "CreateReminder",
                "check calendar" => "CheckCalendar",
                "send email" => "EmailAction",
                "update task" => "UpdateTask",
                _ => "Unknown"
            };
        }

        private Dictionary<string, string> ExtractEntities(string text)
        {
            var entities = new Dictionary<string, string>();
            
            // Enhanced entity extraction
            var lowerText = text.ToLower();
            
            // Date extraction
            if (lowerText.Contains("tomorrow"))
                entities["date"] = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
            else if (lowerText.Contains("today"))
                entities["date"] = DateTime.Today.ToString("yyyy-MM-dd");
            else if (lowerText.Contains("monday") || lowerText.Contains("tuesday") || 
                     lowerText.Contains("wednesday") || lowerText.Contains("thursday") ||
                     lowerText.Contains("friday") || lowerText.Contains("saturday") || 
                     lowerText.Contains("sunday"))
            {
                entities["date"] = GetNextDay(lowerText);
            }
            
            // Time extraction
            var timeMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"(\d{1,2}:\d{2}\s*(?:am|pm)?|\d{1,2}\s*(?:am|pm))", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (timeMatch.Success)
            {
                entities["time"] = timeMatch.Value;
            }
            
            // Priority detection
            if (lowerText.Contains("urgent") || lowerText.Contains("asap") || lowerText.Contains("important"))
                entities["priority"] = "high";

            // Use first few meaningful words as title
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2) // Filter out short words
                .Take(5)
                .ToArray();
                
            entities["title"] = string.Join(" ", words);

            return entities;
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
            
            return DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"); // default to tomorrow
        }

        private NlpResult FallbackParse(string text, string timezone)
        {
            // Enhanced keyword-based fallback
            var result = new NlpResult { Confidence = 0.7 };
            var lowerText = text.ToLower();
            
            if (lowerText.Contains("meeting") || lowerText.Contains("schedule") || 
                lowerText.Contains("appointment") || lowerText.Contains("calendar"))
                result.Intent = "CreateEvent";
            else if (lowerText.Contains("task") || lowerText.Contains("todo") || 
                     lowerText.Contains("trello") || lowerText.Contains("card"))
                result.Intent = "CreateTask";
            else if (lowerText.Contains("remind") || lowerText.Contains("alert") || 
                     lowerText.Contains("notify"))
                result.Intent = "CreateReminder";
            else if (lowerText.Contains("event") || lowerText.Contains("what's on"))
                result.Intent = "CheckCalendar";
            else if (lowerText.Contains("email") || lowerText.Contains("mail") || 
                     lowerText.Contains("inbox"))
                result.Intent = "EmailAction";
            else if (lowerText.Contains("done") || lowerText.Contains("complete") || 
                     lowerText.Contains("finish") || lowerText.Contains("update"))
                result.Intent = "UpdateTask";
            else
                result.Intent = "Unknown";

            result.Entities = ExtractEntities(text);
            return result;
        }
    }

    public class HuggingFaceResponse
    {
        public string[] Labels { get; set; } = Array.Empty<string>();
        public float[] Scores { get; set; } = Array.Empty<float>();
    }
}