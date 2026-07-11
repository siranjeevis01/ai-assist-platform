using System.Text;
using System.Text.Json;

namespace AiAgentBackend.Services.Voice
{
    public interface IVoiceService
    {
        Task<string> TranscribeAudioAsync(byte[] audioData, string mimeType);
        Task<byte[]> TextToSpeechAsync(string text, string language = "en");
    }

    public class VoiceService : IVoiceService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<VoiceService> _logger;
        private readonly string _geminiApiKey;

        public VoiceService(
            HttpClient httpClient,
            ILogger<VoiceService> logger,
            IConfiguration config)
        {
            _httpClient = httpClient;
            _logger = logger;
            _geminiApiKey = config["Gemini:ApiKey"] ?? "";
        }

        public async Task<string> TranscribeAudioAsync(byte[] audioData, string mimeType)
        {
            if (string.IsNullOrEmpty(_geminiApiKey))
            {
                _logger.LogWarning("Gemini API key not configured, falling back to simple transcription");
                return "[Voice transcription requires Gemini API key. Configure GEMINI_API_KEY environment variable.]";
            }

            try
            {
                var base64Audio = Convert.ToBase64String(audioData);

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = "Transcribe this audio exactly as spoken. Return only the transcription text, nothing else." },
                                new
                                {
                                    inlineData = new
                                    {
                                        mimeType = mimeType,
                                        data = base64Audio
                                    }
                                }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.1,
                        maxOutputTokens = 1024
                    }
                };

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={_geminiApiKey}";
                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString() ?? "";

                return text.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Voice transcription failed");
                return $"[Transcription failed: {ex.Message}]";
            }
        }

        public Task<byte[]> TextToSpeechAsync(string text, string language = "en")
        {
            _logger.LogInformation("TTS requested for text ({Length} chars) in {Language}", text.Length, language);

            return Task.FromResult(Encoding.UTF8.GetBytes(
                $"[TTS not yet implemented - would synthesize: \"{text}\"]"));
        }
    }
}
