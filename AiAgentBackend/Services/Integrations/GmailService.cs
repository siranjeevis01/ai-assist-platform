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
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Auth.OAuth2.Flows;
using System.Text.RegularExpressions;
using AiAgentBackend.Services.Integrations;

namespace AiAgentBackend.Services.Integrations
{
    public interface IGmailService
    {
        // Core Gmail operations
        Task<List<GmailEmail>> GetEmailsAsync(int userId, string query = "", int maxResults = 50);
        Task<GmailEmail> GetEmailAsync(int userId, string emailId);
        Task<string> SendEmailAsync(int userId, GmailEmail email);
        Task<bool> SendEmailAsync(int userId, string to, string subject, string body);
        Task<string> DraftEmailAsync(int userId, GmailEmail email);
        Task<string> DraftReplyAsync(int userId, string emailId, string kind);
        
        // Email management
        Task<bool> MarkAsReadAsync(int userId, string emailId);
        Task<bool> MarkAsUnreadAsync(int userId, string emailId);
        Task<bool> ArchiveEmailAsync(int userId, string emailId);
        Task<bool> DeleteEmailAsync(int userId, string emailId);
        
        // Labels and organization
        Task<List<GmailLabel>> GetLabelsAsync(int userId);
        Task<bool> MoveToLabelAsync(int userId, string emailId, string labelName);
        Task<bool> AddLabelAsync(int userId, string emailId, string labelId);
        Task<bool> RemoveLabelAsync(int userId, string emailId, string labelId);
        
        // Search and insights
        Task<List<GmailEmail>> SearchEmailsAsync(int userId, string searchQuery, int maxResults = 20);
        Task<List<(string subject, string from, bool urgent, string id)>> GetInsightsAsync(int userId, DateTime sinceUtc);
        
        // Automation and processing
        Task ProcessIncomingEmails(int userId);
        Task ProcessAllUserEmails();
        
        // Token management
        Task<bool> RefreshGmailToken(int userId);
        
        // NEW: Add the missing method for ProactiveNotificationService
        Task<List<EmailInfo>> GetUnreadEmailsAsync(int userId, int maxResults = 5);
    }

    // Add this class for the EmailInfo type
    public class EmailInfo
    {
        public string Id { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public bool IsImportant { get; set; }
        public DateTime ReceivedAt { get; set; }
    }

    public partial class GmailService : IGmailService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<GmailService> _logger;
        private readonly INlpService _nlpService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        // FIXED: Removed unused _orchestrator field

        public GmailService(
            ApplicationDbContext db, 
            ILogger<GmailService> logger, 
            INlpService nlpService,
            IServiceProvider serviceProvider,
            IConfiguration configuration) 
        {
            _db = db;
            _logger = logger;
            _nlpService = nlpService;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        #region Email Address Parsing
        [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.IgnoreCase)]
        private static partial Regex EmailRegex();

