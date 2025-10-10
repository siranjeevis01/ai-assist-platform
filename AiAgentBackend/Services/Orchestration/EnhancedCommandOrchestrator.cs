using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.Services.Integrations;
using AiAgentBackend.Services.NLP;
using AiAgentBackend.Services.Orchestration;
using AiAgentBackend.Utils;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AiAgentBackend.Hubs;
using System.Text.Json;

namespace AiAgentBackend.Services.Orchestration
{
    public interface IEnhancedCommandOrchestrator : ICommandOrchestrator
    {
        Task<string> HandleEmailCommandAsync(int userId, NlpResult parsed, string source);
        Task<string> HandleCalendarCommandAsync(int userId, NlpResult parsed, string source);
        Task<string> HandleTaskCommandAsync(int userId, NlpResult parsed, string source);
        Task<string> HandleReminderCommandAsync(int userId, NlpResult parsed, string source);
        Task<string> HandleQueryCommandAsync(int userId, NlpResult parsed, string source);
    }

public class UnifiedCommandProcessor
{
    private readonly INlpService _nlpService;
    private readonly ILogger<UnifiedCommandProcessor> _logger;

    public UnifiedCommandProcessor(INlpService nlpService, ILogger<UnifiedCommandProcessor> logger)
    {
        _nlpService = nlpService;
        _logger = logger;
    }

    public async Task<CommandResult> ProcessCommandAsync(CommandRequest request)
    {
        try
        {
            // Determine source platform
            var platform = PlatformDetector.DetectPlatform(request.Source);
            
            // Parse command
            var parsed = await _nlpService.ParseAsync(request.Text, "UTC");
            
            // Execute command
            var results = new Dictionary<string, object>
            {
                ["intent"] = parsed.Intent,
                ["confidence"] = parsed.Confidence,
                ["entities"] = parsed.Entities
            };
            
            return new CommandResult 
            {
                Success = true,
                Message = "Command processed successfully",
                Data = results,
                Responses = PlatformDetector.FormatPlatformResponses(results, platform),
                Actions = new List<string> { parsed.Intent }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing command");
            return new CommandResult 
            {
                Success = false,
                Message = $"Error: {ex.Message}",
                Responses = new Dictionary<string, string>()
            };
        }
    }
}

    public class EnhancedCommandOrchestrator : CommandOrchestrator, IEnhancedCommandOrchestrator
    {
        private readonly IEnhancedGmailService _enhancedGmail;
        private readonly ApplicationDbContext _db;
        private readonly INlpService _nlpService;
        private readonly IConversationStateService _conversationStateService;
        private readonly IHttpWhatsAppService _enhancedWhatsApp;

        public EnhancedCommandOrchestrator(
            ApplicationDbContext db,
            INlpService nlp,
            IGoogleCalendarService cal,
            ITrelloService trello,
            IHttpWhatsAppService wa,
            IGmailService gmail,
            IHubContext<UpdatesHub> hub,
            ILogger<CommandOrchestrator> logger,
            IEnhancedGmailService enhancedGmail,
            IConversationStateService conversationStateService) 
            : base(db, nlp, cal, trello, wa, gmail, hub, logger)
        {
            _enhancedWhatsApp = wa;
            _enhancedGmail = enhancedGmail;
            _db = db;
            _nlpService = nlp;
            _conversationStateService = conversationStateService;
        }

