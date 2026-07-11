using Hangfire;
using AiAgentBackend.Hubs;
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.Services.Integrations;
using AiAgentBackend.Services.NLP;
using AiAgentBackend.Utils;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using AiAgentBackend.Services.Messaging; // Add this using

namespace AiAgentBackend.Services.Orchestration
{
    public interface ICommandOrchestrator
    {
        Task<string> HandleAsync(int userId, string messageText, string source = "WhatsApp");
        Task<bool> HandleEventConfirmationResponse(int userId, string contextData, string selectedOption);
        Task<bool> HandleTaskConfirmationResponse(int userId, string contextData, string selectedOption);
        Task<bool> HandleEmailActionResponse(int userId, string contextData, string selectedOption);
    }

    public class CommandOrchestrator : ICommandOrchestrator
    {
        private readonly ApplicationDbContext _db;
        private readonly INlpService _nlp;
        private readonly IHubContext<UpdatesHub> _hub;
        private readonly IGoogleCalendarService _cal;
        private readonly ITrelloService _trello;
        private readonly IGmailService _gmail;
        private readonly IMessagingService _messagingService; // Changed from IWhatsAppService
        private readonly IConversationStateService _conversationStateService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CommandOrchestrator> _logger;

        public CommandOrchestrator(
            ApplicationDbContext db,
            INlpService nlp,
            IGoogleCalendarService cal,
            ITrelloService trello,
            IGmailService gmail,
            IHubContext<UpdatesHub> hub,
            ILogger<CommandOrchestrator> logger,
            IMessagingService messagingService, // Updated parameter
            IConversationStateService conversationStateService,
            IServiceScopeFactory scopeFactory)
        {
            _db = db;
            _nlp = nlp;
            _cal = cal;
            _trello = trello;
            _gmail = gmail;
            _hub = hub;
            _logger = logger;
            _messagingService = messagingService;
            _conversationStateService = conversationStateService;
            _scopeFactory = scopeFactory;
        }

        public async Task<string> HandleAsync(int userId, string messageText, string source = "WhatsApp")
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var nlp = scope.ServiceProvider.GetRequiredService<INlpService>();
            var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>(); // Updated variable
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<UpdatesHub>>();
            
            try
            {
                var cleanText = TextCleaner.CleanForNlp(messageText);
                
                if (!TextCleaner.IsValidForAnalysis(cleanText))
                {
                    return "Please provide a clear command. I can help with scheduling, tasks, emails, and reminders.";
                }

                var user = await db.Users
                    .Include(u => u.Preference)
                    .FirstOrDefaultAsync(u => u.Id == userId);
                    
                if (user == null)
                    throw new Exception($"User {userId} not found");

                var timezone = user.Timezone ?? "UTC";

                // Check for active multi-turn conversation first
                var currentState = await _conversationStateService.GetCurrentStateAsync(userId);
                if (currentState != null && !string.IsNullOrEmpty(currentState.Intent) && currentState.ExpiresAt > DateTime.UtcNow)
                {
                    _logger.LogInformation("Resuming multi-step conversation for user {UserId}, intent={Intent}, step={Step}",
                        userId, currentState.Intent, currentState.CurrentStep);
                    return await HandleMultiStepConversation(userId, messageText, source, currentState);
                }

                var parsed = await nlp.ParseAsync(cleanText, timezone, userId);
                _logger.LogInformation($"Parsed intent: {parsed.Intent}, Confidence: {parsed.Confidence}");

                if (parsed.Confidence < 0.3 && parsed.Intent != "Unknown")
                {
                    parsed.Intent = "Unknown";
                }

                // Store the message
                var message = new Message
                {
                    UserId = userId,
                    Channel = source,
                    Direction = "Incoming",
                    Body = messageText,
                    Intent = parsed.Intent,
                    EntitiesJson = JsonSerializer.Serialize(parsed.Entities),
                    CreatedAt = DateTime.UtcNow
                };
                
                db.Messages.Add(message);
                await db.SaveChangesAsync();

                // Handle based on intent
                var response = parsed.Intent switch
                {
                    "CreateEvent" => await HandleCalendarCommandAsync(userId, parsed, source),
                    "CreateTask" => await HandleTaskCommandAsync(userId, parsed, source),
                    "UpdateTask" => await HandleUpdateTaskCommandAsync(userId, parsed, source),
                    "CreateReminder" => await HandleReminderCommandAsync(userId, parsed, source),
                    "CheckCalendar" => await HandleQueryCommandAsync(userId, parsed, source),
                    "CheckTasks" => await HandleQueryCommandAsync(userId, parsed, source),
                    "CheckEmails" => await HandleEmailCommandAsync(userId, parsed, source),
                    "EmailAction" => await HandleEmailCommandAsync(userId, parsed, source),
                    "SendEmail" => await HandleEmailCommandAsync(userId, parsed, source),
                    "CheckWeather" => await HandleWeatherQueryAsync(userId, parsed, source),
                    "SetGoal" => await HandleGoalCommandAsync(userId, parsed, source),
                    "DeleteTask" => await HandleDeleteTaskCommandAsync(userId, parsed, source),
                    "SearchWeb" => await HandleSearchQueryAsync(userId, parsed, source),
                    _ => await HandleFallbackCommandAsync(userId, cleanText, source)
                };

                // Store conversation context for multi-turn memory
                if (nlp is IntelligentNlpService intelligentNlp)
                {
                    intelligentNlp.AddConversationContext(userId, messageText, response);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing command");
                try 
                { 
                    var errorMessagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>(); // Updated variable
                    await errorMessagingService.SendMessageAsync(userId, $"Error processing command: {ex.Message}"); 
                } 
                catch { }
                return $"Failed to process command: {ex.Message}";
            }
        }

