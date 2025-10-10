using AiAgentBackend.Data;
using AiAgentBackend.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Net.Http;


namespace AiAgentBackend.Services.Integrations
{
    public interface ITrelloService
    {
        Task<TaskItem> CreateCardAsync(int userId, TaskItem task);
        Task<TaskItem> UpdateCardAsync(int userId, TaskItem task);
        Task<bool> MoveCardAsync(int userId, int taskId, string status);
        Task SyncUserTasks(int userId);
        Task SyncAllUserTasks();
    }

    public class TrelloService : ITrelloService
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _http;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<TrelloService> _logger;

        public TrelloService(IConfiguration config, ApplicationDbContext db, ILogger<TrelloService> logger)
        {
            _config = config;
            _http = new HttpClient { BaseAddress = new Uri("https://api.trello.com/1/") };
            _db = db;
            _logger = logger;
        }

        private string ApiToken => _config["Trello:ApiToken"] ?? "";
        private string ApiKey => _config["Trello:ApiKey"] ?? "";
        private string BoardId => _config["Trello:DefaultBoardId"] ?? "";

        private string GetListIdForStatus(string status) => status switch
        {
            "To Do" => _config["Trello:ToDoListId"] ?? "",
            "In Progress" => _config["Trello:InProgressListId"] ?? "",
            "Done" => _config["Trello:DoneListId"] ?? "",
            _ => BoardId
        };

