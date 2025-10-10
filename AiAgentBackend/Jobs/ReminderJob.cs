using AiAgentBackend.Data;
using AiAgentBackend.Services.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Hangfire;

namespace AiAgentBackend.Jobs
{
public class SmartReminderService
{
    // Simulate fetching tasks/events for user
    private Task<List<UserItem>> GetUpcomingItemsAsync(int userId)
    {
        // TODO: Replace with DB query (Tasks + Events)
        var items = new List<UserItem>
        {
            new UserItem { Title = "Team Meeting", DueUtc = DateTime.UtcNow.AddMinutes(45), Type = "event" },
            new UserItem { Title = "Submit Report", DueUtc = DateTime.UtcNow.AddHours(2), Type = "task" }
        };
        return Task.FromResult(items);
    }

    private DateTime CalculateOptimalReminderTime(UserItem item, int userId)
    {
        // Example: send 15 minutes before deadline
        return item.DueUtc.AddMinutes(-15);
    }

    private Task ScheduleAdaptiveReminderAsync(int userId, UserItem item, DateTime reminderTime)
    {
        // TODO: Hook into Hangfire to schedule actual WhatsApp reminder
        Console.WriteLine($"[DEBUG] Scheduling reminder for {item.Title} at {reminderTime} (user {userId})");
        return Task.CompletedTask;
    }

    public async Task ScheduleSmartRemindersAsync(int userId)
    {
        var upcomingItems = await GetUpcomingItemsAsync(userId);

        foreach (var item in upcomingItems)
        {
            var optimalTime = CalculateOptimalReminderTime(item, userId);
            await ScheduleAdaptiveReminderAsync(userId, item, optimalTime);
        }
    }
}

public class UserItem
{
    public string Title { get; set; } = "";
    public DateTime DueUtc { get; set; }
    public string Type { get; set; } = ""; // task / event
}  

    public class ReminderJob
    {
        private readonly ApplicationDbContext _db;
        private readonly IHttpWhatsAppService _wa;
        private readonly ILogger<ReminderJob> _logger;

        public ReminderJob(ApplicationDbContext db, IHttpWhatsAppService wa, ILogger<ReminderJob> logger)
        {
            _wa = wa;
            _db = db;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 60, 120 })]
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
                        await _wa.SendReminderAsync( // Use _wa instead of undefined wa
                            task.UserId,
                            "task",
                            task.Title,
                            task.DueUtc!.Value,
                            task.Description ?? ""
                        );
                        _logger.LogInformation("Sent reminder for task {TaskId} to user {UserId}", task.Id, task.UserId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send reminder for task {TaskId}", task.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ReminderJob");
            }
        }

        private async Task ProcessTaskReminders()
        {
            // Update any method that uses 'wa' to use '_wa'
            var tasks = await _db.Tasks.ToListAsync();
            foreach (var task in tasks)
            {
                if (task.DueUtc.HasValue)
                {
                    await _wa.SendMessageAsync(task.UserId, $"Reminder: {task.Title} is due!");
                }
            }
        }

        private async Task ProcessEventReminders(DateTime now)
        {
            var upcomingEvents = await _db.Events
                .Include(e => e.User)
                .Where(e => e.StartUtc <= now.AddMinutes(30) &&
                       e.StartUtc >= now &&
                       e.Status == "Scheduled" &&
                       e.User != null &&
                       !string.IsNullOrEmpty(e.User.PhoneNumber))
                .ToListAsync();

            foreach (var evt in upcomingEvents)
            {
                try
                {
                    var success = await _wa.SendReminderAsync(
                        evt.UserId, 
                        "event", 
                        evt.Title, 
                        evt.StartUtc.DateTime, 
                        $"{evt.Location} | {evt.Description}"
                    );
                    
                    if (success)
                    {
                        _logger.LogInformation("Sent reminder for event {EventId} to user {UserId}", 
                            evt.Id, evt.UserId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending reminder for event {EventId}", evt.Id);
                }
                
                await Task.Delay(100);
            }
        }

        private Task ProcessEmailReminders(DateTime now)
        {
            _logger.LogInformation("Email reminder processing would be implemented here");
            return Task.CompletedTask;
        }
    }
}