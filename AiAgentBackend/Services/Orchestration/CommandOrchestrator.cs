using Hangfire;
using AiAgentBackend.Hubs;
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.Services.Integrations;
using AiAgentBackend.Services.NLP;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AiAgentBackend.Utils;

namespace AiAgentBackend.Services.Orchestration
{
    public interface ICommandOrchestrator
    {
        Task<string> HandleAsync(int userId, string messageText, string source = "WhatsApp");
        Task<bool> HandleEventConfirmationResponse(int userId, string contextData, string selectedOption);
        Task<bool> HandleTaskConfirmationResponse(int userId, string contextData, string selectedOption);
        Task<bool> HandleEmailActionResponse(int userId, string contextData, string selectedOption);        
    }

    public class CommandRequest
    {
        public int UserId { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Source { get; set; } = "WhatsApp";
        public Dictionary<string, object> Context { get; set; } = new Dictionary<string, object>();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class CommandResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
        public List<string> Actions { get; set; } = new List<string>();
        public Dictionary<string, string> Responses { get; set; } = new Dictionary<string, string>();
    }
    
    public class CommandOrchestrator : ICommandOrchestrator
    {
        private readonly ApplicationDbContext _db;

        protected readonly INlpService _nlp;
        protected readonly ILogger<CommandOrchestrator> _logger;
        protected readonly IHubContext<UpdatesHub> _hub;
        protected readonly IGoogleCalendarService _cal;
        private readonly ITrelloService _trello;
        private readonly IGmailService _gmail;
        protected readonly IHttpWhatsAppService _wa;

