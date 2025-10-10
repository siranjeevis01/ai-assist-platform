// Services/Integrations/ConversationStateService.cs
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace AiAgentBackend.Services.Integrations
{
    public interface IConversationStateService
    {
        Task<ConversationState> GetCurrentStateAsync(int userId); 
        Task UpdateStateAsync(int userId, string intent, string currentStep, string contextData);
        Task ClearStateAsync(int userId);
        Task<string> GetContextDataAsync(int userId);
    }

    public class ConversationStateService : IConversationStateService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<ConversationStateService> _logger;

        public ConversationStateService(ApplicationDbContext db, ILogger<ConversationStateService> logger)
        {
            _db = db;
            _logger = logger;
        }

public async Task<ConversationState> GetCurrentStateAsync(int userId)
{
    var state = await _db.ConversationStates
        .FirstOrDefaultAsync(cs => cs.UserId == userId && cs.ExpiresAt > DateTime.UtcNow);

    return state ?? new ConversationState
    {
        UserId = userId,
        Intent = string.Empty,
        CurrentStep = string.Empty,
        ContextData = string.Empty,
        LastUpdated = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow
    };
}

        public async Task UpdateStateAsync(int userId, string intent, string currentStep, string contextData)
        {
            var existingState = await GetCurrentStateAsync(userId);
            
            if (existingState == null)
            {
                existingState = new ConversationState
                {
                    UserId = userId,
                    Intent = intent,
                    CurrentStep = currentStep,
                    ContextData = contextData,
                    LastUpdated = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                };
                _db.ConversationStates.Add(existingState);
            }
            else
            {
                existingState.Intent = intent;
                existingState.CurrentStep = currentStep;
                existingState.ContextData = contextData;
                existingState.LastUpdated = DateTime.UtcNow;
                existingState.ExpiresAt = DateTime.UtcNow.AddHours(1);
                _db.ConversationStates.Update(existingState);
            }

            await _db.SaveChangesAsync();
        }

        public async Task ClearStateAsync(int userId)
        {
            var states = await _db.ConversationStates
                .Where(cs => cs.UserId == userId)
                .ToListAsync();

            _db.ConversationStates.RemoveRange(states);
            await _db.SaveChangesAsync();
        }

        public async Task<string> GetContextDataAsync(int userId)
        {
            var state = await GetCurrentStateAsync(userId);
            return state?.ContextData ?? string.Empty;
        }
    }
}