        private async Task<string> HandleCalendarCommandAsync(int userId, NlpResult parsed, string source)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var calendar = scope.ServiceProvider.GetRequiredService<IGoogleCalendarService>();
            var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>(); // Updated variable
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<UpdatesHub>>();

            try
            {
                var user = await db.Users
                    .Include(u => u.Preference)
                    .FirstOrDefaultAsync(u => u.Id == userId);
                    
                var pref = user?.Preference ?? new Preference { DefaultDurationMinutes = 30 };

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
                    AttendeesJson = JsonSerializer.Serialize(attendees)
                };

                // Save to DB first
                db.Events.Add(evt);
                await db.SaveChangesAsync();

                // Try to create in Google Calendar if connected
                string? externalId = null;
                try
                {
                    var googleToken = await db.ProviderTokens
                        .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Google");
                        
                    if (googleToken != null)
                    {
                        var createdEvent = await calendar.CreateEventAsync(userId, evt);
                        if (createdEvent != null)
                        {
                            externalId = createdEvent.ExternalId;
                            evt.ExternalId = externalId;
                            db.Events.Update(evt);
                            await db.SaveChangesAsync();
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
                    
                await messagingService.SendQuickActionsAsync(userId, message, new[] { "Confirm", "Reschedule", "Cancel" });

                // Notify via SignalR
                await hub.Clients.User(userId.ToString()).SendAsync("ReceiveUpdate", new
                {
                    Type = "EventCreated",
                    Event = evt
                });

                return $"Event '{evt.Title}' created successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating event");
                return "Sorry, I encountered an error scheduling your event. Please try again.";
            }
        }

        // Remove the duplicate method declarations and use the existing ones
        public async Task<string> HandleTaskCommandAsync(int userId, NlpResult parsed, string source)
        {
            return await HandleCreateTask(userId, parsed, await GetUserPreference(userId), source);
        }

        public async Task<string> HandleUpdateTaskCommandAsync(int userId, NlpResult parsed, string source)
        {
            return await HandleUpdateTask(userId, parsed, source);
        }

        public async Task<string> HandleReminderCommandAsync(int userId, NlpResult parsed, string source)
        {
            return await HandleCreateReminder(userId, parsed, await GetUserPreference(userId), source);
        }

