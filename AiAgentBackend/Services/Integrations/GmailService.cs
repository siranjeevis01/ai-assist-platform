using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using AiAgentBackend.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;
using MimeKit;
using Microsoft.Extensions.Logging;
using AiAgentBackend.Services.NLP;
using AiAgentBackend.Services.Orchestration;

namespace AiAgentBackend.Services.Integrations
{
    public interface IGmailService
    {
        Task<List<(string subject, string from, bool urgent, string id)>> GetInsightsAsync(int userId, DateTime sinceUtc);
        Task<string> DraftReplyAsync(int userId, string emailId, string kind);
        Task<bool> SendEmailAsync(int userId, string to, string subject, string body);
        Task ProcessIncomingEmails(int userId);
        Task ProcessAllUserEmails();
    }

    public class GmailService : IGmailService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<GmailService> _logger;
        private readonly INlpService _nlpService;
        private readonly IServiceProvider _provider;
        private ICommandOrchestrator? _orchestrator;

        public GmailService(ApplicationDbContext db, ILogger<GmailService> logger, INlpService nlpService, IServiceProvider provider)
        {
            _db = db;
            _logger = logger;
            _nlpService = nlpService;
            _provider = provider;
        }

        private async Task<Google.Apis.Gmail.v1.GmailService> GetGmailServiceAsync(int userId)
        {
            var token = await _db.ProviderTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Google");

            if (token == null)
                throw new Exception("User has not connected Gmail");

            return new Google.Apis.Gmail.v1.GmailService(new Google.Apis.Services.BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(token.EncryptedAccessToken),
                ApplicationName = "AiAgent"
            });
        }

        public async Task<List<(string subject, string from, bool urgent, string id)>> GetInsightsAsync(int userId, DateTime sinceUtc)
        {
            var service = await GetGmailServiceAsync(userId);
            var request = service.Users.Messages.List("me");
            
            request.Q = $"after:{sinceUtc:yyyy/MM/dd}";
            request.MaxResults = 20;
            
            var result = await request.ExecuteAsync();
            var insights = new List<(string, string, bool, string)>();

            if (result.Messages == null) 
                return insights;

            foreach (var msg in result.Messages)
            {
                try
                {
                    var message = await service.Users.Messages.Get("me", msg.Id).ExecuteAsync();
                    
                    var headers = message.Payload.Headers;
                    var subject = headers.FirstOrDefault(h => h.Name == "Subject")?.Value ?? "";
                    var from = headers.FirstOrDefault(h => h.Name == "From")?.Value ?? "";
                    
                    // Check if urgent
                    var subjectLower = subject.ToLower();
                    var isUrgent = subjectLower.Contains("urgent") || 
                                  subjectLower.Contains("asap") || 
                                  subjectLower.Contains("important") ||
                                  message.LabelIds?.Contains("IMPORTANT") == true;
                    
                    // Use NLP to analyze the subject for urgency
                    var nlpResult = await _nlpService.ParseAsync(subject, "UTC");
                    if (nlpResult.Confidence > 0.7 && 
                       (nlpResult.Intent == "CreateTask" || nlpResult.Intent == "CreateReminder"))
                    {
                        isUrgent = true;
                    }

                    insights.Add((subject, from, isUrgent, msg.Id));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process email {MessageId}", msg.Id);
                }
            }

            return insights;
        }

        public async Task<string> DraftReplyAsync(int userId, string emailId, string kind)
        {
            var service = await GetGmailServiceAsync(userId);
            
            // Get the original message
            var message = await service.Users.Messages.Get("me", emailId).ExecuteAsync();
            var headers = message.Payload.Headers;
            
            var subject = headers.FirstOrDefault(h => h.Name == "Subject")?.Value ?? "";
            var from = headers.FirstOrDefault(h => h.Name == "From")?.Value ?? "";
            
            // Create appropriate reply based on kind
            string replyText;
            switch (kind.ToLower())
            {
                case "acknowledge":
                    replyText = $"Hi,\n\nThank you for your email. I've received it and will get back to you soon.\n\nBest regards";
                    break;
                    
                case "meeting":
                    replyText = $"Hi,\n\nThanks for reaching out. I'd be happy to schedule a meeting. Please let me know your availability.\n\nBest regards";
                    break;
                    
                case "task":
                    replyText = $"Hi,\n\nI've added this to my task list and will follow up accordingly.\n\nBest regards";
                    break;
                    
                default:
                    replyText = $"Hi,\n\nThank you for your email.\n\nBest regards";
                    break;
            }

            // Create MIME message
            var mimeMessage = new MimeKit.MimeMessage();
            mimeMessage.From.Add(new MimeKit.MailboxAddress("AI Agent", "ai-agent@example.com"));
            
            // Extract email from "Name <email@example.com>" format
            var fromEmail = from;
            if (from.Contains("<") && from.Contains(">"))
            {
                fromEmail = from.Split('<')[1].Split('>')[0].Trim();
            }
            
            mimeMessage.To.Add(new MimeKit.MailboxAddress("", fromEmail));
            mimeMessage.Subject = "Re: " + subject;
            mimeMessage.Body = new MimeKit.TextPart("plain") { Text = replyText };

            // Convert to RFC 2822 format
            using var stream = new MemoryStream();
            await mimeMessage.WriteToAsync(stream);
            var rawMessage = Convert.ToBase64String(stream.ToArray())
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "");

            // Create draft
            var draft = new Draft
            {
                Message = new Message { Raw = rawMessage }
            };

            var createdDraft = await service.Users.Drafts.Create(draft, "me").ExecuteAsync();
            
            _logger.LogInformation("Created draft reply for email {EmailId} for user {UserId}", emailId, userId);
            
            return createdDraft.Id;
        }

        public async Task<bool> SendEmailAsync(int userId, string to, string subject, string body)
        {
            try
            {
                var service = await GetGmailServiceAsync(userId);
                
                var mimeMessage = new MimeKit.MimeMessage();
                mimeMessage.From.Add(new MimeKit.MailboxAddress("AI Agent", "ai-agent@example.com"));
                mimeMessage.To.Add(new MimeKit.MailboxAddress("", to));
                mimeMessage.Subject = subject;
                mimeMessage.Body = new MimeKit.TextPart("plain") { Text = body };

                using var stream = new MemoryStream();
                await mimeMessage.WriteToAsync(stream);
                var rawMessage = Convert.ToBase64String(stream.ToArray())
                    .Replace('+', '-')
                    .Replace('/', '_')
                    .Replace("=", "");

                var message = new Message { Raw = rawMessage };
                await service.Users.Messages.Send(message, "me").ExecuteAsync();
                
                _logger.LogInformation("Sent email to {To} for user {UserId}", to, userId);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {To} for user {UserId}", to, userId);
                return false;
            }
        }

        public async Task ProcessIncomingEmails(int userId)
        {
            var service = await GetGmailServiceAsync(userId);
            
            // Check for new emails in the inbox
            var request = service.Users.Messages.List("me");
            request.LabelIds = new[] { "INBOX" };
            request.Q = "is:unread";
            
            var result = await request.ExecuteAsync();
            if (result.Messages == null) return;

            foreach (var msg in result.Messages)
            {
                try
                {
                    var message = await service.Users.Messages.Get("me", msg.Id).ExecuteAsync();
                    var headers = message.Payload.Headers;
                    
                    var subject = headers.FirstOrDefault(h => h.Name == "Subject")?.Value ?? "";
                    var from = headers.FirstOrDefault(h => h.Name == "From")?.Value ?? "";
                    
                    // Mark as read
                    await service.Users.Messages.Modify(new ModifyMessageRequest
                    {
                        RemoveLabelIds = new[] { "UNREAD" }
                    }, "me", msg.Id).ExecuteAsync();

                    // Process with NLP
                    var fullText = await GetMessageBody(message);
                    var nlpResult = await _nlpService.ParseAsync($"{subject} {fullText}", "UTC");
                    
                    if (nlpResult.Confidence > 0.6)
                    {
                        // Create task or event based on email content
                        if (nlpResult.Intent == "CreateTask" || nlpResult.Intent == "CreateReminder")
                        {
                            _orchestrator ??= _provider.GetRequiredService<ICommandOrchestrator>();
                            await _orchestrator.HandleAsync(userId, $"{subject} - {fullText}", "Gmail");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process email {MessageId}", msg.Id);
                }
            }
            
            _logger.LogInformation("Processed incoming emails for user {UserId}", userId);
        }

private Task<string> GetMessageBody(Message message)
{
    if (message.Payload.Body?.Data != null)
    {
        return Task.FromResult(DecodeBase64(message.Payload.Body.Data));
    }

    if (message.Payload.Parts != null)
    {
        foreach (var part in message.Payload.Parts)
        {
            if (part.MimeType == "text/plain" && part.Body?.Data != null)
            {
                return Task.FromResult(DecodeBase64(part.Body.Data));
            }
        }
    }

    return Task.FromResult("");
}

        private string DecodeBase64(string base64)
        {
            try
            {
                var data = Convert.FromBase64String(base64.Replace('-', '+').Replace('_', '/'));
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return "";
            }
        }

        public async Task ProcessAllUserEmails()
        {
            try
            {
                var usersWithGoogle = await _db.ProviderTokens
                    .Where(t => t.Provider == "Google")
                    .Select(t => t.UserId)
                    .Distinct()
                    .ToListAsync();

                _logger.LogInformation("Processing emails for {Count} users", usersWithGoogle.Count);

                foreach (var userId in usersWithGoogle)
                {
                    try
                    {
                        await ProcessIncomingEmails(userId);
                        await Task.Delay(1000); // Rate limiting
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process emails for user {UserId}", userId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process all user emails");
            }
        }
    }
}