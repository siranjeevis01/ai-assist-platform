using AiAgentBackend.Data;
using AiAgentBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace AiAgentBackend.Services.RBAC
{
    public interface ITeamService
    {
        Task<Team> CreateTeamAsync(int ownerId, string name, string? description);
        Task<List<Team>> GetUserTeamsAsync(int userId);
        Task<Team?> GetTeamAsync(int userId, int teamId);
        Task<bool> AddMemberAsync(int ownerId, int teamId, int userId, string role);
        Task<bool> RemoveMemberAsync(int ownerId, int teamId, int userId);
        Task<bool> UpdateMemberRoleAsync(int ownerId, int teamId, int userId, string role);
        Task<List<TeamMember>> GetTeamMembersAsync(int userId, int teamId);
        Task<bool> DeleteTeamAsync(int ownerId, int teamId);
        Task<string> GetUserRoleAsync(int userId, int teamId);
        Task<bool> HasPermissionAsync(int userId, int teamId, string permission);
    }

    public class TeamService : ITeamService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<TeamService> _logger;

        private static readonly Dictionary<string, List<string>> _rolePermissions = new()
        {
            ["Owner"] = new() { "manage_team", "delete_team", "manage_members", "manage_rules", "view", "edit", "create", "delete" },
            ["Admin"] = new() { "manage_members", "manage_rules", "view", "edit", "create", "delete" },
            ["Member"] = new() { "view", "edit", "create" },
            ["Viewer"] = new() { "view" }
        };

        public TeamService(ApplicationDbContext db, ILogger<TeamService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Team> CreateTeamAsync(int ownerId, string name, string? description)
        {
            var team = new Team
            {
                Name = name,
                Description = description,
                OwnerId = ownerId,
                CreatedAt = DateTime.UtcNow
            };

            _db.Teams.Add(team);
            await _db.SaveChangesAsync();

            var ownerMember = new TeamMember
            {
                TeamId = team.Id,
                UserId = ownerId,
                Role = "Owner",
                JoinedAt = DateTime.UtcNow
            };

            _db.TeamMembers.Add(ownerMember);
            await _db.SaveChangesAsync();

            return team;
        }

        public async Task<List<Team>> GetUserTeamsAsync(int userId)
        {
            return await _db.TeamMembers
                .Include(tm => tm.Team)
                .Where(tm => tm.UserId == userId)
                .Select(tm => tm.Team)
                .ToListAsync();
        }

        public async Task<Team?> GetTeamAsync(int userId, int teamId)
        {
            var isMember = await _db.TeamMembers
                .AnyAsync(tm => tm.TeamId == teamId && tm.UserId == userId);
            if (!isMember) return null;

            return await _db.Teams
                .Include(t => t.Members)
                .ThenInclude(m => m.User)
                .FirstOrDefaultAsync(t => t.Id == teamId);
        }

        public async Task<bool> AddMemberAsync(int ownerId, int teamId, int userId, string role)
        {
            if (!await HasPermissionAsync(ownerId, teamId, "manage_members"))
                return false;

            var existing = await _db.TeamMembers
                .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.UserId == userId);
            if (existing != null) return false;

            var member = new TeamMember
            {
                TeamId = teamId,
                UserId = userId,
                Role = role,
                JoinedAt = DateTime.UtcNow
            };

            _db.TeamMembers.Add(member);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RemoveMemberAsync(int ownerId, int teamId, int userId)
        {
            if (!await HasPermissionAsync(ownerId, teamId, "manage_members"))
                return false;

            var member = await _db.TeamMembers
                .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.UserId == userId);
            if (member == null || member.Role == "Owner") return false;

            _db.TeamMembers.Remove(member);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateMemberRoleAsync(int ownerId, int teamId, int userId, string role)
        {
            if (!await HasPermissionAsync(ownerId, teamId, "manage_members"))
                return false;

            var member = await _db.TeamMembers
                .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.UserId == userId);
            if (member == null || member.Role == "Owner") return false;

            member.Role = role;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<List<TeamMember>> GetTeamMembersAsync(int userId, int teamId)
        {
            if (!await HasPermissionAsync(userId, teamId, "view"))
                return new List<TeamMember>();

            return await _db.TeamMembers
                .Include(tm => tm.User)
                .Where(tm => tm.TeamId == teamId)
                .ToListAsync();
        }

        public async Task<bool> DeleteTeamAsync(int ownerId, int teamId)
        {
            if (!await HasPermissionAsync(ownerId, teamId, "delete_team"))
                return false;

            var team = await _db.Teams.FindAsync(teamId);
            if (team == null || team.OwnerId != ownerId) return false;

            _db.Teams.Remove(team);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<string> GetUserRoleAsync(int userId, int teamId)
        {
            var member = await _db.TeamMembers
                .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.UserId == userId);
            return member?.Role ?? "None";
        }

        public async Task<bool> HasPermissionAsync(int userId, int teamId, string permission)
        {
            var role = await GetUserRoleAsync(userId, teamId);
            if (role == "None") return false;
            return _rolePermissions.TryGetValue(role, out var perms) && perms.Contains(permission);
        }
    }
}
