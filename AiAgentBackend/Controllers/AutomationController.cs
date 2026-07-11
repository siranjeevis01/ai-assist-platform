using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.Services.Automation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AutomationController : ControllerBase
    {
        private readonly IAutomationService _automation;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AutomationController> _logger;

        public AutomationController(
            IAutomationService automation,
            ApplicationDbContext db,
            ILogger<AutomationController> logger)
        {
            _automation = automation;
            _db = db;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetRules()
        {
            var userId = GetUserId();
            var rules = await _automation.GetUserRulesAsync(userId);
            return Ok(rules);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetRule(int id)
        {
            var userId = GetUserId();
            var rule = await _automation.GetRuleAsync(userId, id);
            if (rule == null) return NotFound();
            return Ok(rule);
        }

        [HttpPost]
        public async Task<IActionResult> CreateRule([FromBody] CreateAutomationRequest request)
        {
            var userId = GetUserId();

            var triggerConfig = JsonSerializer.Deserialize<Dictionary<string, string>>(request.TriggerConfig) ?? new();
            var actions = JsonSerializer.Deserialize<List<AutomationAction>>(request.ActionsJson) ?? new();

            var rule = await _automation.CreateRuleAsync(userId, request.Name, request.TriggerType, triggerConfig, actions);
            return CreatedAtAction(nameof(GetRule), new { id = rule.Id }, rule);
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateRule(int id, [FromBody] UpdateAutomationRequest request)
        {
            var userId = GetUserId();
            var result = await _automation.UpdateRuleAsync(userId, id, request.Name, request.IsActive);
            if (!result) return NotFound();
            return Ok(new { message = "Rule updated" });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRule(int id)
        {
            var userId = GetUserId();
            var result = await _automation.DeleteRuleAsync(userId, id);
            if (!result) return NotFound();
            return Ok(new { message = "Rule deleted" });
        }

        [HttpGet("templates")]
        public IActionResult GetTemplates()
        {
            var templates = new[]
            {
                new
                {
                    name = "Email → Create Task",
                    description = "When urgent email arrives, create a task",
                    triggerType = "email_received",
                    triggerConfig = new Dictionary<string, string> { ["priority"] = "equals:urgent" },
                    actions = new[]
                    {
                        new { type = "create_task", config = new Dictionary<string, string> { ["title"] = "Follow up: {subject}", ["description"] = "From: {from}" }, order = 0 }
                    }
                },
                new
                {
                    name = "Task Deadline → Reminder",
                    description = "Send notification when task is due soon",
                    triggerType = "task_due_soon",
                    triggerConfig = new Dictionary<string, string> { ["minutes_until_due"] = "gt:0" },
                    actions = new[]
                    {
                        new { type = "send_notification", config = new Dictionary<string, string> { ["message"] = "Task '{title}' is due in {time_until_due}" }, order = 0 }
                    }
                },
                new
                {
                    name = "Daily Summary",
                    description = "Send daily summary every morning",
                    triggerType = "schedule",
                    triggerConfig = new Dictionary<string, string> { ["cron"] = "0 9 * * *" },
                    actions = new[]
                    {
                        new { type = "send_notification", config = new Dictionary<string, string> { ["message"] = "Good morning! Here's your daily summary.\nPending tasks: {pending_tasks}\nToday's events: {today_events}" }, order = 0 }
                    }
                },
                new
                {
                    name = "New Document → Notify Team",
                    description = "When a document is uploaded, notify team members",
                    triggerType = "document_uploaded",
                    triggerConfig = new Dictionary<string, string>(),
                    actions = new[]
                    {
                        new { type = "send_notification", config = new Dictionary<string, string> { ["message"] = "New document uploaded: {file_name}" }, order = 0 }
                    }
                },
                new
                {
                    name = "New Event → Send Reminder",
                    description = "Send a reminder 30 minutes before each event",
                    triggerType = "event_created",
                    triggerConfig = new Dictionary<string, string> { ["minutes_before"] = "30" },
                    actions = new[]
                    {
                        new { type = "send_notification", config = new Dictionary<string, string> { ["message"] = "Reminder: '{title}' starts in 30 minutes at {location}" }, order = 0 }
                    }
                },
                new
                {
                    name = "Overdue Task → Email Alert",
                    description = "Send email alert when a task is overdue",
                    triggerType = "task_overdue",
                    triggerConfig = new Dictionary<string, string>(),
                    actions = new[]
                    {
                        new { type = "send_email", config = new Dictionary<string, string> { ["subject"] = "Overdue: {title}", ["body"] = "Task '{title}' was due on {due_date}. Please update its status." }, order = 0 },
                        new { type = "send_notification", config = new Dictionary<string, string> { ["message"] = "Task '{title}' is overdue!" }, order = 1 }
                    }
                },
                new
                {
                    name = "Weekly Report",
                    description = "Send weekly task completion report every Friday",
                    triggerType = "schedule",
                    triggerConfig = new Dictionary<string, string> { ["cron"] = "0 17 * * 5" },
                    actions = new[]
                    {
                        new { type = "send_notification", config = new Dictionary<string, string> { ["message"] = "Weekly Report:\nCompleted this week: {completed_this_week}\nPending: {pending_tasks}\nCompletion rate: {completion_rate}%" }, order = 0 }
                    }
                },
                new
                {
                    name = "Task Started → Log Activity",
                    description = "Log activity when a task moves to In Progress",
                    triggerType = "task_status_changed",
                    triggerConfig = new Dictionary<string, string> { ["from_status"] = "To Do", ["to_status"] = "In Progress" },
                    actions = new[]
                    {
                        new { type = "send_notification", config = new Dictionary<string, string> { ["message"] = "Started working on: {title}" }, order = 0 }
                    }
                },
                new
                {
                    name = "High Priority Email → Telegram Alert",
                    description = "Forward urgent emails to Telegram instantly",
                    triggerType = "email_received",
                    triggerConfig = new Dictionary<string, string> { ["priority"] = "equals:high" },
                    actions = new[]
                    {
                        new { type = "send_notification", config = new Dictionary<string, string> { ["channel"] = "telegram", ["message"] = "URGENT EMAIL\nFrom: {from}\nSubject: {subject}\nPreview: {snippet}" }, order = 0 }
                    }
                },
                new
                {
                    name = "Recurring Task Creator",
                    description = "Auto-create a recurring task every Monday at 9 AM",
                    triggerType = "schedule",
                    triggerConfig = new Dictionary<string, string> { ["cron"] = "0 9 * * 1" },
                    actions = new[]
                    {
                        new { type = "create_task", config = new Dictionary<string, string> { ["title"] = "Weekly review and planning", ["description"] = "Auto-generated weekly task", ["due_date"] = "end_of_week" }, order = 0 }
                    }
                }
            };

            return Ok(templates);
        }

        private int GetUserId() => int.Parse(User.FindFirst("userId")?.Value ?? "0");
    }

    public class CreateAutomationRequest
    {
        public string Name { get; set; } = string.Empty;
        public string TriggerType { get; set; } = string.Empty;
        public string TriggerConfig { get; set; } = "{}";
        public string ActionsJson { get; set; } = "[]";
    }

    public class UpdateAutomationRequest
    {
        public string? Name { get; set; }
        public bool? IsActive { get; set; }
    }
}
