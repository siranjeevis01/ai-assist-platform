// DTOs/Tasks/UpdateTaskRequest.cs
namespace AiAgentBackend.DTOs.Tasks
{
    public class UpdateTaskRequest
    {
        public string? Title { get; set; }
        public string? Status { get; set; }
        public DateTime? DueUtc { get; set; }
        public string? Description { get; set; }
        public string? LabelsCsv { get; set; }
    }
}