        public async Task<TaskItem> CreateCardAsync(int userId, TaskItem task)
        {
            try
            {
                var listId = GetListIdForStatus(task.Status);
                if (string.IsNullOrEmpty(listId))
                {
                    _logger.LogWarning("No list ID found for status {Status}", task.Status);
                    return task;
                }

                var dueDate = task.DueUtc?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var description = !string.IsNullOrEmpty(task.Description) ? task.Description : "";
                
                var url = $"cards?key={ApiKey}&token={ApiToken}&idList={listId}&name={Uri.EscapeDataString(task.Title)}&desc={Uri.EscapeDataString(description)}";
                
                if (!string.IsNullOrEmpty(dueDate))
                {
                    url += $"&due={dueDate}";
                }

                // Add labels if any
                if (!string.IsNullOrEmpty(task.LabelsJson))
                {
                    var labels = JsonSerializer.Deserialize<List<string>>(task.LabelsJson) ?? new List<string>();
                    if (labels != null && labels.Any())
                    {
                        // First, get or create labels
                        var labelIds = new List<string>();
                        foreach (var labelName in labels)
                        {
                            var labelId = await GetOrCreateLabelId(userId, labelName);
                            if (!string.IsNullOrEmpty(labelId))
                            {
                                labelIds.Add(labelId);
                            }
                        }
                        
                        if (labelIds.Any())
                        {
                            url += $"&idLabels={string.Join(",", labelIds)}";
                        }
                    }
                }

                var response = await _http.PostAsync(url, null);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Trello API error: {Error}", error);
                    return task;
                }

                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                task.ExternalId = json.GetProperty("id").GetString() ?? throw new Exception("Trello ID missing");
                task.LabelsJson = json.TryGetProperty("labels", out var labelsElement) ? 
                    JsonSerializer.Serialize(labelsElement.EnumerateArray().Select(l => l.GetProperty("name").GetString())) : 
                    task.LabelsJson;

                _logger.LogInformation("Created Trello card {CardId} for task {TaskId}", task.ExternalId, task.Id);
                return task;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Trello card for task {TaskId}", task.Id);
                return task;
            }
        }

        public async Task<TaskItem> UpdateCardAsync(int userId, TaskItem task)
        {
            if (string.IsNullOrEmpty(task.ExternalId))
            {
                _logger.LogWarning("Task {TaskId} has no external ID, cannot update Trello", task.Id);
                return task;
            }

            try
            {
                var updates = new List<string>();
                
                // Update list if status changed
                var listId = GetListIdForStatus(task.Status);
                if (!string.IsNullOrEmpty(listId))
                {
                    updates.Add($"idList={listId}");
                }

                // Update due date if changed
                if (task.DueUtc.HasValue)
                {
                    updates.Add($"due={task.DueUtc.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")}");
                }
                else
                {
                    updates.Add("due=null");
                }

                // Update name/description if changed
                updates.Add($"name={Uri.EscapeDataString(task.Title)}");
                updates.Add($"desc={Uri.EscapeDataString(task.Description ?? "")}");

                if (updates.Any())
                {
                    var url = $"cards/{task.ExternalId}?key={ApiKey}&token={ApiToken}&{string.Join("&", updates)}";
                    await _http.PutAsync(url, null);
                }

                // Update labels if changed
                if (!string.IsNullOrEmpty(task.LabelsJson))
                {
                    var labels = JsonSerializer.Deserialize<List<string>>(task.LabelsJson) ?? new List<string>();
                    if (labels != null)
                    {
                        await UpdateCardLabels(task.ExternalId, labels);
                    }
                }

                _logger.LogInformation("Updated Trello card {CardId} for task {TaskId}", task.ExternalId, task.Id);
                return task;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update Trello card {CardId} for task {TaskId}", task.ExternalId, task.Id);
                return task;
            }
        }

        public async Task<bool> MoveCardAsync(int userId, int taskId, string status)
        {
            try
            {
                var cardId = await GetExternalIdFromTaskId(taskId);
                var listId = GetListIdForStatus(status);
                
                if (string.IsNullOrEmpty(listId))
                {
                    _logger.LogWarning("No list ID found for status {Status}", status);
                    return false;
                }

                var url = $"cards/{cardId}?key={ApiKey}&token={ApiToken}&idList={listId}";
                var response = await _http.PutAsync(url, null);
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move card for task {TaskId} to status {Status}", taskId, status);
                return false;
            }
        }

        public async Task SyncUserTasks(int userId)
        {
            try
            {
                var trelloToken = await _db.ProviderTokens
                    .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Trello");
                    
                if (trelloToken == null) 
                {
                    _logger.LogWarning("No Trello token found for user {UserId}", userId);
                    return;
                }

                // Get all cards from the board
                var url = $"boards/{BoardId}/cards?key={ApiKey}&token={ApiToken}";
                var response = await _http.GetAsync(url);
                
                if (!response.IsSuccessStatusCode) 
                {
                    _logger.LogError("Failed to fetch cards from Trello board");
                    return;
                }
                
                var cards = await response.Content.ReadFromJsonAsync<JsonElement>();
                var cardList = cards.EnumerateArray().ToList();

                // Get existing tasks for this user
                var existingTasks = await _db.Tasks
                    .Where(t => t.UserId == userId && !string.IsNullOrEmpty(t.ExternalId))
                    .ToListAsync();

foreach (var card in cardList)
{
    var cardId = card.GetProperty("id").GetString() ?? "";
    var cardName = card.GetProperty("name").GetString() ?? "Untitled";
    var cardDesc = card.GetProperty("desc").GetString();
    var listId = card.GetProperty("idList").GetString();

    // Safely parse due date
    DateTime? dueDate = null;
    if (card.TryGetProperty("due", out var dueProp) && dueProp.ValueKind != JsonValueKind.Null)
    {
        var dueString = dueProp.GetString();
        if (!string.IsNullOrEmpty(dueString))
            dueDate = DateTime.Parse(dueString);
    }

    var status = GetStatusFromListId(listId ?? "");

    // Check if we already have this task
    var existingTask = existingTasks.FirstOrDefault(t => t.ExternalId == cardId);
    
    if (existingTask == null)
    {
        var newTask = new TaskItem
        {
            UserId = userId,
            Title = cardName,
            Description = cardDesc,
            DueUtc = dueDate,
            Status = status,
            ExternalId = cardId,
            CreatedAt = DateTime.UtcNow
        };
        _db.Tasks.Add(newTask);
    }
    else
    {
        existingTask.Title = cardName;
        existingTask.Description = cardDesc;
        existingTask.DueUtc = dueDate;
        existingTask.Status = status;
        _db.Tasks.Update(existingTask);
    }
}

                await _db.SaveChangesAsync();
                _logger.LogInformation("Synced Trello tasks for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync Trello tasks for user {UserId}", userId);
            }
        }

        public async Task SyncAllUserTasks()
        {
            try
            {
                var usersWithTrello = await _db.ProviderTokens
                    .Where(t => t.Provider == "Trello")
                    .Select(t => t.UserId)
                    .Distinct()
                    .ToListAsync();

                _logger.LogInformation("Syncing Trello tasks for {Count} users", usersWithTrello.Count);

                foreach (var userId in usersWithTrello)
                {
                    await SyncUserTasks(userId);
                    await Task.Delay(1000); // Rate limiting
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync all user tasks with Trello");
            }
        }

        private async Task<string> GetExternalIdFromTaskId(int taskId)
        {
            var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId);
            if (task == null || string.IsNullOrEmpty(task.ExternalId))
                throw new Exception($"Task {taskId} does not have an ExternalId.");
            return task.ExternalId;
        }

        private async Task<string> GetOrCreateLabelId(int userId, string labelName)
        {
            try
            {
                // First, try to find existing label
                var url = $"boards/{BoardId}/labels?key={ApiKey}&token={ApiToken}";
                var response = await _http.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var labels = await response.Content.ReadFromJsonAsync<JsonElement>();
                    foreach (var label in labels.EnumerateArray())
                    {
                        if (label.TryGetProperty("name", out var name) && 
                            name.GetString()?.Equals(labelName, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            return label.GetProperty("id").GetString() ?? throw new Exception($"Label ID not found for label {labelName}");
                        }
                    }
                }

                // Create new label if not found
                var createUrl = $"labels?key={ApiKey}&token={ApiToken}&name={Uri.EscapeDataString(labelName)}&idBoard={BoardId}&color=blue";
                var createResponse = await _http.PostAsync(createUrl, null);
                
                if (createResponse.IsSuccessStatusCode)
                {
                    var newLabel = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
                    return newLabel.GetProperty("id").GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get or create label {LabelName}", labelName);
            }

            return "";
        }

        private async Task UpdateCardLabels(string cardId, List<string> labels)
        {
            try
            {
                // First, get current labels
                var url = $"cards/{cardId}/labels?key={ApiKey}&token={ApiToken}";
                var response = await _http.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var currentLabels = await response.Content.ReadFromJsonAsync<JsonElement>();
                    
                    // Remove labels not in the new list
                    foreach (var label in currentLabels.EnumerateArray())
                    {
                        var labelId = label.GetProperty("id").GetString();
                        var labelName = label.GetProperty("name").GetString();
                        
                        if (!labels.Contains(labelName, StringComparer.OrdinalIgnoreCase))
                        {
                            await _http.DeleteAsync($"cards/{cardId}/idLabels/{labelId}?key={ApiKey}&token={ApiToken}");
                        }
                    }
                }

                // Add new labels
                foreach (var labelName in labels)
                {
                    var labelId = await GetOrCreateLabelId(0, labelName); // userId not needed for this operation
                    if (!string.IsNullOrEmpty(labelId))
                    {
                        await _http.PostAsync($"cards/{cardId}/idLabels?key={ApiKey}&token={ApiToken}&value={labelId}", null);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update labels for card {CardId}", cardId);
            }
        }

        private string GetStatusFromListId(string listId)
        {
            if (listId == _config["Trello:ToDoListId"]) return "To Do";
            if (listId == _config["Trello:InProgressListId"]) return "In Progress";
            if (listId == _config["Trello:DoneListId"]) return "Done";
            return "To Do";
        }
    }
}