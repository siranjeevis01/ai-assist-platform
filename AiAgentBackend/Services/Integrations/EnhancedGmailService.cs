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
using AiAgentBackend.Models;
using System.Text.Json;

namespace AiAgentBackend.Services.Integrations
{
    public interface IEnhancedGmailService : IGmailService
    {
        Task<List<GmailEmail>> GetEmailsAsync(int userId, string query = "", int maxResults = 50);
        Task<GmailEmail> GetEmailAsync(int userId, string emailId);
        Task<bool> MarkAsReadAsync(int userId, string emailId);
        Task<bool> MarkAsUnreadAsync(int userId, string emailId);
        Task<bool> ArchiveEmailAsync(int userId, string emailId);
        Task<bool> MoveToLabelAsync(int userId, string emailId, string labelName);
        Task<List<GmailLabel>> GetLabelsAsync(int userId);
        Task<string> SendEmailAsync(int userId, GmailEmail email);
        Task<string> DraftEmailAsync(int userId, GmailEmail email);
        Task<bool> DeleteEmailAsync(int userId, string emailId);
        Task<List<GmailEmail>> SearchEmailsAsync(int userId, string searchQuery, int maxResults = 20);
        Task<bool> AddLabelAsync(int userId, string emailId, string labelId);
        Task<bool> RemoveLabelAsync(int userId, string emailId, string labelId);
    }

public class EmailIntelligenceService
{
    private readonly INlpService _aiService;

    public EmailIntelligenceService(INlpService aiService)
    {
        _aiService = aiService;
    }

    public async Task ProcessEmailForAutomationAsync(int userId, GmailEmail email)
    {
        var analysis = await _aiService.ParseAsync($"{email.Subject} {email.Body}", "UTC");

        if (analysis.Intent == "CreateEvent")
        {
            await CreateEventFromEmailAsync(userId, email, analysis);
        }
        else if (analysis.Intent == "CreateTask")
        {
            await CreateTaskFromEmailAsync(userId, email, analysis);
        }

        if (analysis.Intent == "EmailAction")
        {
            await DraftSmartReplyAsync(userId, email, analysis);
        }
    }

    private Task CreateEventFromEmailAsync(int userId, GmailEmail email, NlpResult analysis)
    {
        Console.WriteLine($"[DEBUG] Creating event for user {userId} from email {email.Subject}");
        return Task.CompletedTask;
    }

    private Task CreateTaskFromEmailAsync(int userId, GmailEmail email, NlpResult analysis)
    {
        Console.WriteLine($"[DEBUG] Creating task for user {userId} from email {email.Subject}");
        return Task.CompletedTask;
    }

    private Task DraftSmartReplyAsync(int userId, GmailEmail email, NlpResult analysis)
    {
        Console.WriteLine($"[DEBUG] Drafting smart reply for user {userId} (email {email.Subject})");
        return Task.CompletedTask;
    }
}

    public class EnhancedGmailService : IGmailService, IEnhancedGmailService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<EnhancedGmailService> _logger;
        private readonly INlpService _nlpService;
        private readonly IServiceProvider _provider;
        private ICommandOrchestrator? _orchestrator;
        private readonly IHttpWhatsAppService _whatsAppService;

        public EnhancedGmailService(
            ApplicationDbContext db, 
            ILogger<EnhancedGmailService> logger, 
            INlpService nlpService,
            IServiceProvider provider,
            IHttpWhatsAppService whatsAppService) 
        {
            _db = db;
            _logger = logger;
            _nlpService = nlpService;
            _provider = provider;
            _whatsAppService = whatsAppService;
        }

