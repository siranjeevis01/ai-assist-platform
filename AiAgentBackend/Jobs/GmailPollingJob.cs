// Jobs/GmailPollingJob.cs
using AiAgentBackend.Data;
using AiAgentBackend.Services.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiAgentBackend.Jobs
{
    public class GmailPollingJob
    {
        private readonly ApplicationDbContext _db;
        private readonly IEnhancedGmailService _gmail;
        private readonly IHttpWhatsAppService _wa; 
        private readonly ILogger<GmailPollingJob> _logger;

        public GmailPollingJob(
            ApplicationDbContext db, 
            IEnhancedGmailService gmail, 
            ILogger<GmailPollingJob> logger,
            IHttpWhatsAppService wa)
        {
            _db = db; 
            _gmail = gmail; 
            _wa = wa; 
            _logger = logger;
        }

        public async Task RunAsync()
        {
            try
            {
                var usersWithGoogle = await _db.ProviderTokens
                    .Where(t => t.Provider == "Google")
                    .Select(t => t.UserId)
                    .Distinct()
                    .ToListAsync();

                _logger.LogInformation("Processing Gmail for {Count} users", usersWithGoogle.Count);

                foreach (var userId in usersWithGoogle)
                {
                    try
                    {
                        var user = await _db.Users.FindAsync(userId);
                        if (user == null || string.IsNullOrEmpty(user.PhoneNumber))
                        {
                            _logger.LogWarning("User {UserId} has no phone number, skipping Gmail processing", userId);
                            continue;
                        }

                        var insights = await _gmail.GetInsightsAsync(userId, DateTime.UtcNow.AddHours(-6));
                        
                        _logger.LogInformation("Found {Count} email insights for user {UserId}", insights.Count, userId);
                        
                        foreach (var ins in insights)
                        {
                            var (subject, from, urgent, emailId) = ins;
                            if (urgent)
                            {
                                var task = new AiAgentBackend.Models.TaskItem
                                {
                                    UserId = userId,
                                    Title = $"Follow up: {subject}",
                                    Description = $"From: {from}",
                                    Status = "To Do",
                                    DueUtc = DateTime.UtcNow.AddDays(1),
                                    CreatedAt = DateTime.UtcNow
                                };
                                
                                _db.Tasks.Add(task);
                                await _db.SaveChangesAsync();

                                var success = await _wa.SendMessageAsync( 
                                    userId, 
                                    $"Urgent email detected: '{subject}' from {from}. I created a task for you."
                                );
                                
                                if (success)
                                {
                                    _logger.LogInformation("Notified user {UserId} about urgent email", userId);
                                }

                                // Create a draft reply
                                await _gmail.DraftReplyAsync(userId, emailId, "acknowledge");
                            }
                        }

                        // Process all incoming emails
                        await _gmail.ProcessIncomingEmails(userId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "GmailPollingJob failed for user {UserId}", userId);
                    }
                    
                    // Small delay to avoid rate limiting
                    await Task.Delay(1000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GmailPollingJob");
            }
        }
    }
}