        private bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                return EmailRegex().IsMatch(email.Trim());
            }
            catch
            {
                return false;
            }
        }

        private MailboxAddress ParseEmailAddress(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Email address cannot be empty");

            try
            {
                input = input.Trim();
                string email = string.Empty;
                string name = string.Empty;

                var emailMatch = EmailRegex().Match(input);
                if (emailMatch.Success)
                {
                    email = emailMatch.Value.Trim();
                    name = input.Replace(email, "")
                                .Replace("<", "")
                                .Replace(">", "")
                                .Replace("(", "")
                                .Replace(")", "")
                                .Trim();
                    
                    name = name.Trim(' ', '"', '\'');
                    
                    if (EmailRegex().IsMatch(name))
                    {
                        name = string.Empty;
                    }
                }
                else
                {
                    if (EmailRegex().IsMatch(input))
                    {
                        email = input.Trim();
                    }
                    else
                    {
                        throw new FormatException($"No valid email address found in: {input}");
                    }
                }

                if (!IsValidEmail(email))
                {
                    throw new FormatException($"Invalid email address: {email}");
                }

                return string.IsNullOrEmpty(name) 
                    ? new MailboxAddress("", email)
                    : new MailboxAddress(name, email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse email address: {Input}", input);
                throw new FormatException($"Invalid email address format: {input}", ex);
            }
        }

        private List<MailboxAddress> ParseEmailAddressList(string emailList)
        {
            var addresses = new List<MailboxAddress>();
            
            if (string.IsNullOrWhiteSpace(emailList))
                return addresses;

            try
            {
                var emailStrings = emailList.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var emailString in emailStrings)
                {
                    try
                    {
                        var address = ParseEmailAddress(emailString.Trim());
                        addresses.Add(address);
                    }
                    catch (FormatException ex)
                    {
                        _logger.LogWarning("Skipping invalid email address: {Email} - {Error}", emailString, ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse email address list: {EmailList}", emailList);
            }

            return addresses;
        }

        private string ExtractEmailAddress(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            try
            {
                var match = EmailRegex().Match(input);
                return match.Success ? match.Value : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
        #endregion

        #region Gmail API Service Management
        private async Task<Google.Apis.Gmail.v1.GmailService> GetGmailServiceAsync(int userId)
        {
            var token = await _db.ProviderTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Google");

            if (token == null)
                throw new Exception("User has not connected Gmail");

            if (token.ExpiresAt.HasValue && token.ExpiresAt.Value < DateTime.UtcNow.AddMinutes(5))
            {
                await RefreshGoogleToken(token);
            }

            return new Google.Apis.Gmail.v1.GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(token.EncryptedAccessToken),
                ApplicationName = "AiAgent"
            });
        }

        private async Task<bool> RefreshGoogleToken(ProviderToken token)
        {
            try
            {
                _logger.LogInformation("Refreshing Google token for user {UserId}", token.UserId);

                var clientId = _configuration["Google:ClientId"];
                var clientSecret = _configuration["Google:ClientSecret"];

                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    _logger.LogError("Google OAuth credentials not configured");
                    return false;
                }

                var tokenResponse = new TokenResponse
                {
                    AccessToken = token.EncryptedAccessToken,
                    RefreshToken = token.RefreshToken,
                    ExpiresInSeconds = 3600,
                    IssuedUtc = DateTime.UtcNow
                };

                var initializer = new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = clientId,
                        ClientSecret = clientSecret
                    },
                    Scopes = new[] { 
                        "https://www.googleapis.com/auth/gmail.readonly",
                        "https://www.googleapis.com/auth/gmail.send", 
                        "https://www.googleapis.com/auth/gmail.modify",
                        "https://www.googleapis.com/auth/gmail.compose"
                    }
                };

                var flow = new GoogleAuthorizationCodeFlow(initializer);

                if (!string.IsNullOrEmpty(token.RefreshToken))
                {
                    var newToken = await flow.RefreshTokenAsync(
                        userId: token.UserId.ToString(),
                        refreshToken: token.RefreshToken,
                        CancellationToken.None);

                    if (newToken != null)
                    {
                        token.EncryptedAccessToken = newToken.AccessToken;
                        token.ExpiresAt = DateTime.UtcNow.AddSeconds(newToken.ExpiresInSeconds ?? 3600);
                        
                        if (!string.IsNullOrEmpty(newToken.RefreshToken))
                        {
                            token.RefreshToken = newToken.RefreshToken;
                        }

                        _db.ProviderTokens.Update(token);
                        await _db.SaveChangesAsync();

                        _logger.LogInformation("Successfully refreshed Google token for user {UserId}", token.UserId);
                        return true;
                    }
                }

                _logger.LogWarning("Unable to refresh token for user {UserId} - no refresh token available", token.UserId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh Google token for user {UserId}", token.UserId);
                return false;
            }
        }

        public async Task<bool> RefreshGmailToken(int userId)
        {
            try
            {
                var token = await _db.ProviderTokens
                    .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Google");
                    
                if (token == null) 
                {
                    _logger.LogWarning("No Google token found for user {UserId}", userId);
                    return false;
                }
                
                return await RefreshGoogleToken(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh Gmail token for user {UserId}", userId);
                return false;
            }
        }
        #endregion

        #region Email Operations
        public async Task<List<GmailEmail>> GetEmailsAsync(int userId, string query = "", int maxResults = 50)
        {
            try
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
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                _logger.LogWarning("Token expired for user {UserId}, attempting refresh", userId);
                if (await RefreshGmailToken(userId))
                {
                    return await GetEmailsAsync(userId, query, maxResults);
                }
                throw;
            }
        }

        public async Task<GmailEmail> GetEmailAsync(int userId, string emailId)
        {
            try
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
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                _logger.LogWarning("Token expired for user {UserId}, attempting refresh", userId);
                if (await RefreshGmailToken(userId))
                {
                    return await GetEmailAsync(userId, emailId);
                }
                throw;
            }
        }

        public async Task<string> SendEmailAsync(int userId, GmailEmail email)
        {
            try
            {
                var service = await GetGmailServiceAsync(userId);
                
                var mimeMessage = new MimeMessage();
                
                try
                {
                    var fromAddress = ParseEmailAddress(email.From);
                    mimeMessage.From.Add(fromAddress);
                }
                catch (FormatException ex)
                {
                    _logger.LogError(ex, "Invalid from address: {From}", email.From);
                    throw new ArgumentException($"Invalid from address: {email.From}", ex);
                }

                var toAddresses = ParseEmailAddressList(email.To);
                if (!toAddresses.Any())
                {
                    throw new ArgumentException("No valid recipient addresses found");
                }
                mimeMessage.To.AddRange(toAddresses);
                
                if (!string.IsNullOrEmpty(email.Cc))
                {
                    var ccAddresses = ParseEmailAddressList(email.Cc);
                    mimeMessage.Cc.AddRange(ccAddresses);
                }
                
                mimeMessage.Subject = email.Subject ?? "";
                mimeMessage.Body = new TextPart("plain") { Text = email.Body ?? "" };

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
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                if (await RefreshGmailToken(userId))
                {
                    return await SendEmailAsync(userId, email);
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email for user {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> SendEmailAsync(int userId, string to, string subject, string body)
        {
            var userEmail = await GetUserEmail(userId) ?? "noreply@aiagent.com";
            
            var email = new GmailEmail
            {
                From = userEmail,
                To = to,
                Subject = subject,
                Body = body
            };

            var result = await SendEmailAsync(userId, email);
            return !string.IsNullOrEmpty(result);
        }

        public async Task<string> DraftEmailAsync(int userId, GmailEmail email)
        {
            try
            {
                var service = await GetGmailServiceAsync(userId);
                
                var mimeMessage = new MimeMessage();
                
                try
                {
                    var fromAddress = ParseEmailAddress(email.From);
                    mimeMessage.From.Add(fromAddress);
                }
                catch (FormatException ex)
                {
                    _logger.LogError(ex, "Invalid from address: {From}", email.From);
                    throw new ArgumentException($"Invalid from address: {email.From}", ex);
                }

                var toAddresses = ParseEmailAddressList(email.To);
                if (!toAddresses.Any())
                {
                    throw new ArgumentException("No valid recipient addresses found");
                }
                mimeMessage.To.AddRange(toAddresses);
                
                if (!string.IsNullOrEmpty(email.Cc))
                {
                    var ccAddresses = ParseEmailAddressList(email.Cc);
                    mimeMessage.Cc.AddRange(ccAddresses);
                }
                
                mimeMessage.Subject = email.Subject ?? "";
                mimeMessage.Body = new TextPart("plain") { Text = email.Body ?? "" };

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
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                if (await RefreshGmailToken(userId))
                {
                    return await DraftEmailAsync(userId, email);
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create draft email for user {UserId}", userId);
                throw;
            }
        }

        public async Task<string> DraftReplyAsync(int userId, string emailId, string kind)
        {
            try
            {
                var originalEmail = await GetEmailAsync(userId, emailId);
                
                string fromEmail = ExtractEmailAddress(originalEmail.From);
                string toEmail = ExtractEmailAddress(originalEmail.To);
                
                if (string.IsNullOrEmpty(fromEmail) || string.IsNullOrEmpty(toEmail))
                {
                    throw new ArgumentException("Could not extract valid email addresses for reply");
                }

                var replyText = kind.ToLower() switch
                {
                    "acknowledge" => $"Hi,\n\nThank you for your email. I've received it and will get back to you soon.\n\nBest regards",
                    "meeting" => $"Hi,\n\nThanks for reaching out. I'd be happy to schedule a meeting. Please let me know your availability.\n\nBest regards",
                    "task" => $"Hi,\n\nI've added this to my task list and will follow up accordingly.\n\nBest regards",
                    _ => $"Hi,\n\nThank you for your email.\n\nBest regards"
                };

                var replyEmail = new GmailEmail
                {
                    From = toEmail,
                    To = fromEmail,
                    Subject = $"Re: {originalEmail.Subject}",
                    Body = replyText
                };

                return await DraftEmailAsync(userId, replyEmail);
            }
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                if (await RefreshGmailToken(userId))
                {
                    return await DraftReplyAsync(userId, emailId, kind);
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to draft reply for email {EmailId}", emailId);
                throw;
            }
        }
        #endregion

        #region Email Management
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
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                if (await RefreshGmailToken(userId))
                {
                    return await MarkAsReadAsync(userId, emailId);
                }
                throw;
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
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                if (await RefreshGmailToken(userId))
                {
                    return await MarkAsUnreadAsync(userId, emailId);
                }
                throw;
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
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                if (await RefreshGmailToken(userId))
                {
                    return await ArchiveEmailAsync(userId, emailId);
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to archive email {EmailId}", emailId);
                return false;
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
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                if (await RefreshGmailToken(userId))
                {
                    return await DeleteEmailAsync(userId, emailId);
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete email {EmailId}", emailId);
                return false;
            }
        }
        #endregion

        #region Labels and Organization
        public async Task<List<GmailLabel>> GetLabelsAsync(int userId)
        {
            try
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
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                if (await RefreshGmailToken(userId))
                {
                    return await GetLabelsAsync(userId);
                }
                throw;
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
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                if (await RefreshGmailToken(userId))
                {
                    return await MoveToLabelAsync(userId, emailId, labelName);
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move email {EmailId} to label {LabelName}", emailId, labelName);
                return false;
            }
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
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                if (await RefreshGmailToken(userId))
                {
                    return await AddLabelAsync(userId, emailId, labelId);
                }
                throw;
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
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                if (await RefreshGmailToken(userId))
                {
                    return await RemoveLabelAsync(userId, emailId, labelId);
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove label from email {EmailId}", emailId);
                return false;
            }
        }
        #endregion

        #region Search and Insights
        public async Task<List<GmailEmail>> SearchEmailsAsync(int userId, string searchQuery, int maxResults = 20)
        {
            return await GetEmailsAsync(userId, searchQuery, maxResults);
        }

        public async Task<List<(string subject, string from, bool urgent, string id)>> GetInsightsAsync(int userId, DateTime sinceUtc)
        {
            try
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
            catch (Google.GoogleApiException ex) when (ex.Error.Code == 401)
            {
                if (await RefreshGmailToken(userId))
                {
                    return await GetInsightsAsync(userId, sinceUtc);
                }
                throw;
            }
        }
        #endregion

        private async Task<IWhatsAppService> GetWhatsAppServiceAsync()
        {
            var scope = _serviceProvider.CreateScope();
            return await Task.FromResult(scope.ServiceProvider.GetRequiredService<IWhatsAppService>());
        }
        
        #region Automation and Processing
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
                    var analysis = await _nlpService.ParseAsync($"{email.Subject} {email.Body}", "UTC");
                    
                    if (analysis.Confidence > 0.6)
                    {
                        using var orchestratorScope = _serviceProvider.CreateScope();
                        var orchestrator = orchestratorScope.ServiceProvider.GetRequiredService<ICommandOrchestrator>();
                        var response = await orchestrator.HandleAsync(userId, $"{email.Subject} - {email.Body}", "Gmail");
                        
                        if (email.IsImportant || analysis.Confidence > 0.8)
                        {
                            // Get WhatsApp service using proper scoping
                            var whatsAppService = await GetWhatsAppServiceAsync();
                            var result = await whatsAppService.SendMessageAsync(
                                user.PhoneNumber, 
                                $"📧 Important email: {email.Subject} from {email.From}"
                            );
                            
                            if (result.Success)
                            {
                                _logger.LogInformation("Sent WhatsApp alert for important email to user {UserId}", userId);
                            }
                        }
                    }

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

        // NEW: Implement the missing method for ProactiveNotificationService
        public async Task<List<EmailInfo>> GetUnreadEmailsAsync(int userId, int maxResults = 5)
        {
            try
            {
                var unreadEmails = await GetEmailsAsync(userId, "is:unread", maxResults);
                
                return unreadEmails.Select(email => new EmailInfo
                {
                    Id = email.Id,
                    From = email.From ?? "",
                    Subject = email.Subject ?? "",
                    IsImportant = email.IsImportant,
                    ReceivedAt = email.Date
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get unread emails for user {UserId}", userId);
                return new List<EmailInfo>();
            }
        }
        #endregion

        #region Helper Methods
        private async Task<string?> GetUserEmail(int userId)
        {
            var user = await _db.Users.FindAsync(userId);
            return user?.Email;
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
        #endregion
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