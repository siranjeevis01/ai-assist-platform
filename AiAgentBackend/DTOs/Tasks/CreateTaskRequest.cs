// DTOs/Tasks/CreateTaskRequest.cs
namespace AiAgentBackend.DTOs.Tasks
{
    public class CreateTaskRequest
    {
        public string Title { get; set; } = string.Empty;
        public DateTime? DueUtc { get; set; }
        public string? Description { get; set; }
        public string? LabelsCsv { get; set; }
        public string? RecurrenceRule { get; set; }
    }
}