        public async Task<string> HandleEmailCommandAsync(int userId, NlpResult parsed, string source)
        {
            try
            {
                var entities = parsed.Entities ?? new Dictionary<string, string>();
                var action = entities.GetValueOrDefault("action", "read");

                switch (action.ToLower())
                {
                    case "read":
                    case "check":
                        var insights = await _gmail.GetInsightsAsync(userId, DateTime.UtcNow.AddDays(-7));
                        if (!insights.Any())
                            return "No recent emails found.";

                        var response = "📧 Recent Emails:\n";
                        foreach (var insight in insights.Take(3))
                        {
                            var urgency = insight.urgent ? "🚨 " : "";
                            response += $"\n{urgency}• {insight.subject.Truncate(50)} - From: {insight.from}";
                        }
                        return response;

                    case "send":
                        var to = entities.GetValueOrDefault("to", "");
                        var emailSubject = entities.GetValueOrDefault("subject", "");
                        var body = entities.GetValueOrDefault("body", "");

                        if (string.IsNullOrEmpty(to) || string.IsNullOrEmpty(emailSubject))
                            return "Please provide recipient and subject for the email.";

                        var result = await _gmail.SendEmailAsync(userId, to, emailSubject, body);
                        return result ? $"Email sent to {to} successfully." : "Failed to send email.";

                    default:
                        return "I can help you with emails. Try: 'check emails' or 'send email to john@example.com'";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling email command");
                return "Sorry, I encountered an error processing your email request.";
            }
        }

        private async Task<string> HandleWeatherQueryAsync(int userId, NlpResult parsed, string source)
        {
            var location = parsed.Entities.GetValueOrDefault("location", "your area");
            return $"I can help with weather once a weather API is configured. " +
                   $"Currently, I'd check the forecast for {location}. " +
                   $"Please configure a weather provider to enable this feature.";
        }

        private async Task<string> HandleGoalCommandAsync(int userId, NlpResult parsed, string source)
        {
            return await HandleCreateTask(userId, parsed, await GetUserPreference(userId), source);
        }

        private async Task<string> HandleDeleteTaskCommandAsync(int userId, NlpResult parsed, string source)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                var title = parsed.Entities.GetValueOrDefault("title", "");

                TaskItem? taskToDelete = null;

                if (!string.IsNullOrEmpty(title))
                {
                    taskToDelete = await db.Tasks
                        .Where(t => t.UserId == userId && t.Title.Contains(title))
                        .OrderByDescending(t => t.Id)
                        .FirstOrDefaultAsync();
                }

                if (taskToDelete == null)
                {
                    taskToDelete = await db.Tasks
                        .Where(t => t.UserId == userId)
                        .OrderByDescending(t => t.Id)
                        .FirstOrDefaultAsync();
                }

                if (taskToDelete == null)
                    return "No matching task found to delete.";

                var deletedTitle = taskToDelete.Title;
                db.Tasks.Remove(taskToDelete);
                await db.SaveChangesAsync();

                await _messagingService.SendMessageAsync(userId, $"Task '{deletedTitle}' has been deleted.");
                return $"Task '{deletedTitle}' deleted successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting task");
                return "Sorry, I encountered an error deleting the task. Please try again.";
            }
        }

        private async Task<string> HandleSearchQueryAsync(int userId, NlpResult parsed, string source)
        {
            var query = parsed.Entities.GetValueOrDefault("title", parsed.Entities.GetValueOrDefault("query", ""));
            if (string.IsNullOrEmpty(query))
                return "What would you like me to search for?";

            return $"Web search is coming soon. I would search for: \"{query}\". " +
                   $"This feature will be available in a future update.";
        }

        public async Task<string> HandleQueryCommandAsync(int userId, NlpResult parsed, string source)
        {
            try
            {
                var queryType = parsed.Intent.ToLower();
                
                switch (queryType)
                {
                    case "checkcalendar":
                        return await HandleCheckCalendar(userId, parsed);
                    case "checktasks":
                        return await HandleCheckTasks(userId, parsed);
                    case "checkemails":
                        return await HandleEmailCommandAsync(userId, parsed, source);
                    default:
                        return "I can show you your calendar events, tasks, or emails. Try: 'show my calendar', 'what are my tasks', or 'check my emails'";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling query command");
                return "Sorry, I encountered an error retrieving your information.";
            }
        }

public Task<string> HandleFallbackCommandAsync(int userId, string cleanText, string source)
{
    var suggestions = new List<string>
    {
        "📅 Schedule a meeting: 'Schedule meeting tomorrow at 3 PM'",
        "✅ Create a task: 'Create task for project report due Friday'",
        "🔔 Set a reminder: 'Remind me to call John at 2 PM'",
        "📧 Check emails: 'Check my emails'",
        "📅 View calendar: 'What's on my calendar today?'",
        "✅ View tasks: 'Show my tasks'"
    };

    var response = "I can help you with:\n" + string.Join("\n", suggestions.Take(3));
    response += "\n\nTry one of the commands above or ask for help with a specific task.";

    return Task.FromResult(response);
}

        // Keep the existing protected methods but ensure they use scoped services properly
protected virtual async Task<string> HandleCreateEvent(int userId, NlpResult parsed, Preference pref, string source)
{
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var calendar = scope.ServiceProvider.GetRequiredService<IGoogleCalendarService>();
    var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>(); // Changed this line
    var hub = scope.ServiceProvider.GetRequiredService<IHubContext<UpdatesHub>>();

    try
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
            AttendeesJson = JsonSerializer.Serialize(attendees)
        };

        // Save to DB first
        db.Events.Add(evt);
        await db.SaveChangesAsync();

        // Try to create in Google Calendar if connected
        string? externalId = null;
        try
        {
            var googleToken = await db.ProviderTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Google");
                
            if (googleToken != null)
            {
                var createdEvent = await calendar.CreateEventAsync(userId, evt);
                if (createdEvent != null)
                {
                    externalId = createdEvent.ExternalId;
                    evt.ExternalId = externalId;
                    db.Events.Update(evt);
                    await db.SaveChangesAsync();
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
            
        await messagingService.SendQuickActionsAsync(userId, message, new[] { "Confirm", "Reschedule", "Cancel" }); // Changed this line

        // Notify via SignalR
        await hub.Clients.User(userId.ToString()).SendAsync("ReceiveUpdate", new
        {
            Type = "EventCreated",
            Event = evt
        });

        return $"Event '{evt.Title}' created successfully.";
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error creating event");
        return "Sorry, I encountered an error scheduling your event. Please try again.";
    }
}

        protected virtual async Task<string> HandleCreateTask(int userId, NlpResult parsed, Preference pref, string source)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trello = scope.ServiceProvider.GetRequiredService<ITrelloService>();
            var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>(); // Updated
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<UpdatesHub>>();

            try
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
                    LabelsJson = JsonSerializer.Serialize(labels),
                    CreatedAt = DateTime.UtcNow
                };

                db.Tasks.Add(task);
                await db.SaveChangesAsync();

                // Try to create in Trello if connected
                string? externalId = null;
                try
                {
                    var trelloToken = await db.ProviderTokens
                        .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Trello");
                        
                    if (trelloToken != null)
                    {
                        var createdCard = await trello.CreateCardAsync(userId, task);
                        if (createdCard != null && !string.IsNullOrEmpty(createdCard.ExternalId))
                        {
                            externalId = createdCard.ExternalId;
                            task.ExternalId = externalId;
                            db.Tasks.Update(task);
                            await db.SaveChangesAsync();
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
                    
                await messagingService.SendQuickActionsAsync(userId, message, new[] { "Complete", "Snooze", "Edit" });

                await hub.Clients.User(userId.ToString()).SendAsync("ReceiveUpdate", new
                {
                    Type = "TaskCreated",
                    Task = task
                });

                return $"Task '{task.Title}' created successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating task");
                return "Sorry, I encountered an error creating your task. Please try again.";
            }
        }

        protected virtual async Task<string> HandleUpdateTask(int userId, NlpResult parsed, string source)
        {
            try
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

                await _messagingService.SendMessageAsync(userId, $"Task '{taskToUpdate.Title}' updated from {oldStatus} to {status}.");
                
                await _hub.Clients.User(userId.ToString()).SendAsync("ReceiveUpdate", new
                {
                    Type = "TaskUpdated",
                    Task = taskToUpdate
                });

                return $"Task '{taskToUpdate.Title}' updated to {status}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating task");
                return "Sorry, I encountered an error updating your task. Please try again.";
            }
        }

        protected virtual async Task<string> HandleCreateReminder(int userId, NlpResult parsed, Preference pref, string source)
        {
            try
            {
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating reminder");
                return "Sorry, I encountered an error setting your reminder. Please try again.";
            }
        }

        protected virtual async Task<string> HandleCheckCalendar(int userId, NlpResult parsed)
        {
            try
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
                response.AppendLine("📅 Here are your upcoming events:");
                
                foreach (var evt in events.Take(5))
                {
                    var localTime = evt.StartUtc.ToLocalTime();
                    response.AppendLine($"• {evt.Title} - {localTime:MMM dd, HH:mm}");
                }

                if (events.Count > 5)
                    response.AppendLine($"... and {events.Count - 5} more events.");

                return response.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking calendar");
                return "Sorry, I encountered an error checking your calendar. Please try again.";
            }
        }

        protected virtual async Task<string> HandleCheckTasks(int userId, NlpResult parsed)
        {
            try
            {
                var statusFilter = parsed.Entities.GetValueOrDefault("status", "To Do") ?? "To Do";
                
                var tasks = await _db.Tasks
                    .Where(t => t.UserId == userId && t.Status == statusFilter)
                    .OrderBy(t => t.DueUtc)
                    .ToListAsync();

                if (!tasks.Any())
                    return $"No tasks with status '{statusFilter}' found.";

                var response = new System.Text.StringBuilder();
                response.AppendLine($"✅ Here are your '{statusFilter}' tasks:");
                
                foreach (var task in tasks.Take(5))
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking tasks");
                return "Sorry, I encountered an error checking your tasks. Please try again.";
            }
        }

        private async Task<string> HandleMultiStepConversation(int userId, string messageText, string source, ConversationState currentState)
        {
            try
            {
                switch (currentState.Intent)
                {
                    case "CreateEvent":
                        return await HandleEventCreationStep(userId, messageText, currentState);
                    case "CreateTask":
                        return await HandleTaskCreationStep(userId, messageText, currentState);
                    case "EmailAction":
                        return await HandleEmailActionStep(userId, messageText, currentState);
                    default:
                        await _conversationStateService.ClearStateAsync(userId);
                        return "I lost track of our conversation. Please start over.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling multi-step conversation for user {UserId}", userId);
                await _conversationStateService.ClearStateAsync(userId);
                return "I encountered an error processing your request. Please try again.";
            }
        }

        private async Task<string> HandleEventCreationStep(int userId, string messageText, ConversationState currentState)
        {
            var eventData = JsonSerializer.Deserialize<EventCreationData>(currentState.ContextData) 
                        ?? new EventCreationData();
            
            switch (currentState.CurrentStep)
            {
                case "CONFIRM_TITLE":
                    eventData.Title = messageText;
                    await _conversationStateService.UpdateStateAsync(userId, "CreateEvent", "CONFIRM_DATE", 
                        JsonSerializer.Serialize(eventData));
                    return "Great! When would you like to schedule this event?";
                    
                case "CONFIRM_DATE":
                    // Parse date from message
                    var parsedDate = ParseDateTime(messageText, await GetUserTimezone(userId));
                    eventData.StartTime = parsedDate;
                    eventData.EndTime = parsedDate.AddHours(1);
                    
                    await _conversationStateService.UpdateStateAsync(userId, "CreateEvent", "CONFIRM_ATTENDEES", 
                        JsonSerializer.Serialize(eventData));
                    return "Got it! Would you like to add any attendees? (Please provide emails separated by commas)";
                    
                case "CONFIRM_ATTENDEES":
                    if (!string.IsNullOrEmpty(messageText) && messageText.ToLower() != "no")
                    {
                        eventData.Attendees = messageText.Split(',').Select(a => a.Trim()).ToList();
                    }
                    
                    // Create the event
                    var evt = new Event
                    {
                        UserId = userId,
                        Title = eventData.Title,
                        StartUtc = new DateTimeOffset(eventData.StartTime),
                        EndUtc = new DateTimeOffset(eventData.EndTime),
                        AttendeesJson = JsonSerializer.Serialize(eventData.Attendees),
                        Status = "Scheduled",
                        Source = "WhatsApp"
                    };
                    
                    _db.Events.Add(evt);
                    await _db.SaveChangesAsync();
                    
                    // Try to sync with Google Calendar
                    try
                    {
                        var googleToken = await _db.ProviderTokens
                            .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Google");
                            
                        if (googleToken != null)
                        {
                            await _cal.CreateEventAsync(userId, evt);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Google Calendar sync failed for event");
                    }
                    
                    await _conversationStateService.ClearStateAsync(userId);
                    
                    // Send confirmation with quick actions
                    await _messagingService.SendQuickActionsAsync(userId, $"Event '{evt.Title}' created successfully!", 
                        new[] { "Confirm", "Reschedule", "Cancel" });
                    
                    return $"Event '{evt.Title}' has been created successfully!";
                    
                default:
                    await _conversationStateService.ClearStateAsync(userId);
                    return "I lost track of our conversation. Please start over.";
            }
        }

        private async Task<string> HandleTaskCreationStep(int userId, string messageText, ConversationState state)
        {
            var taskData = JsonSerializer.Deserialize<TaskCreationData>(state.ContextData)
                        ?? new TaskCreationData();

            switch (state.CurrentStep)
            {
                case "CONFIRM_TITLE":
                    taskData.Title = messageText;
                    await _conversationStateService.UpdateStateAsync(userId, "CreateTask", "CONFIRM_DUE",
                        JsonSerializer.Serialize(taskData));
                    return "Got it! When is this task due? (e.g., 'tomorrow', 'Friday', 'next Monday', or say 'no due date')";

                case "CONFIRM_DUE":
                    if (messageText.ToLower() != "no" && messageText.ToLower() != "no due date")
                    {
                        var userTz = await GetUserTimezone(userId);
                        taskData.DueDate = ParseDateTime(messageText, userTz);
                    }
                    await _conversationStateService.UpdateStateAsync(userId, "CreateTask", "CONFIRM_PRIORITY",
                        JsonSerializer.Serialize(taskData));
                    return "What priority? (high, medium, low, or skip)";

                case "CONFIRM_PRIORITY":
                    var priority = messageText.ToLower().Trim();
                    if (priority is "high" or "medium" or "low")
                        taskData.Priority = priority;
                    await _conversationStateService.UpdateStateAsync(userId, "CreateTask", "CONFIRM_LABELS",
                        JsonSerializer.Serialize(taskData));
                    return "Any labels? (e.g., 'work, urgent' or skip)";

                case "CONFIRM_LABELS":
                    if (!string.IsNullOrEmpty(messageText) && messageText.ToLower() != "skip")
                    {
                        taskData.Labels = messageText.Split(',').Select(l => l.Trim()).ToList();
                    }

                    var task = new TaskItem
                    {
                        UserId = userId,
                        Title = taskData.Title,
                        Description = taskData.Description,
                        DueUtc = taskData.DueDate?.ToUniversalTime(),
                        Status = "To Do",
                        LabelsJson = JsonSerializer.Serialize(taskData.Labels),
                        CreatedAt = DateTime.UtcNow
                    };

                    _db.Tasks.Add(task);
                    await _db.SaveChangesAsync();

                    try
                    {
                        var trelloToken = await _db.ProviderTokens
                            .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Trello");
                        if (trelloToken != null)
                        {
                            var trello = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ITrelloService>();
                            var card = await trello.CreateCardAsync(userId, task);
                            if (card != null && !string.IsNullOrEmpty(card.ExternalId))
                            {
                                task.ExternalId = card.ExternalId;
                                _db.Tasks.Update(task);
                                await _db.SaveChangesAsync();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Trello sync failed during multi-turn task creation");
                    }

                    await _conversationStateService.ClearStateAsync(userId);

                    var dueMsg = task.DueUtc.HasValue ? $" due {task.DueUtc.Value.ToLocalTime():MMM dd}" : "";
                    var labelMsg = taskData.Labels.Any() ? $" with labels [{string.Join(", ", taskData.Labels)}]" : "";
                    await _messagingService.SendQuickActionsAsync(userId,
                        $"Task '{task.Title}' created!{dueMsg}{labelMsg}",
                        new[] { "Complete", "Snooze", "Edit" });

                    return $"Task '{task.Title}' created successfully!{dueMsg}{labelMsg}";

                default:
                    await _conversationStateService.ClearStateAsync(userId);
                    return "I lost track of our conversation. Please start over.";
            }
        }

        private async Task<string> HandleEmailActionStep(int userId, string messageText, ConversationState state)
        {
            var emailData = JsonSerializer.Deserialize<EmailActionData>(state.ContextData)
                        ?? new EmailActionData();

            switch (state.CurrentStep)
            {
                case "CONFIRM_RECIPIENT":
                    emailData.To = messageText;
                    await _conversationStateService.UpdateStateAsync(userId, "EmailAction", "CONFIRM_SUBJECT",
                        JsonSerializer.Serialize(emailData));
                    return "What should the subject be?";

                case "CONFIRM_SUBJECT":
                    emailData.Subject = messageText;
                    await _conversationStateService.UpdateStateAsync(userId, "EmailAction", "CONFIRM_BODY",
                        JsonSerializer.Serialize(emailData));
                    return "What should the email body say?";

                case "CONFIRM_BODY":
                    emailData.Body = messageText;
                    await _conversationStateService.UpdateStateAsync(userId, "EmailAction", "CONFIRM_SEND",
                        JsonSerializer.Serialize(emailData));
                    return $"Ready to send email to {emailData.To}:\nSubject: {emailData.Subject}\n\nSend it? (yes/no)";

                case "CONFIRM_SEND":
                    if (messageText.ToLower() is "yes" or "y" or "send")
                    {
                        try
                        {
                            var result = await _gmail.SendEmailAsync(userId, emailData.To, emailData.Subject, emailData.Body);
                            await _conversationStateService.ClearStateAsync(userId);
                            return result
                                ? $"Email sent to {emailData.To} successfully!"
                                : "Failed to send email. Please try again.";
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send email via multi-turn flow");
                            await _conversationStateService.ClearStateAsync(userId);
                            return "Failed to send email. Please try again later.";
                        }
                    }
                    else
                    {
                        await _conversationStateService.ClearStateAsync(userId);
                        return "Email cancelled. You can start a new email anytime.";
                    }

                default:
                    await _conversationStateService.ClearStateAsync(userId);
                    return "I lost track of our conversation. Please start over.";
            }
        }

        private DateTime ParseDateTime(string input, string timezone)
        {
            try
            {
                if (DateTime.TryParse(input, out var result))
                {
                    return result.ToUniversalTime();
                }
                
                // Handle relative times
                if (input.Contains("tomorrow"))
                {
                    return DateTime.UtcNow.AddDays(1);
                }
                else if (input.Contains("next week"))
                {
                    return DateTime.UtcNow.AddDays(7);
                }
                
                return DateTime.UtcNow.AddHours(1); // fallback
            }
            catch
            {
                return DateTime.UtcNow.AddHours(1); // fallback
            }
        }

        private async Task<string> GetUserTimezone(int userId)
        {
            var user = await _db.Users.FindAsync(userId);
            return user?.Timezone ?? "UTC";
        }

        private async Task<Preference> GetUserPreference(int userId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            
            var user = await db.Users
                .Include(u => u.Preference)
                .FirstOrDefaultAsync(u => u.Id == userId);
                
            return user?.Preference ?? new Preference { UserId = userId };
        }

        public async Task SendReminderNotification(int userId, int taskId)
        {
            try
            {
                var task = await _db.Tasks.FindAsync(taskId);
                if (task == null || task.Status == "Done") return;

                await _messagingService.SendMessageAsync(userId, $"🔔 Reminder: {task.Title}" + // Updated
                    (task.Description != null ? $"\n{task.Description}" : ""));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending reminder notification");
            }
        }

        public async Task<bool> HandleEventConfirmationResponse(int userId, string contextData, string selectedOption)
        {
            try
            {
                _logger.LogInformation("User {UserId} responded to event confirmation: {Option}", userId, selectedOption);
                await _messagingService.SendMessageAsync(userId, $"Event confirmation recorded: {selectedOption}"); // Updated
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling event confirmation");
                return false;
            }
        }

        public async Task<bool> HandleTaskConfirmationResponse(int userId, string contextData, string selectedOption)
        {
            try
            {
                _logger.LogInformation("User {UserId} responded to task confirmation: {Option}", userId, selectedOption);
                await _messagingService.SendMessageAsync(userId, $"Task confirmation recorded: {selectedOption}"); // Updated
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling task confirmation");
                return false;
            }
        }

        public async Task<bool> HandleEmailActionResponse(int userId, string contextData, string selectedOption)
        {
            try
            {
                _logger.LogInformation("User {UserId} responded to email action: {Option}", userId, selectedOption);
                await _messagingService.SendMessageAsync(userId, $"Email action recorded: {selectedOption}"); // Updated
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling email action");
                return false;
            }
        }
    }

    // Supporting classes
    public class EventCreationData
    {
        public string Title { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<string> Attendees { get; set; } = new List<string>();
    }

    public class TaskCreationData
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime? DueDate { get; set; }
        public string Priority { get; set; } = "medium";
        public List<string> Labels { get; set; } = new List<string>();
    }

    public class EmailActionData
    {
        public string To { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }
}