        private async Task<Google.Apis.Gmail.v1.GmailService> GetGmailServiceAsync(int userId)
        {
            var token = await _db.ProviderTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Google");

            if (token == null)
                throw new Exception("User has not connected Gmail");

            return new Google.Apis.Gmail.v1.GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(token.EncryptedAccessToken),
                ApplicationName = "AiAgent"
            });
        }

        public async Task<List<GmailEmail>> GetEmailsAsync(int userId, string query = "", int maxResults = 50)
        {
            var service = await GetGmailServiceAsync(userId);
            var request = service.Users.Messages.List("me");
            
            if (!string.IsNullOrEmpty(query))
                request.Q = query;
                
            request.MaxResults = maxResults;
            
            var result = await request.ExecuteAsync();
            var emails = new List<GmailEmail>();

            if (result.Messages == null) 
                return emails;

            foreach (var msg in result.Messages)
            {
                try
                {
                    var email = await GetEmailAsync(userId, msg.Id);
                    emails.Add(email);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process email {MessageId}", msg.Id);
                }
            }

            return emails;
        }

        public async Task<GmailEmail> GetEmailAsync(int userId, string emailId)
        {
            var service = await GetGmailServiceAsync(userId);
            var message = await service.Users.Messages.Get("me", emailId).ExecuteAsync();
            
            var headers = message.Payload.Headers;
            var subject = headers.FirstOrDefault(h => h.Name == "Subject")?.Value ?? "";
            var from = headers.FirstOrDefault(h => h.Name == "From")?.Value ?? "";
            var to = headers.FirstOrDefault(h => h.Name == "To")?.Value ?? "";
            var date = headers.FirstOrDefault(h => h.Name == "Date")?.Value ?? "";
            var cc = headers.FirstOrDefault(h => h.Name == "Cc")?.Value ?? "";

            var body = await GetMessageBody(message);
            var snippet = message.Snippet;
            var isUnread = message.LabelIds?.Contains("UNREAD") == true;
            var isImportant = message.LabelIds?.Contains("IMPORTANT") == true;
            var isStarred = message.LabelIds?.Contains("STARRED") == true;

            return new GmailEmail
            {
                Id = emailId,
                Subject = subject,
                From = from,
                To = to,
                Cc = cc,
                Date = DateTime.TryParse(date, out var parsedDate) ? parsedDate : DateTime.UtcNow,
                Body = body,
                Snippet = snippet,
                IsUnread = isUnread,
                IsImportant = isImportant,
                IsStarred = isStarred,
                LabelIds = message.LabelIds?.ToList() ?? new List<string>()
            };
        }

        public async Task<bool> MarkAsReadAsync(int userId, string emailId)
        {
            try
            {
                var service = await GetGmailServiceAsync(userId);
                await service.Users.Messages.Modify(new ModifyMessageRequest
                {
                    RemoveLabelIds = new[] { "UNREAD" }
                }, "me", emailId).ExecuteAsync();

                _logger.LogInformation("Marked email {EmailId} as read for user {UserId}", emailId, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark email {EmailId} as read", emailId);
                return false;
            }
        }

        public async Task<bool> MarkAsUnreadAsync(int userId, string emailId)
        {
            try
            {
                var service = await GetGmailServiceAsync(userId);
                await service.Users.Messages.Modify(new ModifyMessageRequest
                {
                    AddLabelIds = new[] { "UNREAD" }
                }, "me", emailId).ExecuteAsync();

                _logger.LogInformation("Marked email {EmailId} as unread for user {UserId}", emailId, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark email {EmailId} as unread", emailId);
                return false;
            }
        }

        public async Task<bool> ArchiveEmailAsync(int userId, string emailId)
        {
            try
            {
                var service = await GetGmailServiceAsync(userId);
                await service.Users.Messages.Modify(new ModifyMessageRequest
                {
                    RemoveLabelIds = new[] { "INBOX" }
                }, "me", emailId).ExecuteAsync();

                _logger.LogInformation("Archived email {EmailId} for user {UserId}", emailId, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to archive email {EmailId}", emailId);
                return false;
            }
        }

        public async Task<bool> MoveToLabelAsync(int userId, string emailId, string labelName)
        {
            try
            {
                var service = await GetGmailServiceAsync(userId);
                var labels = await GetLabelsAsync(userId);
                var label = labels.FirstOrDefault(l => l.Name.Equals(labelName, StringComparison.OrdinalIgnoreCase));

                if (label == null)
                    return false;

                await service.Users.Messages.Modify(new ModifyMessageRequest
                {
                    AddLabelIds = new[] { label.Id }
                }, "me", emailId).ExecuteAsync();

                _logger.LogInformation("Moved email {EmailId} to label {LabelName}", emailId, labelName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move email {EmailId} to label {LabelName}", emailId, labelName);
                return false;
            }
        }

        public async Task<List<GmailLabel>> GetLabelsAsync(int userId)
        {
            var service = await GetGmailServiceAsync(userId);
            var request = service.Users.Labels.List("me");
            var result = await request.ExecuteAsync();

            return result.Labels?.Select(l => new GmailLabel
            {
                Id = l.Id,
                Name = l.Name,
                Type = l.Type,
                MessageListVisibility = l.MessageListVisibility,
                LabelListVisibility = l.LabelListVisibility
            }).ToList() ?? new List<GmailLabel>();
        }

        public async Task<string> SendEmailAsync(int userId, GmailEmail email)
        {
            try
            {
                var service = await GetGmailServiceAsync(userId);
                
                var mimeMessage = new MimeMessage();
                mimeMessage.From.Add(new MailboxAddress("", email.From));
                mimeMessage.To.Add(new MailboxAddress("", email.To));
                
                if (!string.IsNullOrEmpty(email.Cc))
                    mimeMessage.Cc.Add(new MailboxAddress("", email.Cc));
                
                mimeMessage.Subject = email.Subject;
                mimeMessage.Body = new TextPart("plain") { Text = email.Body };

                using var stream = new MemoryStream();
                await mimeMessage.WriteToAsync(stream);
                var rawMessage = Convert.ToBase64String(stream.ToArray())
                    .Replace('+', '-')
                    .Replace('/', '_')
                    .Replace("=", "");

                var message = new Google.Apis.Gmail.v1.Data.Message { Raw = rawMessage };
                var sentMessage = await service.Users.Messages.Send(message, "me").ExecuteAsync();
                
                _logger.LogInformation("Sent email to {To} for user {UserId}", email.To, userId);
                return sentMessage.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email for user {UserId}", userId);
                throw;
            }
        }

        public async Task<string> DraftEmailAsync(int userId, GmailEmail email)
        {
            try
            {
                var service = await GetGmailServiceAsync(userId);
                
                var mimeMessage = new MimeMessage();
                mimeMessage.From.Add(new MailboxAddress("", email.From));
                mimeMessage.To.Add(new MailboxAddress("", email.To));
                
                if (!string.IsNullOrEmpty(email.Cc))
                    mimeMessage.Cc.Add(new MailboxAddress("", email.Cc));
                
                mimeMessage.Subject = email.Subject;
                mimeMessage.Body = new TextPart("plain") { Text = email.Body };

                using var stream = new MemoryStream();
                await mimeMessage.WriteToAsync(stream);
                var rawMessage = Convert.ToBase64String(stream.ToArray())
                    .Replace('+', '-')
                    .Replace('/', '_')
                    .Replace("=", "");

                var draft = new Draft
                {
                    Message = new Google.Apis.Gmail.v1.Data.Message { Raw = rawMessage }
                };

                var createdDraft = await service.Users.Drafts.Create(draft, "me").ExecuteAsync();
                
                _logger.LogInformation("Created draft email for user {UserId}", userId);
                return createdDraft.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create draft email for user {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> DeleteEmailAsync(int userId, string emailId)
        {
            try
            {
                var service = await GetGmailServiceAsync(userId);
                await service.Users.Messages.Trash("me", emailId).ExecuteAsync();
                
                _logger.LogInformation("Deleted email {EmailId} for user {UserId}", emailId, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete email {EmailId}", emailId);
                return false;
            }
        }

        public async Task<List<GmailEmail>> SearchEmailsAsync(int userId, string searchQuery, int maxResults = 20)
        {
            return await GetEmailsAsync(userId, searchQuery, maxResults);
        }

        public async Task<bool> AddLabelAsync(int userId, string emailId, string labelId)
        {
            try
            {
                var service = await GetGmailServiceAsync(userId);
                await service.Users.Messages.Modify(new ModifyMessageRequest
                {
                    AddLabelIds = new[] { labelId }
                }, "me", emailId).ExecuteAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add label to email {EmailId}", emailId);
                return false;
            }
        }

        public async Task<bool> RemoveLabelAsync(int userId, string emailId, string labelId)
        {
            try
            {
                var service = await GetGmailServiceAsync(userId);
                await service.Users.Messages.Modify(new ModifyMessageRequest
                {
                    RemoveLabelIds = new[] { labelId }
                }, "me", emailId).ExecuteAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove label from email {EmailId}", emailId);
                return false;
            }
        }

        // Implement existing IGmailService methods
        public async Task<List<(string subject, string from, bool urgent, string id)>> GetInsightsAsync(int userId, DateTime sinceUtc)
        {
            var emails = await GetEmailsAsync(userId, $"after:{sinceUtc:yyyy/MM/dd}", 50);
            var insights = new List<(string, string, bool, string)>();

            foreach (var email in emails)
            {
                var isUrgent = email.IsImportant || 
                              email.Subject?.ToLower().Contains("urgent") == true ||
                              email.Subject?.ToLower().Contains("asap") == true;

                insights.Add((email.Subject ?? "", email.From ?? "", isUrgent, email.Id));
            }

            return insights;
        }

        public async Task<string> DraftReplyAsync(int userId, string emailId, string kind)
        {
            var originalEmail = await GetEmailAsync(userId, emailId);
            var replyText = kind.ToLower() switch
            {
                "acknowledge" => $"Hi,\n\nThank you for your email. I've received it and will get back to you soon.\n\nBest regards",
                "meeting" => $"Hi,\n\nThanks for reaching out. I'd be happy to schedule a meeting. Please let me know your availability.\n\nBest regards",
                "task" => $"Hi,\n\nI've added this to my task list and will follow up accordingly.\n\nBest regards",
                _ => $"Hi,\n\nThank you for your email.\n\nBest regards"
            };

            var replyEmail = new GmailEmail
            {
                From = originalEmail.To, // Reply from the original recipient
                To = originalEmail.From,
                Subject = $"Re: {originalEmail.Subject}",
                Body = replyText
            };

            return await DraftEmailAsync(userId, replyEmail);
        }

        Task<bool> IGmailService.SendEmailAsync(int userId, string to, string subject, string body)
        {
            var email = new GmailEmail
            {
                From = "ai-agent@example.com", // This should be the user's email
                To = to,
                Subject = subject,
                Body = body
            };

            return SendEmailAsync(userId, email).ContinueWith(t => !string.IsNullOrEmpty(t.Result));
        }

    public async Task ProcessIncomingEmails(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null || string.IsNullOrEmpty(user.PhoneNumber))
        {
            _logger.LogWarning("User {UserId} has no phone number, skipping Gmail processing", userId);
            return;
        }         
           
        var unreadEmails = await GetEmailsAsync(userId, "is:unread", 10);
        
        foreach (var email in unreadEmails)
        {
            try
            {
                // Analyze email content
                var analysis = await _nlpService.ParseAsync($"{email.Subject} {email.Body}", "UTC");
                
                if (analysis.Confidence > 0.6)
                {
                    _orchestrator ??= _provider.GetRequiredService<ICommandOrchestrator>();
                    var response = await _orchestrator.HandleAsync(userId, $"{email.Subject} - {email.Body}", "Gmail");
                    
                    // Send WhatsApp notification for important emails
                    if (email.IsImportant || analysis.Confidence > 0.8)
                    {
                        await _whatsAppService.SendMessageAsync( // Use _whatsAppService
                            userId, 
                            $"📧 Important email: {email.Subject} from {email.From}"
                        );
                    }
                }

                // Mark as processed
                await MarkAsReadAsync(userId, email.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process email {EmailId}", email.Id);
            }
        }
    }

        public async Task ProcessAllUserEmails()
        {
            var usersWithGoogle = await _db.ProviderTokens
                .Where(t => t.Provider == "Google")
                .Select(t => t.UserId)
                .Distinct()
                .ToListAsync();

            foreach (var userId in usersWithGoogle)
            {
                try
                {
                    await ProcessIncomingEmails(userId);
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process emails for user {UserId}", userId);
                }
            }
        }

        private Task<string> GetMessageBody(Google.Apis.Gmail.v1.Data.Message message)
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

            return Task.FromResult(message.Snippet ?? "");
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
    }

    public class GmailEmail
    {
        public string Id { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Cc { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Body { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        public bool IsUnread { get; set; }
        public bool IsImportant { get; set; }
        public bool IsStarred { get; set; }
        public List<string> LabelIds { get; set; } = new List<string>();
    }

    public class GmailLabel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string MessageListVisibility { get; set; } = string.Empty;
        public string LabelListVisibility { get; set; } = string.Empty;
    }
}