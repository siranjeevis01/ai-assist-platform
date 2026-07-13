// Configuration/TrelloOptions.cs
namespace AiAgentBackend.Configuration
{
    public class TrelloOptions
    {
        // OAuth 1.0A fields
        public string ConsumerKey { get; set; } = string.Empty;
        public string ConsumerSecret { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;

        // Shared fallback (backward compat)
        public string ApiKey { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;

        // Board/list config
        public string DefaultBoardId { get; set; } = string.Empty;
        public string ToDoListId { get; set; } = string.Empty;
        public string InProgressListId { get; set; } = string.Empty;
        public string DoneListId { get; set; } = string.Empty;
    }
}