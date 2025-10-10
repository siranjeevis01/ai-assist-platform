using Microsoft.Extensions.Logging;

namespace AiAgentBackend.Services.NLP
{
    public class EnhancedNlpService : INlpService
    {
        private readonly ILogger<EnhancedNlpService> _logger;
        private readonly INlpService _primaryService;
        private readonly INlpService _fallbackService;

        public EnhancedNlpService(
            ILogger<EnhancedNlpService> logger,
            OpenAiNlpService primaryService,
            FreeNlpService fallbackService)
        {
            _logger = logger;
            _primaryService = primaryService;
            _fallbackService = fallbackService;
        }

        public async Task<NlpResult> ParseAsync(string text, string timezone)
        {
            // Try primary service first (OpenAI)
            try
            {
                var result = await _primaryService.ParseAsync(text, timezone);
                if (result.Confidence > 0.6) // Only use if confident
                {
                    _logger.LogInformation("Using OpenAI NLP with confidence: {Confidence}", result.Confidence);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("OpenAI NLP failed, falling back: {Error}", ex.Message);
            }

            // Fall back to free service
            try
            {
                var result = await _fallbackService.ParseAsync(text, timezone);
                _logger.LogInformation("Using Free NLP with confidence: {Confidence}", result.Confidence);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("All NLP services failed: {Error}", ex.Message);
                
                // Ultimate fallback
                return new NlpResult
                {
                    Intent = "Unknown",
                    Confidence = 0.5,
                    Entities = new Dictionary<string, string>
                    {
                        ["title"] = text.Length > 50 ? text.Substring(0, 50) : text,
                        ["fallback"] = "true"
                    }
                };
            }
        }
    }
}