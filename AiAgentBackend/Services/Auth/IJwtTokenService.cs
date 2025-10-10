using AiAgentBackend.Models;

namespace AiAgentBackend.Services.Auth
{
    public interface IJwtTokenService
    {
        string CreateToken(User user);
        string HashPassword(string password);
        bool VerifyPassword(string password, string hash);
    }
}
