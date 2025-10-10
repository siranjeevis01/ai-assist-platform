// Configuration/TrelloOptions.cs
namespace AiAgentBackend.Configuration
{
    public class TrelloOptions
    {
        public string ApiKey { get; set; } = string.Empty;
        public string ApiToken { get; set; } = string.Empty;
        public string DefaultBoardId { get; set; } = string.Empty;
        public string ToDoListId { get; set; } = string.Empty;
        public string InProgressListId { get; set; } = string.Empty;
        public string DoneListId { get; set; } = string.Empty;
    }
}