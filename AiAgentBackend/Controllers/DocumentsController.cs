using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DocumentsController : ControllerBase
    {
        private readonly IDocumentService _documents;
        private readonly ILogger<DocumentsController> _logger;

        public DocumentsController(IDocumentService documents, ILogger<DocumentsController> logger)
        {
            _documents = documents;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetDocuments()
        {
            var userId = GetUserId();
            var docs = await _documents.GetUserDocumentsAsync(userId);
            return Ok(docs);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetDocument(int id)
        {
            var userId = GetUserId();
            var doc = await _documents.GetDocumentAsync(userId, id);
            if (doc == null) return NotFound();
            return Ok(new
            {
                doc.Id,
                doc.FileName,
                doc.ContentType,
                doc.SizeBytes,
                doc.Summary,
                doc.EmbeddingStatus,
                doc.CreatedAt,
                textPreview = doc.ExtractedText?.Length > 1000
                    ? doc.ExtractedText[..1000] + "..."
                    : doc.ExtractedText
            });
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided" });

            if (file.Length > 50 * 1024 * 1024)
                return BadRequest(new { error = "File too large (max 50MB)" });

            var allowedTypes = new[] {
                "text/plain", "text/csv", "text/html", "text/xml", "text/markdown",
                "application/json", "application/pdf",
                "application/msword",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            };

            if (!allowedTypes.Contains(file.ContentType))
                return BadRequest(new { error = $"Unsupported file type: {file.ContentType}" });

            var userId = GetUserId();
            using var stream = file.OpenReadStream();
            var doc = await _documents.UploadAsync(userId, file.FileName, file.ContentType, stream);

            _logger.LogInformation("User {UserId} uploaded document: {FileName}", userId, file.FileName);

            return Ok(new
            {
                doc.Id,
                doc.FileName,
                doc.ContentType,
                doc.SizeBytes,
                doc.Summary,
                message = "Document uploaded successfully"
            });
        }

        [HttpPost("{id}/query")]
        public async Task<IActionResult> QueryDocument(int id, [FromBody] QueryRequest request)
        {
            var userId = GetUserId();
            var answer = await _documents.QueryDocumentAsync(userId, id, request.Question);
            return Ok(new { answer });
        }

        [HttpPost("query-all")]
        public async Task<IActionResult> QueryAllDocuments([FromBody] QueryRequest request)
        {
            var userId = GetUserId();
            var answer = await _documents.QueryAllDocumentsAsync(userId, request.Question);
            return Ok(new { answer });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            var userId = GetUserId();
            var result = await _documents.DeleteDocumentAsync(userId, id);
            if (!result) return NotFound();
            return Ok(new { message = "Document deleted" });
        }

        private int GetUserId() => int.Parse(User.FindFirst("uid")?.Value ?? User.FindFirst("sub")?.Value ?? "0");
    }

    public class QueryRequest
    {
        public string Question { get; set; } = string.Empty;
    }
}
