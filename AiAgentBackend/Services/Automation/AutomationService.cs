using System.Text.Json;
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace AiAgentBackend.Services.Automation
{
    public interface IAutomationService
    {
        Task<AutomationRule> CreateRuleAsync(int userId, string name, string triggerType, Dictionary<string, string> triggerConfig, List<AutomationAction> actions);
        Task<List<AutomationRule>> GetUserRulesAsync(int userId);
        Task<AutomationRule?> GetRuleAsync(int userId, int ruleId);
        Task<bool> UpdateRuleAsync(int userId, int ruleId, string? name, bool? isActive);
        Task<bool> DeleteRuleAsync(int userId, int ruleId);
        Task EvaluateTriggersAsync(string triggerType, Dictionary<string, string> context);
    }

    public class AutomationService : IAutomationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AutomationService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public AutomationService(
            ApplicationDbContext db,
            ILogger<AutomationService> logger,
            IServiceScopeFactory scopeFactory)
        {
            _db = db;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public async Task<AutomationRule> CreateRuleAsync(
            int userId, string name, string triggerType,
            Dictionary<string, string> triggerConfig, List<AutomationAction> actions)
        {
            var rule = new AutomationRule
            {
                UserId = userId,
                Name = name,
                TriggerType = triggerType,
                TriggerConfig = JsonSerializer.Serialize(triggerConfig),
                ActionsJson = JsonSerializer.Serialize(actions),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.AutomationRules.Add(rule);
            await _db.SaveChangesAsync();
            return rule;
        }

        public async Task<List<AutomationRule>> GetUserRulesAsync(int userId)
        {
            return await _db.AutomationRules
                .Where(ar => ar.UserId == userId)
                .OrderByDescending(ar => ar.CreatedAt)
                .ToListAsync();
        }

        public async Task<AutomationRule?> GetRuleAsync(int userId, int ruleId)
        {
            return await _db.AutomationRules
                .FirstOrDefaultAsync(ar => ar.Id == ruleId && ar.UserId == userId);
        }

        public async Task<bool> UpdateRuleAsync(int userId, int ruleId, string? name, bool? isActive)
        {
            var rule = await _db.AutomationRules
                .FirstOrDefaultAsync(ar => ar.Id == ruleId && ar.UserId == userId);
            if (rule == null) return false;

            if (name != null) rule.Name = name;
            if (isActive.HasValue) rule.IsActive = isActive.Value;
            rule.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteRuleAsync(int userId, int ruleId)
        {
            var rule = await _db.AutomationRules
                .FirstOrDefaultAsync(ar => ar.Id == ruleId && ar.UserId == userId);
            if (rule == null) return false;

            _db.AutomationRules.Remove(rule);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task EvaluateTriggersAsync(string triggerType, Dictionary<string, string> context)
        {
            var matchingRules = await _db.AutomationRules
                .Where(ar => ar.IsActive && ar.TriggerType == triggerType)
                .ToListAsync();

            foreach (var rule in matchingRules)
            {
                try
                {
                    var triggerConfig = JsonSerializer.Deserialize<Dictionary<string, string>>(rule.TriggerConfig) ?? new();
                    if (EvaluateCondition(triggerConfig, context))
                    {
                        await ExecuteActionsAsync(rule, context);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error evaluating trigger for rule {RuleId}", rule.Id);
                }
            }
        }

        private bool EvaluateCondition(Dictionary<string, string> triggerConfig, Dictionary<string, string> context)
        {
            foreach (var (key, expectedValue) in triggerConfig)
            {
                if (!context.TryGetValue(key, out var actualValue))
                    return false;

                if (expectedValue.StartsWith("contains:"))
                {
                    var search = expectedValue["contains:".Length..];
                    if (!actualValue.Contains(search, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                else if (expectedValue.StartsWith("equals:"))
                {
                    var equals = expectedValue["equals:".Length..];
                    if (!actualValue.Equals(equals, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                else if (expectedValue.StartsWith("gt:"))
                {
                    if (double.TryParse(expectedValue["gt:".Length..], out var threshold) &&
                        double.TryParse(actualValue, out var actual) && actual <= threshold)
                        return false;
                }
                else if (expectedValue != actualValue)
                {
                    return false;
                }
            }
            return true;
        }

        private async Task ExecuteActionsAsync(AutomationRule rule, Dictionary<string, string> context)
        {
            var actions = JsonSerializer.Deserialize<List<AutomationAction>>(rule.ActionsJson) ?? new();
            var sortedActions = actions.OrderBy(a => a.Order).ToList();

            using var scope = _scopeFactory.CreateScope();
            var messagingService = scope.ServiceProvider.GetRequiredService<Services.Messaging.IMessagingService>();

            foreach (var action in sortedActions)
            {
                try
                {
                    if (action.DelayEnabled && action.DelayMinutes > 0)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(action.DelayMinutes));
                    }

                    switch (action.Type)
                    {
                        case "send_notification":
                            if (action.Config.TryGetValue("message", out var msg))
                            {
                                var resolvedMsg = ResolveTemplate(msg, context);
                                await messagingService.SendMessageAsync(rule.UserId, resolvedMsg);
                            }
                            break;

                        case "create_task":
                            var taskDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            var task = new TaskItem
                            {
                                UserId = rule.UserId,
                                Title = ResolveTemplate(action.Config.GetValueOrDefault("title", "Automation Task"), context),
                                Description = ResolveTemplate(action.Config.GetValueOrDefault("description", ""), context),
                                Status = "To Do",
                                CreatedAt = DateTime.UtcNow
                            };
                            if (action.Config.TryGetValue("due", out var dueStr) && DateTime.TryParse(dueStr, out var dueDate))
                                task.DueUtc = dueDate.ToUniversalTime();
                            taskDb.Tasks.Add(task);
                            await taskDb.SaveChangesAsync();
                            break;

                        case "send_email":
                            var gmailService = scope.ServiceProvider.GetRequiredService<Services.Integrations.IGmailService>();
                            await gmailService.SendEmailAsync(
                                rule.UserId,
                                action.Config.GetValueOrDefault("to", ""),
                                ResolveTemplate(action.Config.GetValueOrDefault("subject", ""), context),
                                ResolveTemplate(action.Config.GetValueOrDefault("body", ""), context)
                            );
                            break;

                        default:
                            _logger.LogWarning("Unknown automation action type: {ActionType}", action.Type);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing action {ActionType} for rule {RuleId}", action.Type, rule.Id);
                }
            }

            rule.RunCount++;
            rule.LastRunAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        private string ResolveTemplate(string template, Dictionary<string, string> context)
        {
            var result = template;
            foreach (var (key, value) in context)
            {
                result = result.Replace($"{{{key}}}", value);
            }
            return result;
        }
    }
}
