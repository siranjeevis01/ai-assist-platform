using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.Models
{
    public class Document
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string StoragePath { get; set; } = string.Empty;
        public string? ExtractedText { get; set; }
        public string? Summary { get; set; }
        public string EmbeddingStatus { get; set; } = "pending";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual User User { get; set; } = null!;
        public virtual ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
    }

    public class DocumentChunk
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public int ChunkIndex { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? EmbeddingVector { get; set; }
        public int TokenCount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual Document Document { get; set; } = null!;
    }
}