        public new async Task<string> HandleAsync(int userId, string messageText, string source = "WhatsApp")
        {
            try
            {
                var user = await _db.Users
                    .Include(u => u.Preference)
                    .FirstOrDefaultAsync(u => u.Id == userId);
                    
                if (user == null)
                    throw new Exception($"User {userId} not found");

                var pref = user.Preference ?? new Preference { UserId = userId };
                var timezone = user.Timezone ?? "UTC";

                // Check for ongoing conversation state
                var currentState = await _conversationStateService.GetCurrentStateAsync(userId);
                if (currentState != null)
                {
                    return await HandleMultiStepConversation(userId, messageText, source, currentState);
                }

                // Parse the message with NLP
                var parsed = await _nlpService.ParseAsync(messageText, timezone);
                _logger.LogInformation($"Parsed intent: {parsed.Intent}, Confidence: {parsed.Confidence}");

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
                
                _db.Messages.Add(message);
                await _db.SaveChangesAsync();

                // Handle based on intent with enhanced capabilities
                switch (parsed.Intent)
                {
                    case "CreateEvent":
                        return await HandleCalendarCommandAsync(userId, parsed, source);
                        
                    case "CreateTask":
                    case "UpdateTask":
                        return await HandleTaskCommandAsync(userId, parsed, source);
                        
                    case "CreateReminder":
                        return await HandleReminderCommandAsync(userId, parsed, source);
                        
                    case "CheckCalendar":
                    case "CheckTasks":
                    case "CheckEmails":
                        return await HandleQueryCommandAsync(userId, parsed, source);
                        
                    case "EmailAction":
                        return await HandleEmailCommandAsync(userId, parsed, source);
                        
                    default:
                        return await HandleFallbackCommandAsync(userId, messageText, source);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing command");
                return $"Failed to process command: {ex.Message}";
            }
        }

        public async Task<string> HandleEmailCommandAsync(int userId, NlpResult parsed, string source)
        {
            try
            {
                var entities = parsed.Entities ?? new Dictionary<string, string>();
                
                var action = entities.GetValueOrDefault("action", "read");
                var subject = entities.GetValueOrDefault("subject", "");
                var from = entities.GetValueOrDefault("from", "");

                switch (action.ToLower())
                {
                    case "read":
                    case "check":
                        var emails = await _enhancedGmail.GetEmailsAsync(userId, "is:unread", 5);
                        if (!emails.Any())
                            return "No unread emails found.";

                        var response = "📧 Unread Emails:\n";
                        foreach (var email in emails.Take(3))
                            response += $"\n• {email.Subject.Truncate(50)} - From: {email.From}\n";

                        if (emails.Count > 3)
                            response += $"\n... and {emails.Count - 3} more emails";

                        await _enhancedWhatsApp.SendQuickReplyOptionsAsync(userId, response, new Dictionary<string, string>
                        {
                            { "Read", "Mark as read" },
                            { "Reply", "Send reply" },
                            { "Archive", "Move to archive" }
                        });

                        return "Here are your unread emails. Choose an action:";

                    case "reply":
                        if (string.IsNullOrEmpty(subject))
                            return "Please specify which email you want to reply to.";

                        return "Please type your reply message.";

                    case "send":
                        var to = entities.GetValueOrDefault("to", "");
                        var body = entities.GetValueOrDefault("body", entities.GetValueOrDefault("message", ""));

                        if (string.IsNullOrEmpty(to) || string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(body))
                            return "Please provide recipient, subject, and message content.";

                        await _enhancedGmail.SendEmailAsync(userId, to, subject, body);
                        return $"Email sent to {to} successfully.";

                    default:
                        return "I can help you with emails. Try: 'check emails', 'reply to email', or 'send email to john@example.com'";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling email command");
                return "Sorry, I encountered an error processing your email request.";
            }
        }

        public async Task<string> HandleCalendarCommandAsync(int userId, NlpResult parsed, string source)
        {
            try
            {
                var entities = parsed.Entities ?? new Dictionary<string, string>();
                
                var title = entities.GetValueOrDefault("title") ?? "Meeting";
                var location = entities.GetValueOrDefault("location", "");
                
                // Parse datetime
                DateTime start;
                if (parsed.Entities != null && parsed.Entities.TryGetValue("datetime", out var dtRaw) && DateTime.TryParse(dtRaw, out var dt))
                    start = dt.ToUniversalTime();
                else
                    start = DateTime.UtcNow.AddHours(1);

                // Parse duration
                var user = await _db.Users.Include(u => u.Preference).FirstOrDefaultAsync(u => u.Id == userId);
                var pref = user?.Preference ?? new Preference { UserId = userId };
                var durationMinutes = parsed.Entities != null && parsed.Entities.TryGetValue("duration_minutes", out var dstr) && 
                                      int.TryParse(dstr, out var d) ? d : pref.DefaultDurationMinutes;
                var end = start.AddMinutes(durationMinutes);

                // Parse attendees
                var attendees = new List<string>();
                if (parsed.Entities != null && parsed.Entities.TryGetValue("attendees", out var attendeeStr))
                {
                    attendees = attendeeStr.Split(',').Select(a => a.Trim()).ToList();
                }

                var evt = new Event
                {
                    UserId = userId,
                    Title = title,
                    Description = parsed.Entities?.GetValueOrDefault("description", "") ?? "",
                    StartUtc = new DateTimeOffset(start),
                    EndUtc = new DateTimeOffset(end),
                    Location = location,
                    Status = "Scheduled",
                    Source = source,
                    AttendeesJson = JsonSerializer.Serialize(attendees)
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

                // Send enhanced confirmation with quick actions
                await _enhancedWhatsApp.SendEventConfirmationAsync(userId, evt, new[] { "Confirm", "Reschedule", "Cancel", "Add Reminder" });

                // Notify via SignalR
                await _hub.Clients.User(userId.ToString()).SendAsync("ReceiveUpdate", new
                {
                    Type = "EventCreated",
                    Event = evt
                });

                return $"Event '{evt.Title}' created successfully. Please confirm the details.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling calendar command");
                return "Sorry, I encountered an error scheduling your event.";
            }
        }

        public async Task<string> HandleTaskCommandAsync(int userId, NlpResult parsed, string source)
        {
            try
            {
                var title = parsed.Entities?.GetValueOrDefault("title", "New Task") ?? "New Task";
                var action = parsed.Entities?.GetValueOrDefault("action", "create") ?? "create";
                
                if (action.ToLower() == "update" || action.ToLower() == "complete")
                {
                    // Handle task updates
                    var taskToUpdate = await FindTaskToUpdate(userId, parsed);
                    if (taskToUpdate == null)
                        return "No matching task found to update.";

                    if (action.ToLower() == "complete")
                    {
                        taskToUpdate.Status = "Done";
                        taskToUpdate.CompletedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        if (parsed.Entities != null && parsed.Entities.TryGetValue("status", out var status))
                            taskToUpdate.Status = status;
                    }

                    _db.Tasks.Update(taskToUpdate);
                    await _db.SaveChangesAsync();

                    await _enhancedWhatsApp.SendMessageAsync(userId, $"Task '{taskToUpdate.Title}' updated to {taskToUpdate.Status}.");
                    return $"Task '{taskToUpdate.Title}' updated successfully.";
                }

                // Create new task
                DateTime? due = null;
                if (parsed.Entities != null && parsed.Entities.TryGetValue("due", out var dueRaw) && DateTime.TryParse(dueRaw, out var du))
                    due = du.ToUniversalTime();
                else if (parsed.Entities != null && parsed.Entities.ContainsKey("due"))
                    due = DateTime.UtcNow.AddDays(1);

                var description = parsed.Entities?.GetValueOrDefault("description", "") ?? "";
                
                var labels = new List<string>();
                if (parsed.Entities != null && parsed.Entities.TryGetValue("labels", out var labelsStr))
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

                _db.Tasks.Add(task);
                await _db.SaveChangesAsync();

                // Send enhanced confirmation
                await _enhancedWhatsApp.SendTaskConfirmationAsync(userId, task, new[] { "Complete", "Edit", "Set Reminder", "Add to Calendar" });

                await _hub.Clients.User(userId.ToString()).SendAsync("ReceiveUpdate", new
                {
                    Type = "TaskCreated",
                    Task = task
                });

                return $"Task '{task.Title}' created successfully. What would you like to do next?";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling task command");
                return "Sorry, I encountered an error creating your task.";
            }
        }

        public async Task<string> HandleReminderCommandAsync(int userId, NlpResult parsed, string source)
        {
            try
            {
                var title = parsed.Entities?.GetValueOrDefault("title", "Reminder") ?? "Reminder";
                
                DateTime remindAt;
                if (parsed.Entities != null && parsed.Entities.TryGetValue("datetime", out var dtRaw) && DateTime.TryParse(dtRaw, out var dt))
                    remindAt = dt.ToUniversalTime();
                else
                    remindAt = DateTime.UtcNow.AddHours(1);

                // Create a task for the reminder
                var task = new TaskItem
                {
                    UserId = userId,
                    Title = title,
                    Description = parsed.Entities?.GetValueOrDefault("description", "") ?? "",
                    DueUtc = remindAt,
                    Status = "To Do",
                    CreatedAt = DateTime.UtcNow
                };

                _db.Tasks.Add(task);
                await _db.SaveChangesAsync();

                // Schedule WhatsApp reminder
                BackgroundJob.Schedule(() => 
                    _enhancedWhatsApp.SendReminderAsync(userId, "task", title, remindAt, task.Description), 
                    remindAt - DateTime.UtcNow
                );

                return $"Reminder set for '{title}' at {remindAt:yyyy-MM-dd HH:mm}. I'll notify you on WhatsApp.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling reminder command");
                return "Sorry, I encountered an error setting your reminder.";
            }
        }

        public async Task<string> HandleQueryCommandAsync(int userId, NlpResult parsed, string source)
        {
            try
            {
                var queryType = parsed.Intent.ToLower();
                
                switch (queryType)
                {
                    case "checkcalendar":
                        var fromDate = DateTime.UtcNow;
                        var toDate = DateTime.UtcNow.AddDays(7);
                        
                        if (parsed.Entities != null && parsed.Entities.TryGetValue("date", out var dateStr))
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

                        var eventResponse = "📅 Upcoming Events:\n";
                        foreach (var evt in events.Take(5))
                        {
                            eventResponse += $"\n• {evt.Title} - {evt.StartUtc.DateTime.ToLocalTime():MMM dd, HH:mm}";
                        }

                        return eventResponse;

                    case "checktasks":
                        var statusFilter = parsed.Entities?.GetValueOrDefault("status", "To Do") ?? "To Do";
                        
                        var tasks = await _db.Tasks
                            .Where(t => t.UserId == userId && t.Status == statusFilter)
                            .OrderBy(t => t.DueUtc)
                            .ToListAsync();

                        if (!tasks.Any())
                            return $"No tasks with status '{statusFilter}' found.";

                        var taskResponse = $"✅ {statusFilter} Tasks:\n";
                        foreach (var task in tasks.Take(5))
                        {
                            var dueInfo = task.DueUtc.HasValue 
                                ? $"Due {task.DueUtc.Value.ToLocalTime():MMM dd}" 
                                : "No due date";
                            taskResponse += $"\n• {task.Title} - {dueInfo}";
                        }

                        return taskResponse;

                    case "checkemails":
                        var emails = await _enhancedGmail.GetEmailsAsync(userId, "is:unread", 5);
                        if (!emails.Any())
                            return "No unread emails found.";

                        var emailResponse = "📧 Unread Emails:\n";
                        foreach (var email in emails.Take(3))
                        {
                            emailResponse += $"\n• {email.Subject.Truncate(50)} - From: {email.From}";
                        }

                        return emailResponse;

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

        private async Task<string> HandleFallbackCommandAsync(int userId, string messageText, string source)
        {
            // Use AI to generate a helpful response for unknown commands
            try
            {
                if (_nlpService is OpenAiNlpService nlpService)
                {
                    var context = $"User asked: {messageText}. Available commands: schedule meetings, create tasks, set reminders, check calendar, check emails.";
                    var response = await nlpService.GenerateResponseAsync(
                        "Provide a helpful response suggesting available commands",
                        context
                    );
                    
                    return response;
                }
            }
            catch
            {
                // Fallback to default response
            }

            return "Sorry, I didn't understand that command. I can help you with:\n" +
                   "• 📅 Scheduling meetings and events\n" +
                   "• ✅ Creating and managing tasks\n" +
                   "• 📧 Handling emails\n" +
                   "• 🔔 Setting reminders\n\n" +
                   "Try something like: 'Schedule a meeting tomorrow at 3 PM' or 'Create a task for project report'";
        }

        private async Task<string> HandleMultiStepConversation(int userId, string messageText, string source, AiAgentBackend.Models.ConversationState currentState)
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
                    await _enhancedWhatsApp.SendEventConfirmationAsync(userId, evt, 
                        new[] { "Confirm", "Reschedule", "Cancel", "Add Reminder" });
                    
                    return $"Event '{evt.Title}' has been created successfully! Please confirm the details above.";
                    
                default:
                    await _conversationStateService.ClearStateAsync(userId);
                    return "I lost track of our conversation. Please start over.";
            }
        }

        private async Task<TaskItem?> FindTaskToUpdate(int userId, NlpResult parsed)
        {
            TaskItem? taskToUpdate = null;
            
            if (parsed.Entities != null && parsed.Entities.TryGetValue("title", out var taskTitle))
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

            return taskToUpdate;
        }

        private async Task<string> HandleTaskCreationStep(int userId, string messageText, ConversationState state)
        {
            // Implement task creation step logic here
            await Task.CompletedTask;
            return "Task creation step not yet implemented.";
        }

        private async Task<string> HandleEmailActionStep(int userId, string messageText, ConversationState state)
        {
            // Implement email action step logic here
            await Task.CompletedTask;
            return "Email action step not yet implemented.";
        }

        private DateTime ParseDateTime(string input, string timezone)
        {
            // TODO: connect to NLP datetime parser
            // For now, use a simple implementation
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

        public override async Task<bool> HandleEventConfirmationResponse(int userId, string contextData, string selectedOption)
        {
            _logger.LogInformation("User {UserId} responded to event confirmation: {Option}", userId, selectedOption);
            await _enhancedWhatsApp.SendMessageAsync(userId, $"Event confirmation recorded: {selectedOption}");
            return true;
        }

        public override async Task<bool> HandleTaskConfirmationResponse(int userId, string contextData, string selectedOption)
        {
            _logger.LogInformation("User {UserId} responded to task confirmation: {Option}", userId, selectedOption);
            await _enhancedWhatsApp.SendMessageAsync(userId, $"Task confirmation recorded: {selectedOption}");
            return true;
        }

        public override async Task<bool> HandleEmailActionResponse(int userId, string contextData, string selectedOption)
        {
            _logger.LogInformation("User {UserId} responded to email action: {Option}", userId, selectedOption);
            await _enhancedWhatsApp.SendMessageAsync(userId, $"Email action recorded: {selectedOption}");
            return true;
        }
    }

    // EventCreationData class for multi-step event creation
    public class EventCreationData
    {
        public string Title { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<string> Attendees { get; set; } = new List<string>();
    }
}