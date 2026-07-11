using System.Text;
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace AiAgentBackend.Services.Documents
{
    public interface IDocumentService
    {
        Task<Document> UploadAsync(int userId, string fileName, string contentType, Stream content);
        Task<List<Document>> GetUserDocumentsAsync(int userId);
        Task<Document?> GetDocumentAsync(int userId, int documentId);
        Task<bool> DeleteDocumentAsync(int userId, int documentId);
        Task<string> QueryDocumentAsync(int userId, int documentId, string question);
        Task<string> QueryAllDocumentsAsync(int userId, string question);
        Task<string> GetSummaryAsync(int userId, int documentId);
    }

    public class DocumentService : IDocumentService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<DocumentService> _logger;
        private readonly string _storageBasePath;

        public DocumentService(
            ApplicationDbContext db,
            ILogger<DocumentService> logger,
            IConfiguration config)
        {
            _db = db;
            _logger = logger;
            _storageBasePath = config["Storage:BasePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        }

        public async Task<Document> UploadAsync(int userId, string fileName, string contentType, Stream content)
        {
            var userDir = Path.Combine(_storageBasePath, userId.ToString());
            Directory.CreateDirectory(userDir);

            var storedName = $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
            var storagePath = Path.Combine(userDir, storedName);

            using (var fileStream = new FileStream(storagePath, FileMode.Create))
            {
                await content.CopyToAsync(fileStream);
            }

            var text = await ExtractTextAsync(storagePath, contentType);

            var document = new Document
            {
                UserId = userId,
                FileName = fileName,
                ContentType = contentType,
                SizeBytes = new FileInfo(storagePath).Length,
                StoragePath = storagePath,
                ExtractedText = text,
                Summary = text?.Length > 500 ? text[..500] + "..." : text,
                EmbeddingStatus = "completed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Documents.Add(document);
            await _db.SaveChangesAsync();

            if (!string.IsNullOrEmpty(text))
            {
                await CreateChunksAsync(document.Id, text);
            }

            return document;
        }

        public async Task<List<Document>> GetUserDocumentsAsync(int userId)
        {
            return await _db.Documents
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new Document
                {
                    Id = d.Id,
                    FileName = d.FileName,
                    ContentType = d.ContentType,
                    SizeBytes = d.SizeBytes,
                    Summary = d.Summary,
                    EmbeddingStatus = d.EmbeddingStatus,
                    CreatedAt = d.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<Document?> GetDocumentAsync(int userId, int documentId)
        {
            return await _db.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId);
        }

        public async Task<bool> DeleteDocumentAsync(int userId, int documentId)
        {
            var doc = await _db.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId);
            if (doc == null) return false;

            if (File.Exists(doc.StoragePath))
                File.Delete(doc.StoragePath);

            _db.Documents.Remove(doc);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<string> QueryDocumentAsync(int userId, int documentId, string question)
        {
            var doc = await _db.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId);
            if (doc == null) return "Document not found.";

            if (string.IsNullOrEmpty(doc.ExtractedText))
                return "No text content available in this document.";

            return await AnswerQuestionAsync(doc.ExtractedText, question);
        }

        public async Task<string> QueryAllDocumentsAsync(int userId, string question)
        {
            var docs = await _db.Documents
                .Where(d => d.UserId == userId && d.ExtractedText != null)
                .OrderByDescending(d => d.CreatedAt)
                .Take(10)
                .ToListAsync();

            if (!docs.Any())
                return "No documents found to query.";

            var combinedText = string.Join("\n\n---\n\n",
                docs.Select(d => $"[{d.FileName}]:\n{d.ExtractedText}"));

            return await AnswerQuestionAsync(combinedText, question);
        }

        public async Task<string> GetSummaryAsync(int userId, int documentId)
        {
            var doc = await _db.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId);
            if (doc == null) return "Document not found.";
            return doc.Summary ?? "No summary available.";
        }

        private async Task<string?> ExtractTextAsync(string filePath, string contentType)
        {
            try
            {
                return contentType.ToLower() switch
                {
                    "text/plain" or "text/csv" => await File.ReadAllTextAsync(filePath),
                    "application/json" => await File.ReadAllTextAsync(filePath),
                    "text/html" or "text/xml" or "text/markdown" => await File.ReadAllTextAsync(filePath),
                    _ when contentType.Contains("pdf") => $"[PDF document: {Path.GetFileName(filePath)} - text extraction requires additional library]",
                    _ when contentType.Contains("word") => $"[Word document: {Path.GetFileName(filePath)} - text extraction requires additional library]",
                    _ => $"[File: {Path.GetFileName(filePath)} ({contentType})]"
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract text from {FilePath}", filePath);
                return null;
            }
        }

        private async Task CreateChunksAsync(int documentId, string text)
        {
            var chunkSize = 500;
            var overlap = 50;
            var chunks = new List<DocumentChunk>();

            for (var i = 0; i < text.Length; i += chunkSize - overlap)
            {
                var length = Math.Min(chunkSize, text.Length - i);
                var chunkText = text.Substring(i, length);

                chunks.Add(new DocumentChunk
                {
                    DocumentId = documentId,
                    ChunkIndex = chunks.Count,
                    Content = chunkText,
                    TokenCount = chunkText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                    CreatedAt = DateTime.UtcNow
                });

                if (i + chunkSize >= text.Length) break;
            }

            _db.DocumentChunks.AddRange(chunks);
            await _db.SaveChangesAsync();
        }

        private Task<string> AnswerQuestionAsync(string context, string question)
        {
            var questionLower = question.ToLower();
            var sentences = context.Split(new[] { '.', '!', '?', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var scored = new List<(string sentence, double score)>();

            var questionWords = questionLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2)
                .ToHashSet();

            foreach (var sentence in sentences)
            {
                var sentenceLower = sentence.ToLower().Trim();
                if (string.IsNullOrWhiteSpace(sentenceLower) || sentenceLower.Length < 10) continue;

                var score = questionWords.Count(w => sentenceLower.Contains(w)) / (double)questionWords.Count;
                if (score > 0) scored.Add((sentence.Trim(), score));
            }

            if (!scored.Any())
            {
                return Task.FromResult($"Based on the document, I couldn't find a direct answer to: \"{question}\"\n\nThe document contains {sentences.Length} sentences of content. Try rephrasing your question.");
            }

            var topSentences = scored
                .OrderByDescending(s => s.score)
                .Take(3)
                .Select(s => s.sentence);

            var answer = string.Join(". ", topSentences);
            return Task.FromResult($"Based on the document:\n\n{answer}\n\n_({scored.Count} relevant passages found)_");
        }
    }
}
