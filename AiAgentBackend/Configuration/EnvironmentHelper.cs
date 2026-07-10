// Configuration/EnvironmentHelper.cs
using Microsoft.Extensions.Configuration;

namespace AiAgentBackend.Configuration
{
    public static class EnvironmentHelper
    {
        public static bool IsDevelopment => 
            (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty) == "Development";
            
        public static bool IsProduction => 
            (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty) == "Production";
            
        public static bool IsStaging => 
            (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty) == "Staging";

        public static string GetConnectionString(IConfiguration configuration)
        {
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
            return env switch
            {
                "Production" => configuration.GetConnectionString("ProductionConnection") 
                             ?? configuration.GetConnectionString("DefaultConnection")
                             ?? "Server=127.0.0.1;Port=3306;Database=AiAgentDb;User=root;Password=;",
                _ => configuration.GetConnectionString("DefaultConnection")
                             ?? "Server=127.0.0.1;Port=3306;Database=AiAgentDb;User=root;Password=;"
            };
        }

        public static string GetBaseUrl(IConfiguration configuration)
        {
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
            return env switch
            {
                "Production" => configuration["AiAgent:BaseUrl"] ?? "https://yourappdomain.com",
                _ => configuration["AiAgent:BaseUrl"] ?? "http://localhost:5000"
            };
        }
    }
}