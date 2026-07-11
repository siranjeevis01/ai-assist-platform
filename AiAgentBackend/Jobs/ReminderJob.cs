using AiAgentBackend.Data;
using AiAgentBackend.Services.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Hangfire;
using AiAgentBackend.Services.Messaging;

namespace AiAgentBackend.Jobs
{
    public class SmartReminderService
    {
        private readonly ApplicationDbContext _db;
        private readonly IMessagingService _messagingService;
        private readonly ILogger<SmartReminderService> _logger;

        public SmartReminderService(
            ApplicationDbContext db,
            IMessagingService messagingService,
            ILogger<SmartReminderService> logger)
        {
            _db = db;
            _messagingService = messagingService;
            _logger = logger;
        }

        private async Task<List<UserItem>> GetUpcomingItemsAsync(int userId)
        {
            var now = DateTime.UtcNow;
            var threshold = now.AddHours(24);

            var tasks = await _db.Tasks
                .Where(t => t.UserId == userId &&
                       t.DueUtc.HasValue &&
                       t.DueUtc > now &&
                       t.DueUtc <= threshold &&
                       t.Status != "Done")
                .Select(t => new UserItem
                {
                    Id = t.Id,
                    Title = t.Title,
                    DueUtc = t.DueUtc!.Value,
                    Type = "task"
                })
                .ToListAsync();

            var events = await _db.Events
                .Where(e => e.UserId == userId &&
                       e.StartUtc > now &&
                       e.StartUtc <= threshold &&
                       e.Status == "Scheduled")
                .Select(e => new UserItem
                {
                    Id = e.Id,
                    Title = e.Title,
                    DueUtc = e.StartUtc.UtcDateTime,
                    Type = "event"
                })
                .ToListAsync();

            return tasks.Concat(events).OrderBy(i => i.DueUtc).ToList();
        }

        private DateTime CalculateOptimalReminderTime(UserItem item)
        {
            var timeUntilDue = item.DueUtc - DateTime.UtcNow;

            if (timeUntilDue.TotalMinutes <= 15)
                return DateTime.UtcNow.AddMinutes(1);

            if (timeUntilDue.TotalHours <= 1)
                return item.DueUtc.AddMinutes(-10);

            if (timeUntilDue.TotalHours <= 4)
                return item.DueUtc.AddMinutes(-15);

            if (timeUntilDue.TotalHours <= 24)
                return item.DueUtc.AddMinutes(-30);

            return item.DueUtc.AddHours(-1);
        }

        private Task ScheduleAdaptiveReminderAsync(int userId, UserItem item, DateTime reminderTime)
        {
            var delay = reminderTime - DateTime.UtcNow;
            if (delay <= TimeSpan.Zero)
                delay = TimeSpan.FromSeconds(5);

            var emoji = item.Type == "event" ? "📅" : "✅";
            var timeUntil = item.DueUtc - DateTime.UtcNow;
            var timeText = timeUntil.TotalHours < 1
                ? $"{timeUntil.Minutes} minutes"
                : $"{timeUntil.TotalHours:F1} hours";

            var message = $"{emoji} Reminder: '{item.Title}' is due in {timeText} ({item.DueUtc:MMM dd, HH:mm})";

            BackgroundJob.Schedule(
                () => _messagingService.SendMessageAsync(userId, message),
                delay
            );

            _logger.LogInformation("Scheduled {Type} reminder for user {UserId}: '{Title}' at {Time}",
                item.Type, userId, item.Title, reminderTime);
            return Task.CompletedTask;
        }

        public async Task ScheduleSmartRemindersAsync(int userId)
        {
            var upcomingItems = await GetUpcomingItemsAsync(userId);

            foreach (var item in upcomingItems)
            {
                var optimalTime = CalculateOptimalReminderTime(item);
                await ScheduleAdaptiveReminderAsync(userId, item, optimalTime);
            }
        }

