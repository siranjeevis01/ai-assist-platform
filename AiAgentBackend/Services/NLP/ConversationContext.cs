namespace AiAgentBackend.Services.NLP
{
    public class ConversationContext
    {
        public string History { get; set; } = string.Empty;
        public string Timezone { get; set; } = "UTC";
        public string UserId { get; set; } = string.Empty;
        public string Platform { get; set; } = "WhatsApp";
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }
}