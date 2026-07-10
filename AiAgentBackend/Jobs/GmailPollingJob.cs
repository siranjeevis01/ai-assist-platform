// Jobs/GmailPollingJob.cs
using AiAgentBackend.Data;
using AiAgentBackend.Services.Integrations;
using AiAgentBackend.Services.Messaging; 
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiAgentBackend.Jobs
{
    public class GmailPollingJob
    {
        private readonly ILogger<GmailPollingJob> _logger;
        private readonly IServiceProvider _serviceProvider;

        public GmailPollingJob(
            ILogger<GmailPollingJob> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task RunAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var gmail = scope.ServiceProvider.GetRequiredService<IGmailService>();

                var usersWithGoogle = await db.ProviderTokens
                    .Where(t => t.Provider == "Google")
                    .Select(t => t.UserId)
                    .Distinct()
                    .ToListAsync();

                _logger.LogInformation("Processing Gmail for {Count} users", usersWithGoogle.Count);

                foreach (var userId in usersWithGoogle)
                {
                    try
                    {
                        var user = await db.Users.FindAsync(userId);
                        if (user == null || string.IsNullOrEmpty(user.PhoneNumber))
                        {
                            _logger.LogWarning("User {UserId} has no phone number, skipping Gmail processing", userId);
                            continue;
                        }

                        var insights = await gmail.GetInsightsAsync(userId, DateTime.UtcNow.AddHours(-6));
                        
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
                                
                                db.Tasks.Add(task);
                                await db.SaveChangesAsync();

                                // Get messaging service from the same scope
                                var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>(); // Changed this line
                                
                                var result = await messagingService.SendMessageAsync( // Changed this line
                                    userId, 
                                    $"Urgent email detected: '{subject}' from {from}. I created a task for you."
                                );
                                
                                if (result.Success)
                                {
                                    _logger.LogInformation("Notified user {UserId} about urgent email", userId);
                                }

                                // Create a draft reply
                                await gmail.DraftReplyAsync(userId, emailId, "acknowledge");
                            }
                        }

                        // Process all incoming emails
                        await gmail.ProcessIncomingEmails(userId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "GmailPollingJob failed for user {UserId}", userId);
                    }
                    
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