        public async Task ScheduleAllUsersSmartRemindersAsync()
        {
            var userIds = await _db.Tasks
                .Where(t => t.DueUtc.HasValue && t.Status != "Done")
                .Select(t => t.UserId)
                .Distinct()
                .ToListAsync();

            foreach (var userId in userIds)
            {
                try
                {
                    await ScheduleSmartRemindersAsync(userId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to schedule smart reminders for user {UserId}", userId);
                }
            }
        }
    }

    public class UserItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public DateTime DueUtc { get; set; }
        public string Type { get; set; } = "";
    }

    public class SmartReminderHangfireJob
    {
        private readonly SmartReminderService _smartReminder;

        public SmartReminderHangfireJob(SmartReminderService smartReminder)
        {
            _smartReminder = smartReminder;
        }

        [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 120 })]
        public async Task RunAsync()
        {
            await _smartReminder.ScheduleAllUsersSmartRemindersAsync();
        }
    }

    public class ReminderJob
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<ReminderJob> _logger;
        private readonly IMessagingService _messagingService;

        public ReminderJob(ApplicationDbContext db, ILogger<ReminderJob> logger, IMessagingService messagingService)
        {
            _db = db;
            _logger = logger;
            _messagingService = messagingService;
        }

        public async Task RunAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                var dueTasks = await _db.Tasks
                    .Include(t => t.User)
                    .Where(t => t.DueUtc.HasValue &&
                           t.DueUtc <= now.AddMinutes(30) &&
                           t.DueUtc > now &&
                           t.Status != "Done" &&
                           t.User != null)
                    .ToListAsync();

                foreach (var task in dueTasks)
                {
                    try
                    {
                        var timeUntil = task.DueUtc!.Value - now;
                        var timeText = timeUntil.TotalMinutes < 1
                            ? "now"
                            : timeUntil.TotalHours < 1
                                ? $"{timeUntil.Minutes} minutes"
                                : $"{timeUntil.TotalHours:F1} hours";

                        var result = await _messagingService.SendMessageAsync(
                            task.UserId,
                            $"Reminder: '{task.Title}' is due in {timeText} ({task.DueUtc!.Value:MMM dd, HH:mm})"
                        );

                        if (result.Success)
                        {
                            task.LastReminderSentAt = DateTime.UtcNow;
                            _db.Tasks.Update(task);
                            await _db.SaveChangesAsync();
                            _logger.LogInformation("Sent reminder for task {TaskId} to user {UserId}", task.Id, task.UserId);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to send reminder for task {TaskId}: {Error}", task.Id, result.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send reminder for task {TaskId}", task.Id);
                    }
                }

                await ProcessEventReminders(now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ReminderJob");
            }
        }

        private async Task ProcessEventReminders(DateTime now)
        {
            var upcomingEvents = await _db.Events
                .Include(e => e.User)
                .Where(e => e.StartUtc <= now.AddMinutes(30) &&
                       e.StartUtc >= now &&
                       e.Status == "Scheduled" &&
                       e.User != null)
                .ToListAsync();

            foreach (var evt in upcomingEvents)
            {
                try
                {
                    var timeUntil = evt.StartUtc - now;
                    var timeText = timeUntil.TotalMinutes < 1
                        ? "starting now"
                        : timeUntil.TotalHours < 1
                            ? $"in {timeUntil.Minutes} minutes"
                            : $"in {timeUntil.TotalHours:F1} hours";

                    var message = $"📅 Event Reminder: '{evt.Title}' {timeText} ({evt.StartUtc:MMM dd, HH:mm} UTC)";

                    if (!string.IsNullOrEmpty(evt.Location))
                        message += $"\n📍 Location: {evt.Location}";

                    if (!string.IsNullOrEmpty(evt.Description))
                        message += $"\n📝 Description: {evt.Description}";

                    var result = await _messagingService.SendMessageAsync(evt.UserId, message);

                    if (result.Success)
                    {
                        _logger.LogInformation("Sent reminder for event {EventId} to user {UserId}", evt.Id, evt.UserId);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to send reminder for event {EventId}: {Error}", evt.Id, result.Error);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending reminder for event {EventId}", evt.Id);
                }

                await Task.Delay(100);
            }
        }
    }
}