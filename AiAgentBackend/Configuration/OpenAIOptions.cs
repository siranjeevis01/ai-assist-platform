// Configuration/OpenAIOptions.cs
namespace AiAgentBackend.Configuration
{
    public class OpenAIOptions
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-4o-mini";
        public decimal Temperature { get; set; } = 0.1m;
        public int MaxTokens { get; set; } = 500;
    }
}