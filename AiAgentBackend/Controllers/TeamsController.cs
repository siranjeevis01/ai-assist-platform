using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.Services.RBAC;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TeamsController : ControllerBase
    {
        private readonly ITeamService _teams;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<TeamsController> _logger;

        public TeamsController(ITeamService teams, ApplicationDbContext db, ILogger<TeamsController> logger)
        {
            _teams = teams;
            _db = db;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetTeams()
        {
            var userId = GetUserId();
            var teams = await _teams.GetUserTeamsAsync(userId);
            return Ok(teams);
        }

        [HttpPost]
        public async Task<IActionResult> CreateTeam([FromBody] CreateTeamRequest request)
        {
            var userId = GetUserId();
            var team = await _teams.CreateTeamAsync(userId, request.Name, request.Description);
            return Ok(team);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTeam(int id)
        {
            var userId = GetUserId();
            var team = await _teams.GetTeamAsync(userId, id);
            if (team == null) return NotFound();
            return Ok(team);
        }

        [HttpPost("{id}/members")]
        public async Task<IActionResult> AddMember(int id, [FromBody] AddMemberRequest request)
        {
            var userId = GetUserId();
            var targetUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (targetUser == null) return BadRequest(new { error = "User not found" });

            var result = await _teams.AddMemberAsync(userId, id, targetUser.Id, request.Role);
            if (!result) return BadRequest(new { error = "Failed to add member" });
            return Ok(new { message = "Member added" });
        }

        [HttpDelete("{id}/members/{userId}")]
        public async Task<IActionResult> RemoveMember(int id, int userId)
        {
            var currentUserId = GetUserId();
            var result = await _teams.RemoveMemberAsync(currentUserId, id, userId);
            if (!result) return BadRequest(new { error = "Failed to remove member" });
            return Ok(new { message = "Member removed" });
        }

        [HttpPatch("{id}/members/{userId}/role")]
        public async Task<IActionResult> UpdateRole(int id, int userId, [FromBody] UpdateRoleRequest request)
        {
            var currentUserId = GetUserId();
            var result = await _teams.UpdateMemberRoleAsync(currentUserId, id, userId, request.Role);
            if (!result) return BadRequest(new { error = "Failed to update role" });
            return Ok(new { message = "Role updated" });
        }

        [HttpGet("{id}/members")]
        public async Task<IActionResult> GetMembers(int id)
        {
            var userId = GetUserId();
            var members = await _teams.GetTeamMembersAsync(userId, id);
            return Ok(members);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTeam(int id)
        {
            var userId = GetUserId();
            var result = await _teams.DeleteTeamAsync(userId, id);
            if (!result) return BadRequest(new { error = "Failed to delete team" });
            return Ok(new { message = "Team deleted" });
        }

        private int GetUserId() => int.Parse(User.FindFirst("uid")?.Value ?? User.FindFirst("sub")?.Value ?? "0");
    }

    public class CreateTeamRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class AddMemberRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "Member";
    }

    public class UpdateRoleRequest
    {
        public string Role { get; set; } = string.Empty;
    }
}
