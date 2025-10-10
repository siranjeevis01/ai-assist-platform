// Services/Integrations/ProactiveNotificationService.cs
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using Microsoft.EntityFrameworkCore;

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
        private readonly ApplicationDbContext _db;
        private readonly IEnhancedGmailService _gmailService;
        private readonly ILogger<ProactiveNotificationService> _logger;
        private readonly IHttpWhatsAppService _whatsAppService; // Add this field

        public ProactiveNotificationService(
            ApplicationDbContext db, 
            IEnhancedGmailService gmailService,
            ILogger<ProactiveNotificationService> logger,
            IHttpWhatsAppService whatsAppService) // Add this parameter
        {
            _db = db;
            _whatsAppService = whatsAppService; // Initialize the field
            _gmailService = gmailService;
            _logger = logger;
        }

        public async Task CheckAndSendRemindersAsync()
        {
            var now = DateTime.UtcNow;
            var reminderThreshold = now.AddMinutes(30);

            // Get tasks due soon
            var dueTasks = await _db.Tasks
                .Include(t => t.User)
                .Where(t => t.DueUtc.HasValue && 
                       t.DueUtc <= reminderThreshold &&
                       t.DueUtc > now &&
                       t.Status != "Done" &&
                       t.User != null &&
                       !string.IsNullOrEmpty(t.User.PhoneNumber))
                .ToListAsync();

            foreach (var task in dueTasks)
            {
                await _whatsAppService.SendReminderAsync( // Use _whatsAppService
                    task.UserId,
                    "task",
                    task.Title,
                    task.DueUtc!.Value,
                    task.Description ?? ""
                );
            }
        }

        public async Task CheckAndSendEventRemindersAsync()
        {
            var now = DateTime.UtcNow;
            var reminderThreshold = now.AddMinutes(30);

            // Get events starting soon
            var upcomingEvents = await _db.Events
                .Include(e => e.User)
                .Where(e => e.StartUtc <= reminderThreshold &&
                       e.StartUtc > now &&
                       e.Status == "Scheduled" &&
                       e.User != null &&
                       !string.IsNullOrEmpty(e.User.PhoneNumber))
                .ToListAsync();

            foreach (var evt in upcomingEvents)
            {
                await _whatsAppService.SendReminderAsync( // Use _whatsAppService
                    evt.UserId,
                    "event",
                    evt.Title,
                    evt.StartUtc.DateTime,
                    $"{evt.Location} | {evt.Description}"
                );
            }
        }

        public async Task CheckAndSendEmailAlertsAsync()
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
                    var urgentEmails = await _gmailService.GetEmailsAsync(userId, "is:unread label:important", 5);
                    
                    foreach (var email in urgentEmails.Where(e => e.IsImportant))
                    {
                        await _whatsAppService.SendEmailAlertAsync( 
                            userId,
                            email.Subject,
                            email.From,
                            "high",
                            email.Id
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to check email alerts for user {UserId}", userId);
                }
            }
        }

        public async Task CheckAndSendTaskDeadlineWarningsAsync()
        {
            var now = DateTime.UtcNow;
            var warningThreshold = now.AddHours(24);

            // Get tasks due in the next 24 hours
            var dueSoonTasks = await _db.Tasks
                .Include(t => t.User)
                .Where(t => t.DueUtc.HasValue && 
                       t.DueUtc <= warningThreshold &&
                       t.DueUtc > now &&
                       t.Status != "Done" &&
                       t.User != null &&
                       !string.IsNullOrEmpty(t.User.PhoneNumber))
                .ToListAsync();

            foreach (var task in dueSoonTasks)
            {
                await _whatsAppService.SendMessageAsync( 
                    task.UserId,
                    $"⏰ Task deadline approaching: '{task.Title}' is due at {task.DueUtc!.Value:MMM dd, yyyy HH:mm}"
                );
            }
        }
    }
}