        public CommandOrchestrator(
            ApplicationDbContext db,
            INlpService nlp,
            IGoogleCalendarService cal,
            ITrelloService trello,
            IHttpWhatsAppService wa,
            IGmailService gmail,
            IHubContext<UpdatesHub> hub,
            ILogger<CommandOrchestrator> logger)
        {
            _db = db;
            _nlp = nlp;
            _cal = cal;
            _trello = trello;
            _wa = wa;
            _gmail = gmail;
            _hub = hub;
            _logger = logger;
        }

public async Task<string> HandleAsync(int userId, string messageText, string source = "WhatsApp")
{
    try
    {
        // Clean the input text first
        var cleanText = TextCleaner.CleanForNlp(messageText);
        
        if (!TextCleaner.IsValidForAnalysis(cleanText))
        {
            return "Please provide a clear command. I can help with scheduling, tasks, emails, and reminders.";
        }

        var user = await _db.Users
            .Include(u => u.Preference)
            .FirstOrDefaultAsync(u => u.Id == userId);
            
        if (user == null)
            throw new Exception($"User {userId} not found");

        var pref = user.Preference ?? new Preference { UserId = userId };
        var timezone = user.Timezone ?? "UTC";

        // Use cleaned text for NLP
        var parsed = await _nlp.ParseAsync(cleanText, timezone);
        _logger.LogInformation($"Parsed intent: {parsed.Intent}, Confidence: {parsed.Confidence}");

        // Store the message
        var message = new Message
        {
            UserId = userId,
            Channel = source,
            Direction = "Incoming",
            Body = messageText,
            Intent = parsed.Intent,
            EntitiesJson = System.Text.Json.JsonSerializer.Serialize(parsed.Entities),
            CreatedAt = DateTime.UtcNow
        };
        
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        // Handle based on intent
        switch (parsed.Intent)
        {
            case "CreateEvent":
                return await HandleCreateEvent(userId, parsed, pref, source);
                
            case "CreateTask":
                return await HandleCreateTask(userId, parsed, pref, source);
                
            case "UpdateTask":
                return await HandleUpdateTask(userId, parsed, source);
                
            case "CreateReminder":
                return await HandleCreateReminder(userId, parsed, pref, source);
                
            case "CheckCalendar":
                return await HandleCheckCalendar(userId, parsed);
                
            case "CheckTasks":
                return await HandleCheckTasks(userId, parsed);
                
            case "EmailAction":
                return await HandleEmailAction(userId, parsed, source);
                
            default:
                return "Sorry, I didn't understand that command. Try something like: 'Schedule a meeting tomorrow at 3 PM' or 'Create a Trello card for project tasks'.";
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing command");
        try 
        { 
            await _wa.SendMessageAsync(userId, $"Error processing command: {ex.Message}"); 
        } 
        catch { }
        return $"Failed to process command: {ex.Message}";
    }
}

        private async Task<string> HandleCreateEvent(int userId, NlpResult parsed, Preference pref, string source)
        {
            var title = parsed.Entities.GetValueOrDefault("title") ?? "Meeting";
            var location = parsed.Entities.GetValueOrDefault("location", "");
            
            // Parse datetime
            DateTime start;
            if (parsed.Entities.TryGetValue("datetime", out var dtRaw) && DateTime.TryParse(dtRaw, out var dt))
                start = dt.ToUniversalTime();
            else
                start = DateTime.UtcNow.AddHours(1);

            // Parse duration
            var durationMinutes = parsed.Entities.TryGetValue("duration_minutes", out var dstr) && 
                                int.TryParse(dstr, out var d) ? d : pref.DefaultDurationMinutes;
            var end = start.AddMinutes(durationMinutes);

            // Parse attendees
            var attendees = new List<string>();
            if (parsed.Entities.TryGetValue("attendees", out var attendeeStr))
            {
                attendees = attendeeStr.Split(',').Select(a => a.Trim()).ToList();
            }

            var evt = new Event
            {
                UserId = userId,
                Title = title,
                Description = parsed.Entities.GetValueOrDefault("description", "") ?? "",
                StartUtc = new DateTimeOffset(start),
                EndUtc = new DateTimeOffset(end),
                Location = location,
                Status = "Scheduled",
                Source = source,
                AttendeesJson = System.Text.Json.JsonSerializer.Serialize(attendees)
            };

            // Save to DB first
            _db.Events.Add(evt);
            await _db.SaveChangesAsync();

            // Try to create in Google Calendar if connected
            string? externalId = null;
            try
            {
                var googleToken = await _db.ProviderTokens
                    .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Google");
                    
                if (googleToken != null)
                {
                    var createdEvent = await _cal.CreateEventAsync(userId, evt);
                    if (createdEvent != null)
                    {
                        externalId = createdEvent.ExternalId;
                        evt.ExternalId = externalId;
                        _db.Events.Update(evt);
                        await _db.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Google Calendar integration failed");
            }

            // Send confirmation with quick actions
            var localTime = start.ToLocalTime();
            var message = $"Event '{evt.Title}' scheduled for {localTime:yyyy-MM-dd HH:mm}.";
            
            if (!string.IsNullOrEmpty(externalId))
                message += " ✅ Synced with Google Calendar.";
                
            await _wa.SendQuickActionsAsync(userId, message, new[] { "Confirm", "Reschedule", "Cancel" });

            // Notify via SignalR
            await _hub.Clients.User(userId.ToString()).SendAsync("ReceiveUpdate", new
            {
                Type = "EventCreated",
                Event = evt
            });

            return $"Event '{evt.Title}' created successfully.";
        }

        private async Task<string> HandleCreateTask(int userId, NlpResult parsed, Preference pref, string source)
        {
            var title = parsed.Entities.GetValueOrDefault("title", "New Task") ?? "New Task";
            
            // Parse due date
            DateTime? due = null;
            if (parsed.Entities.TryGetValue("due", out var dueRaw) && DateTime.TryParse(dueRaw, out var du))
                due = du.ToUniversalTime();
            else if (parsed.Entities.ContainsKey("due"))
                due = DateTime.UtcNow.AddDays(1); // Default to tomorrow

            var description = parsed.Entities.GetValueOrDefault("description", "");
            
            // Parse labels
            var labels = new List<string>();
            if (parsed.Entities.TryGetValue("labels", out var labelsStr))
            {
                labels = labelsStr.Split(',').Select(l => l.Trim()).ToList();
            }

            var task = new TaskItem
            {
                UserId = userId,
                Title = title,
                Description = description,
                DueUtc = due,
                Status = "To Do",
                LabelsJson = System.Text.Json.JsonSerializer.Serialize(labels),
                CreatedAt = DateTime.UtcNow
            };

            _db.Tasks.Add(task);
            await _db.SaveChangesAsync();

            // Try to create in Trello if connected
            string? externalId = null;
            try
            {
                var trelloToken = await _db.ProviderTokens
                    .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Trello");
                    
                if (trelloToken != null)
                {
                    var createdCard = await _trello.CreateCardAsync(userId, task);
                    if (createdCard != null && !string.IsNullOrEmpty(createdCard.ExternalId))
                    {
                        externalId = createdCard.ExternalId;
                        task.ExternalId = externalId;
                        _db.Tasks.Update(task);
                        await _db.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Trello integration failed");
            }

            // Send confirmation
            var dueMessage = due.HasValue ? $"Due {due.Value.ToLocalTime():yyyy-MM-dd}" : "No due date";
            var message = $"Task '{task.Title}' created. {dueMessage}.";
            
            if (!string.IsNullOrEmpty(externalId))
                message += " ✅ Synced with Trello.";
                
            await _wa.SendQuickActionsAsync(userId, message, new[] { "Complete", "Snooze", "Edit" });

            await _hub.Clients.User(userId.ToString()).SendAsync("ReceiveUpdate", new
            {
                Type = "TaskCreated",
                Task = task
            });

            return $"Task '{task.Title}' created successfully.";
        }

        private async Task<string> HandleUpdateTask(int userId, NlpResult parsed, string source)
        {
            var status = parsed.Entities.GetValueOrDefault("status", "In Progress") ?? "In Progress";
            
            // Try to find the task by title or use the most recent
            TaskItem? taskToUpdate = null;
            
            if (parsed.Entities.TryGetValue("title", out var taskTitle))
            {
                taskToUpdate = await _db.Tasks
                    .Where(t => t.UserId == userId && t.Title.Contains(taskTitle))
                    .OrderByDescending(t => t.Id)
                    .FirstOrDefaultAsync();
            }
            
            if (taskToUpdate == null)
            {
                taskToUpdate = await _db.Tasks
                    .Where(t => t.UserId == userId)
                    .OrderByDescending(t => t.Id)
                    .FirstOrDefaultAsync();
            }

            if (taskToUpdate == null)
                return "No tasks found to update.";

            var oldStatus = taskToUpdate.Status;
            taskToUpdate.Status = status;
            
            if (status == "Done" || status == "Completed")
                taskToUpdate.CompletedAt = DateTime.UtcNow;

            // Update in Trello if connected
            try
            {
                var trelloToken = await _db.ProviderTokens
                    .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Trello");
                    
                if (trelloToken != null && !string.IsNullOrEmpty(taskToUpdate.ExternalId))
                {
                    await _trello.UpdateCardAsync(userId, taskToUpdate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Trello update failed");
            }

            await _db.SaveChangesAsync();

            await _wa.SendMessageAsync(userId, $"Task '{taskToUpdate.Title}' updated from {oldStatus} to {status}.");
            
            await _hub.Clients.User(userId.ToString()).SendAsync("ReceiveUpdate", new
            {
                Type = "TaskUpdated",
                Task = taskToUpdate
            });

            return $"Task '{taskToUpdate.Title}' updated to {status}.";
        }

        private async Task<string> HandleCreateReminder(int userId, NlpResult parsed, Preference pref, string source)
        {
            // Implementation for reminders
            var title = parsed.Entities.GetValueOrDefault("title", "Reminder") ?? "Reminder";
            
            DateTime remindAt;
            if (parsed.Entities.TryGetValue("datetime", out var dtRaw) && DateTime.TryParse(dtRaw, out var dt))
                remindAt = dt.ToUniversalTime();
            else
                remindAt = DateTime.UtcNow.AddHours(1);

            // Create a task for the reminder
            var task = new TaskItem
            {
                UserId = userId,
                Title = title,
                Description = parsed.Entities.GetValueOrDefault("description", "") ?? "",
                DueUtc = remindAt,
                Status = "To Do",
                CreatedAt = DateTime.UtcNow
            };

            _db.Tasks.Add(task);
            await _db.SaveChangesAsync();

            // Schedule a background job to send the reminder
            BackgroundJob.Schedule(() => 
                SendReminderNotification(userId, task.Id), 
                remindAt - DateTime.UtcNow
            );

            var localTime = remindAt.ToLocalTime();
            return $"Reminder set for '{title}' at {localTime:yyyy-MM-dd HH:mm}.";
        }

        private async Task<string> HandleCheckCalendar(int userId, NlpResult parsed)
        {
            DateTime fromDate = DateTime.UtcNow;
            DateTime toDate = DateTime.UtcNow.AddDays(7);
            
            if (parsed.Entities.TryGetValue("date", out var dateStr))
            {
                if (DateTime.TryParse(dateStr, out var specificDate))
                {
                    fromDate = specificDate.Date.ToUniversalTime();
                    toDate = specificDate.Date.AddDays(1).ToUniversalTime();
                }
            }

            var events = await _db.Events
                .Where(e => e.UserId == userId && 
                           e.StartUtc >= fromDate && 
                           e.StartUtc <= toDate)
                .OrderBy(e => e.StartUtc)
                .ToListAsync();

            if (!events.Any())
                return "No events found for the specified period.";

            var response = new System.Text.StringBuilder();
            response.AppendLine("Here are your upcoming events:");
            
            foreach (var evt in events.Take(5)) // Limit to 5 events
            {
                var localTime = evt.StartUtc.ToLocalTime();
                response.AppendLine($"• {evt.Title} - {localTime:MMM dd, HH:mm}");
            }

            if (events.Count > 5)
                response.AppendLine($"... and {events.Count - 5} more events.");

            return response.ToString();
        }

        private async Task<string> HandleCheckTasks(int userId, NlpResult parsed)
        {
            var statusFilter = parsed.Entities.GetValueOrDefault("status", "To Do") ?? "To Do";
            
            var tasks = await _db.Tasks
                .Where(t => t.UserId == userId && t.Status == statusFilter)
                .OrderBy(t => t.DueUtc)
                .ToListAsync();

            if (!tasks.Any())
                return $"No tasks with status '{statusFilter}' found.";

            var response = new System.Text.StringBuilder();
            response.AppendLine($"Here are your '{statusFilter}' tasks:");
            
            foreach (var task in tasks.Take(5)) // Limit to 5 tasks
            {
                var dueInfo = task.DueUtc.HasValue 
                    ? $"Due {task.DueUtc.Value.ToLocalTime():MMM dd}" 
                    : "No due date";
                response.AppendLine($"• {task.Title} - {dueInfo}");
            }

            if (tasks.Count > 5)
                response.AppendLine($"... and {tasks.Count - 5} more tasks.");

            return response.ToString();
        }

private Task<string> HandleEmailAction(int userId, NlpResult parsed, string source)
{
    return Task.FromResult("Email action processed. I'll help you manage your emails.");
}

public async Task SendReminderNotification(int userId, int taskId)
{
    var task = await _db.Tasks.FindAsync(taskId);
    if (task == null || task.Status == "Done") return;

    await _wa.SendMessageAsync(userId, $"🔔 Reminder: {task.Title}" +
        (task.Description != null ? $"\n{task.Description}" : ""));
}

public virtual async Task<bool> HandleEventConfirmationResponse(int userId, string contextData, string selectedOption)
{
    _logger.LogInformation("User {UserId} responded to event confirmation: {Option}", userId, selectedOption);
    await _wa.SendMessageAsync(userId, $"Event confirmation recorded: {selectedOption}");
    return true;
}

public virtual async Task<bool> HandleTaskConfirmationResponse(int userId, string contextData, string selectedOption)
{
    _logger.LogInformation("User {UserId} responded to task confirmation: {Option}", userId, selectedOption);
    await _wa.SendMessageAsync(userId, $"Task confirmation recorded: {selectedOption}");
    return true;
}

public virtual async Task<bool> HandleEmailActionResponse(int userId, string contextData, string selectedOption)
{
    _logger.LogInformation("User {UserId} responded to email action: {Option}", userId, selectedOption);
    await _wa.SendMessageAsync(userId, $"Email action recorded: {selectedOption}");
    return true;
}

    }
}