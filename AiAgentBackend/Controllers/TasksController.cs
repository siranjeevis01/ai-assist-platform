using AiAgentBackend.Data;
using AiAgentBackend.DTOs.Tasks;
using AiAgentBackend.Models;
using AiAgentBackend.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TasksController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ITrelloService _trello;
        private readonly ILogger<TasksController> _logger;

        public TasksController(ApplicationDbContext db, ITrelloService trello, 
                             ILogger<TasksController> logger)
        { 
            _db = db; 
            _trello = trello;
            _logger = logger;
        }

        private int GetUserId()
        {
            var userIdStr = User.FindFirstValue("uid") ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TaskItem>>> List([FromQuery] string? status = null)
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var query = _db.Tasks
                    .Where(t => t.UserId == userId);
                
                if (!string.IsNullOrWhiteSpace(status))
                    query = query.Where(t => t.Status == status);
                
                query = query.OrderByDescending(t => t.CreatedAt);

                var tasks = await query.ToListAsync();
                return Ok(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing tasks");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpPost]
        public async Task<ActionResult<TaskItem>> Create([FromBody] CreateTaskRequest req)
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var task = new TaskItem
                { 
                    UserId = userId,
                    Title = req.Title,
                    DueUtc = req.DueUtc,
                    Description = req.Description,
                    Status = "To Do",
                    CreatedAt = DateTime.UtcNow
                };

                if (!string.IsNullOrWhiteSpace(req.LabelsCsv))
                {
                    var labels = req.LabelsCsv.Split(',')
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrEmpty(x))
                        .ToArray();
                    task.LabelsJson = JsonSerializer.Serialize(labels);
                }

                _db.Tasks.Add(task);
                
                // Log the task creation
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Entity = "Task",
                    Action = "Create",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                // Sync with Trello if connected
                try
                {
                    var hasTrello = await _db.ProviderTokens
                        .AnyAsync(pt => pt.UserId == userId && pt.Provider == "Trello");
                        
                    if (hasTrello)
                    {
                        var created = await _trello.CreateCardAsync(userId, task);
                        if (created != null && !string.IsNullOrEmpty(created.ExternalId))
                        {
                            task.ExternalId = created.ExternalId;
                            _db.Tasks.Update(task);
                            await _db.SaveChangesAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Trello sync failed for task {TaskId}", task.Id);
                }

                _logger.LogInformation("Task created: {TaskId} by user {UserId}", task.Id, userId);

                return CreatedAtAction(nameof(List), new { id = task.Id }, task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating task");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpPatch("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateTaskRequest req)
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
                if (task is null) return NotFound();

                if (req.Title is not null) 
                    task.Title = req.Title;
                    
                if (req.Status is not null) 
                    task.Status = req.Status;
                    
                if (req.DueUtc.HasValue) 
                    task.DueUtc = req.DueUtc;
                    
                if (req.Description is not null) 
                    task.Description = req.Description;

                if (!string.IsNullOrWhiteSpace(req.LabelsCsv))
                {
                    var labels = req.LabelsCsv.Split(',')
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrEmpty(x))
                        .ToArray();
                    task.LabelsJson = JsonSerializer.Serialize(labels);
                }

                // Set completed date if task is marked as done
                if (req.Status == "Done" && task.Status != "Done")
                    task.CompletedAt = DateTime.UtcNow;
                else if (req.Status != "Done" && task.Status == "Done")
                    task.CompletedAt = null;

                _db.Tasks.Update(task);
                
                // Log the task update
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Entity = "Task",
                    Action = "Update",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                // Sync with Trello if connected
                try
                {
                    var hasTrello = await _db.ProviderTokens
                        .AnyAsync(pt => pt.UserId == userId && pt.Provider == "Trello");
                        
                    if (hasTrello && !string.IsNullOrEmpty(task.ExternalId))
                    {
                        await _trello.UpdateCardAsync(userId, task);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Trello update failed for task {TaskId}", task.Id);
                }

                _logger.LogInformation("Task updated: {TaskId} by user {UserId}", task.Id, userId);

                return Ok(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating task {TaskId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
                if (task is null) return NotFound();

                // Delete from Trello first if connected
                try
                {
                    var hasTrello = await _db.ProviderTokens
                        .AnyAsync(pt => pt.UserId == userId && pt.Provider == "Trello");
                        
                    if (hasTrello && !string.IsNullOrEmpty(task.ExternalId))
                    {
                        // Trello deletion is handled via webhooks when card is deleted
                        // We just need to ensure our local record is removed
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Trello delete handling failed for task {TaskId}", task.Id);
                }

                _db.Tasks.Remove(task);
                
                // Log the task deletion
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Entity = "Task",
                    Action = "Delete",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                _logger.LogInformation("Task deleted: {TaskId} by user {UserId}", task.Id, userId);

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting task {TaskId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpPost("{id:int}/complete")]
        public async Task<IActionResult> Complete(int id)
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
                if (task is null) return NotFound();

                task.Status = "Done";
                task.CompletedAt = DateTime.UtcNow;

                _db.Tasks.Update(task);
                
                // Log the task completion
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Entity = "Task",
                    Action = "Complete",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                // Sync with Trello if connected
                try
                {
                    var hasTrello = await _db.ProviderTokens
                        .AnyAsync(pt => pt.UserId == userId && pt.Provider == "Trello");
                        
                    if (hasTrello && !string.IsNullOrEmpty(task.ExternalId))
                    {
                        await _trello.MoveCardAsync(userId, task.Id, "Done");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Trello completion sync failed for task {TaskId}", task.Id);
                }

                _logger.LogInformation("Task completed: {TaskId} by user {UserId}", task.Id, userId);

                return Ok(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing task {TaskId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpPost("{id:int}/sync")]
        public async Task<IActionResult> SyncWithTrello(int id)
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
                if (task is null) return NotFound();

                var hasTrello = await _db.ProviderTokens
                    .AnyAsync(pt => pt.UserId == userId && pt.Provider == "Trello");
                    
                if (!hasTrello)
                    return BadRequest(new { error = "Trello not connected" });

                TaskItem syncedTask;
                if (string.IsNullOrEmpty(task.ExternalId))
                {
                    syncedTask = await _trello.CreateCardAsync(userId, task);
                }
                else
                {
                    syncedTask = await _trello.UpdateCardAsync(userId, task);
                }

                if (syncedTask != null && !string.IsNullOrEmpty(syncedTask.ExternalId))
                {
                    task.ExternalId = syncedTask.ExternalId;
                    _db.Tasks.Update(task);
                    await _db.SaveChangesAsync();
                }

                // Log the sync action
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Entity = "Task",
                    Action = "SyncWithTrello",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                _logger.LogInformation("Task synced with Trello: {TaskId} by user {UserId}", task.Id, userId);

                return Ok(new { message = "Task synced successfully", externalId = task.ExternalId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing task {TaskId} with Trello", id);
                return StatusCode(500, new { error = "Internal server error during sync" });
            }
        }

        [HttpPost("sync-all")]
        public async Task<IActionResult> SyncAllWithTrello()
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var hasTrello = await _db.ProviderTokens
                    .AnyAsync(pt => pt.UserId == userId && pt.Provider == "Trello");
                    
                if (!hasTrello)
                    return BadRequest(new { error = "Trello not connected" });

                await _trello.SyncUserTasks(userId);

                // Log the bulk sync action
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Entity = "Task",
                    Action = "SyncAllWithTrello",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                _logger.LogInformation("All tasks synced with Trello for user {UserId}", userId);

                return Ok(new { message = "All tasks synced successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing all tasks with Trello");
                return StatusCode(500, new { error = "Internal server error during bulk sync" });
            }
        }
    }
}