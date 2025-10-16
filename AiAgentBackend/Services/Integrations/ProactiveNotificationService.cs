using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.Services.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiAgentBackend.Services.Integrations
{
    public interface IProactiveNotificationService
    {
        Task CheckAndSendRemindersAsync();
        Task CheckAndSendEventRemindersAsync();
        Task CheckAndSendEmailAlertsAsync();
        Task CheckAndSendTaskDeadlineWarningsAsync();
    }

    public class ProactiveNotificationService : IProactiveNotificationService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ProactiveNotificationService> _logger;

        public ProactiveNotificationService(
            IServiceProvider serviceProvider,
            ILogger<ProactiveNotificationService> logger) 
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        // FIXED: Added proper async implementation
        private async Task<(ApplicationDbContext db, IGmailService gmail, IWhatsAppService whatsApp)> GetServicesAsync()
        {
            var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var gmail = scope.ServiceProvider.GetRequiredService<IGmailService>();
            var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
            
            // Ensure database connection is ready
            await db.Database.CanConnectAsync();
            
            return (db, gmail, whatsApp);
        }

        public async Task CheckAndSendRemindersAsync()
        {
            try
            {
                var (db, gmail, whatsApp) = await GetServicesAsync();
                
                var now = DateTime.UtcNow;
                var reminderThreshold = now.AddMinutes(30);

                var dueTasks = await db.Tasks
                    .Include(t => t.User)
                    .Where(t => t.DueUtc.HasValue && 
                           t.DueUtc <= reminderThreshold &&
                           t.DueUtc > now &&
                           t.Status != "Done" &&
                           (t.LastReminderSentAt == null || t.LastReminderSentAt < t.DueUtc.Value.AddMinutes(-15)) &&
                           t.User != null &&
                           !string.IsNullOrEmpty(t.User.PhoneNumber))
                    .ToListAsync();

                _logger.LogInformation("Found {Count} tasks due for reminders", dueTasks.Count);

                foreach (var task in dueTasks)
                {
                    try
                    {
                        var message = $"🔔 Reminder: '{task.Title}' is due at {task.DueUtc!.Value:MMM dd, yyyy HH:mm} UTC";
                        if (!string.IsNullOrEmpty(task.Description))
                            message += $"\nDetails: {task.Description}";

                        var result = await whatsApp.SendMessageAsync(task.User.PhoneNumber!, message);
                        
                        if (result.Success)
                        {
                            _logger.LogInformation("✅ Sent reminder for task {TaskId} to user {UserId}", task.Id, task.UserId);
                            
                            // Mark as notified to avoid duplicate reminders
                            task.LastReminderSentAt = DateTime.UtcNow;
                            db.Tasks.Update(task);
                            await db.SaveChangesAsync();
                        }
                        else
                        {
                            _logger.LogWarning("❌ Failed to send reminder for task {TaskId}: {Error}", task.Id, result.Error);
                        }
                        
                        await Task.Delay(500); // Rate limiting
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Failed to send reminder for task {TaskId}", task.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in CheckAndSendRemindersAsync");
            }
        }

        public async Task CheckAndSendEventRemindersAsync()
        {
            try
            {
                var (db, gmail, whatsApp) = await GetServicesAsync();
                
                var now = DateTime.UtcNow;
                var reminderThreshold = now.AddMinutes(30);

                var upcomingEvents = await db.Events
                    .Include(e => e.User)
                    .Where(e => e.StartUtc <= reminderThreshold &&
                           e.StartUtc > now &&
                           e.Status == "Scheduled" &&
                           e.User != null &&
                           !string.IsNullOrEmpty(e.User.PhoneNumber))
                    .ToListAsync();

                _logger.LogInformation("Found {Count} events due for reminders", upcomingEvents.Count);

                foreach (var evt in upcomingEvents)
                {
                    try
                    {
                        var message = $"📅 Event Reminder: '{evt.Title}' at {evt.StartUtc:MMM dd, yyyy HH:mm} UTC";
                        
                        if (!string.IsNullOrEmpty(evt.Location))
                            message += $"\n📍 Location: {evt.Location}";
                            
                        if (!string.IsNullOrEmpty(evt.Description))
                            message += $"\n📝 Description: {evt.Description}";

                        if (evt.User?.PhoneNumber != null)
                        {
                            var result = await whatsApp.SendMessageAsync(evt.User.PhoneNumber, message);
                            
                            if (result.Success)
                            {
                                _logger.LogInformation("✅ Sent reminder for event {EventId} to user {UserId}", evt.Id, evt.UserId);
                            }
                            else
                            {
                                _logger.LogWarning("❌ Failed to send reminder for event {EventId}: {Error}", evt.Id, result.Error);
                            }
                        }
                        
                        await Task.Delay(500); // Rate limiting
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error sending reminder for event {EventId}", evt.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in CheckAndSendEventRemindersAsync");
            }
        }

        public async Task CheckAndSendEmailAlertsAsync()
        {
            try
            {
                var (db, gmail, whatsApp) = await GetServicesAsync();
                
                var usersWithGoogle = await db.ProviderTokens
                    .Include(t => t.User)
                    .Where(t => t.Provider == "Google" && t.User != null && !string.IsNullOrEmpty(t.User.PhoneNumber))
                    .Select(t => t.User!)
                    .Distinct()
                    .ToListAsync();

                _logger.LogInformation("Processing email alerts for {Count} users", usersWithGoogle.Count);

                foreach (var user in usersWithGoogle)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(user.PhoneNumber))
                            continue;

                        var unreadEmails = await gmail.GetUnreadEmailsAsync(user.Id, 5);
                        
                        foreach (var email in unreadEmails.Take(3))
                        {
                            var priority = email.IsImportant ? "🚨 URGENT" : "📧 New";
                            var message = $"{priority} Email:\nFrom: {email.From}\nSubject: {email.Subject}";
                            
                            var result = await whatsApp.SendMessageAsync(user.PhoneNumber, message);
                            
                            if (result.Success)
                            {
                                _logger.LogInformation("✅ Sent email alert to user {UserId}", user.Id);
                                await Task.Delay(1000); // Rate limiting between emails
                            }
                            else
                            {
                                _logger.LogWarning("❌ Failed to send email alert to user {UserId}: {Error}", user.Id, result.Error);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Failed to check email alerts for user {UserId}", user.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in CheckAndSendEmailAlertsAsync");
            }
        }

        public async Task CheckAndSendTaskDeadlineWarningsAsync()
        {
            try
            {
                var (db, gmail, whatsApp) = await GetServicesAsync();
                
                var now = DateTime.UtcNow;
                var warningThreshold = now.AddHours(24);

                var dueSoonTasks = await db.Tasks
                    .Include(t => t.User)
                    .Where(t => t.DueUtc.HasValue && 
                           t.DueUtc <= warningThreshold &&
                           t.DueUtc > now &&
                           t.Status != "Done" &&
                           t.User != null &&
                           !string.IsNullOrEmpty(t.User.PhoneNumber))
                    .ToListAsync();

                _logger.LogInformation("Found {Count} tasks with approaching deadlines", dueSoonTasks.Count);

                foreach (var task in dueSoonTasks)
                {
                    try
                    {
                        var timeUntilDue = task.DueUtc!.Value - now;
                        var hoursUntilDue = Math.Round(timeUntilDue.TotalHours, 1);
                        
                        var message = $"⏰ Task deadline approaching: '{task.Title}' is due in {hoursUntilDue} hours ({task.DueUtc!.Value:MMM dd, yyyy HH:mm} UTC)";
                        
                        if (task.User?.PhoneNumber != null)
                        {
                            var result = await whatsApp.SendMessageAsync(task.User.PhoneNumber, message);
                            
                            if (result.Success)
                            {
                                _logger.LogInformation("✅ Sent deadline warning for task {TaskId} to user {UserId}", task.Id, task.UserId);
                            }
                            else
                            {
                                _logger.LogWarning("❌ Failed to send deadline warning for task {TaskId}: {Error}", task.Id, result.Error);
                            }
                        }
                        
                        await Task.Delay(500); // Rate limiting
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Failed to send deadline warning for task {TaskId}", task.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in CheckAndSendTaskDeadlineWarningsAsync");
            }
        }
    }
}