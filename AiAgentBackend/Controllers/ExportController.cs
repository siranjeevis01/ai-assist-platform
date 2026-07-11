using AiAgentBackend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ExportController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<ExportController> _logger;

        public ExportController(ApplicationDbContext db, ILogger<ExportController> logger)
        {
            _db = db;
            _logger = logger;
        }

        private int GetUserId()
        {
            var userIdStr = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }

        [HttpGet("tasks")]
        public async Task<IActionResult> ExportTasks([FromQuery] string format = "json")
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            var tasks = await _db.Tasks
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.Status,
                    t.Description,
                    t.DueUtc,
                    t.CreatedAt,
                    t.CompletedAt,
                    t.RecurrenceRule,
                    Labels = t.LabelsJson
                })
                .ToListAsync();

            if (format.ToLower() == "csv")
            {
                var csv = new StringBuilder();
                csv.AppendLine("Id,Title,Status,Description,DueUtc,CreatedAt,CompletedAt,RecurrenceRule,Labels");
                foreach (var t in tasks)
                {
                    csv.AppendLine($"{t.Id},\"{Escape(t.Title)}\",\"{Escape(t.Status)}\",\"{Escape(t.Description ?? "")}\",{t.DueUtc},{t.CreatedAt},{t.CompletedAt},{t.RecurrenceRule},\"{Escape(t.Labels ?? "")}\"");
                }
                return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"tasks_{DateTime.UtcNow:yyyyMMdd}.csv");
            }

            return Ok(tasks);
        }

        [HttpGet("events")]
        public async Task<IActionResult> ExportEvents([FromQuery] string format = "json")
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            var events = await _db.Events
                .Where(e => e.UserId == userId)
                .OrderByDescending(e => e.StartUtc)
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    e.Description,
                    e.Location,
                    e.StartUtc,
                    e.EndUtc,
                    e.Status,
                    e.Source,
                    Attendees = e.AttendeesJson
                })
                .ToListAsync();

            if (format.ToLower() == "csv")
            {
                var csv = new StringBuilder();
                csv.AppendLine("Id,Title,Description,Location,StartUtc,EndUtc,Status,Source,Attendees");
                foreach (var e in events)
                {
                    csv.AppendLine($"{e.Id},\"{Escape(e.Title)}\",\"{Escape(e.Description ?? "")}\",\"{Escape(e.Location ?? "")}\",{e.StartUtc},{e.EndUtc},{e.Status},{e.Source},\"{Escape(e.Attendees ?? "")}\"");
                }
                return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"events_{DateTime.UtcNow:yyyyMMdd}.csv");
            }

            return Ok(events);
        }

        [HttpGet("all")]
        public async Task<IActionResult> ExportAll([FromQuery] string format = "json")
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            var tasks = await _db.Tasks.Where(t => t.UserId == userId).OrderByDescending(t => t.CreatedAt).ToListAsync();
            var events = await _db.Events.Where(e => e.UserId == userId).OrderByDescending(e => e.StartUtc).ToListAsync();

            if (format.ToLower() == "csv")
            {
                var tasksCsv = new StringBuilder();
                tasksCsv.AppendLine("--- TASKS ---");
                tasksCsv.AppendLine("Id,Title,Status,Description,DueUtc,CreatedAt,CompletedAt");
                foreach (var t in tasks)
                    tasksCsv.AppendLine($"{t.Id},\"{Escape(t.Title)}\",\"{Escape(t.Status)}\",\"{Escape(t.Description ?? "")}\",{t.DueUtc},{t.CreatedAt},{t.CompletedAt}");

                tasksCsv.AppendLine();
                tasksCsv.AppendLine("--- EVENTS ---");
                tasksCsv.AppendLine("Id,Title,Description,Location,StartUtc,EndUtc,Status");
                foreach (var e in events)
                    tasksCsv.AppendLine($"{e.Id},\"{Escape(e.Title)}\",\"{Escape(e.Description ?? "")}\",\"{Escape(e.Location ?? "")}\",{e.StartUtc},{e.EndUtc},{e.Status}");

                return File(Encoding.UTF8.GetBytes(tasksCsv.ToString()), "text/csv", $"export_{DateTime.UtcNow:yyyyMMdd}.csv");
            }

            return Ok(new { tasks, events, exportedAt = DateTime.UtcNow });
        }

        private static string Escape(string s) => s.Replace("\"", "\"\"");
    }
}
