using AiAgentBackend.Configuration;
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Microsoft.Extensions.Options;


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
        private readonly TrelloOptions _options;
        private readonly HttpClient _http;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<TrelloService> _logger;

        public TrelloService(IOptions<TrelloOptions> options, ApplicationDbContext db, ILogger<TrelloService> logger)
        {
            _options = options.Value;
            _http = new HttpClient { BaseAddress = new Uri("https://api.trello.com/1/") };
            _db = db;
            _logger = logger;
        }

        private string BoardId => _options.DefaultBoardId;

        private async Task<(string apiKey, string apiToken)> GetUserCredentialsAsync(int userId)
        {
            var token = await _db.ProviderTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Trello");

            if (token != null)
                return (GetConsumerKey(), token.EncryptedAccessToken);

            var sharedKey = !string.IsNullOrEmpty(_options.ConsumerKey) ? _options.ConsumerKey : _options.ApiKey;
            var sharedToken = _options.AccessToken;
            if (!string.IsNullOrEmpty(sharedKey) && !string.IsNullOrEmpty(sharedToken))
                return (sharedKey, sharedToken);

            return (string.Empty, string.Empty);
        }

        private string GetConsumerKey() =>
            !string.IsNullOrEmpty(_options.ConsumerKey) ? _options.ConsumerKey : _options.ApiKey;

        private string GetListIdForStatus(string status) => status switch
        {
            "To Do" => _options.ToDoListId,
            "In Progress" => _options.InProgressListId,
            "Done" => _options.DoneListId,
            _ => BoardId
        };

        public async Task<TaskItem> CreateCardAsync(int userId, TaskItem task)
        {
            try
            {
                var (apiKey, apiToken) = await GetUserCredentialsAsync(userId);
                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiToken))
                {
                    _logger.LogWarning("No Trello credentials for user {UserId}", userId);
                    return task;
                }

                var listId = GetListIdForStatus(task.Status);
                if (string.IsNullOrEmpty(listId))
                {
                    _logger.LogWarning("No list ID found for status {Status}", task.Status);
                    return task;
                }

                var dueDate = task.DueUtc?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var description = !string.IsNullOrEmpty(task.Description) ? task.Description : "";
                
                var url = $"cards?key={apiKey}&token={apiToken}&idList={listId}&name={Uri.EscapeDataString(task.Title)}&desc={Uri.EscapeDataString(description)}";
                
                if (!string.IsNullOrEmpty(dueDate))
                {
                    url += $"&due={dueDate}";
                }

                if (!string.IsNullOrEmpty(task.LabelsJson))
                {
                    var labels = JsonSerializer.Deserialize<List<string>>(task.LabelsJson) ?? new List<string>();
                    if (labels != null && labels.Any())
                    {
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
                var (apiKey, apiToken) = await GetUserCredentialsAsync(userId);
                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiToken))
                {
                    _logger.LogWarning("No Trello credentials for user {UserId}", userId);
                    return task;
                }

                var updates = new List<string>();
                
                var listId = GetListIdForStatus(task.Status);
                if (!string.IsNullOrEmpty(listId))
                {
                    updates.Add($"idList={listId}");
                }

                if (task.DueUtc.HasValue)
                {
                    updates.Add($"due={task.DueUtc.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")}");
                }
                else
                {
                    updates.Add("due=null");
                }

                updates.Add($"name={Uri.EscapeDataString(task.Title)}");
                updates.Add($"desc={Uri.EscapeDataString(task.Description ?? "")}");

                if (updates.Any())
                {
                    var url = $"cards/{task.ExternalId}?key={apiKey}&token={apiToken}&{string.Join("&", updates)}";
                    await _http.PutAsync(url, null);
                }

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
                var (apiKey, apiToken) = await GetUserCredentialsAsync(userId);
                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiToken))
                    return false;

                var cardId = await GetExternalIdFromTaskId(taskId);
                var listId = GetListIdForStatus(status);
                
                if (string.IsNullOrEmpty(listId))
                {
                    _logger.LogWarning("No list ID found for status {Status}", status);
                    return false;
                }

                var url = $"cards/{cardId}?key={apiKey}&token={apiToken}&idList={listId}";
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
                var (apiKey, apiToken) = await GetUserCredentialsAsync(userId);
                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiToken))
                {
                    _logger.LogWarning("No Trello credentials for user {UserId}", userId);
                    return;
                }

                var url = $"boards/{BoardId}/cards?key={apiKey}&token={apiToken}";
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
                var (apiKey, apiToken) = await GetUserCredentialsAsync(userId);
                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiToken))
                    return "";

                var url = $"boards/{BoardId}/labels?key={apiKey}&token={apiToken}";
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

                var createUrl = $"labels?key={apiKey}&token={apiToken}&name={Uri.EscapeDataString(labelName)}&idBoard={BoardId}&color=blue";
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
                var (apiKey, apiToken) = await GetUserCredentialsAsync(0);
                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiToken))
                    return;

                var url = $"cards/{cardId}/labels?key={apiKey}&token={apiToken}";
                var response = await _http.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var currentLabels = await response.Content.ReadFromJsonAsync<JsonElement>();
                    
                    foreach (var label in currentLabels.EnumerateArray())
                    {
                        var labelId = label.GetProperty("id").GetString();
                        var labelName = label.GetProperty("name").GetString();
                        
                        if (!labels.Contains(labelName, StringComparer.OrdinalIgnoreCase))
                        {
                            await _http.DeleteAsync($"cards/{cardId}/idLabels/{labelId}?key={apiKey}&token={apiToken}");
                        }
                    }
                }

                foreach (var labelName in labels)
                {
                    var labelId = await GetOrCreateLabelId(0, labelName);
                    if (!string.IsNullOrEmpty(labelId))
                    {
                        await _http.PostAsync($"cards/{cardId}/idLabels?key={apiKey}&token={apiToken}&value={labelId}", null);
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
            if (listId == _options.ToDoListId) return "To Do";
            if (listId == _options.InProgressListId) return "In Progress";
            if (listId == _options.DoneListId) return "Done";
            return "To Do";
        }
    }
}