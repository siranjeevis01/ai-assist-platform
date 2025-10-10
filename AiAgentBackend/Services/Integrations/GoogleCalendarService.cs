using AiEvent = AiAgentBackend.Models.Event;
using GEvent = Google.Apis.Calendar.v3.Data.Event;

using AiAgentBackend.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using AiAgentBackend.DTOs.Google;

namespace AiAgentBackend.Services.Integrations
{

    public interface IGoogleCalendarService
    {
        Task<AiEvent> CreateEventAsync(int userId, AiEvent e);
        Task<AiEvent> UpdateEventAsync(int userId, AiEvent e);
        Task DeleteEventAsync(int userId, string externalId);
        Task<AiEvent?> GetEventAsync(int userId, string externalId);
        Task<List<AiEvent>> ListEventsAsync(int userId, DateTime fromUtc, DateTime toUtc);
        Task<List<DateTime>> SuggestAlternatesAsync(int userId, DateTime desiredStart, TimeSpan duration);
        Task SyncAllUserCalendars();
    }
    
    public class GoogleCalendarService : IGoogleCalendarService
    {
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<GoogleCalendarService> _logger;

        public GoogleCalendarService(ApplicationDbContext db, IConfiguration config, ILogger<GoogleCalendarService> logger)
        {
            _db = db;
            _config = config;
            _logger = logger;
        }

        private async Task<CalendarService> GetCalendarServiceAsync(int userId)
        {
            var token = await _db.ProviderTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Google");

            if (token == null)
                throw new Exception("User has not connected Google Calendar");

            // Refresh token if expired or about to expire
            if (!token.ExpiresAt.HasValue || token.ExpiresAt <= DateTime.UtcNow.AddMinutes(5))
            {
                _logger.LogInformation("Refreshing Google access token for user {UserId}", userId);
                
                using var http = new HttpClient();
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "client_id", _config["Google:ClientId"]! },
                    { "client_secret", _config["Google:ClientSecret"]! },
                    { "refresh_token", token.RefreshToken! },
                    { "grant_type", "refresh_token" }
                });

                var resp = await http.PostAsync("https://oauth2.googleapis.com/token", content);
                if (!resp.IsSuccessStatusCode)
                {
                    var error = await resp.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to refresh Google token: {Error}", error);
                    throw new Exception($"Failed to refresh token: {resp.StatusCode}");
                }

                var json = await resp.Content.ReadFromJsonAsync<GoogleTokenResponse>();
                if (json == null)
                    throw new Exception("Invalid token response from Google");

                token.EncryptedAccessToken = json.AccessToken;
                token.ExpiresAt = DateTime.UtcNow.AddSeconds(json.ExpiresIn);
                await _db.SaveChangesAsync();
            }

            if (string.IsNullOrEmpty(token.RefreshToken))
                throw new Exception("Google refresh token is missing.");

            var credential = GoogleCredential.FromAccessToken(token.EncryptedAccessToken ?? throw new Exception("Access token is null"));

            return new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AiAgent"
            });
        }

        public async Task<AiEvent> CreateEventAsync(int userId, AiEvent e)
        {
            try
            {
            var service = await GetCalendarServiceAsync(userId);

            var googleEvent = new GEvent
            {
                Summary = e.Title,
                Location = e.Location,
                Description = e.Description,
                Start = new EventDateTime { DateTimeDateTimeOffset = e.StartUtc, TimeZone = "UTC" },
                End   = new EventDateTime { DateTimeDateTimeOffset = e.EndUtc,   TimeZone = "UTC" }
            };

            // Add attendees if any
            if (!string.IsNullOrEmpty(e.AttendeesJson))
            {
                var attendees = JsonSerializer.Deserialize<List<string>>(e.AttendeesJson) ?? new List<string>();
                googleEvent.Attendees = attendees.Select(email => new EventAttendee { Email = email }).ToList();
            }

            var created = await service.Events.Insert(googleEvent, "primary").ExecuteAsync();
            e.ExternalId = created.Id;

            // Add conference data for online meetings
            if (e.Location?.Contains("online") == true || e.Location?.Contains("meet") == true)
            {
                var updateRequest = service.Events.Get("primary", e.ExternalId);
                var existingEvent = await updateRequest.ExecuteAsync();
                
                existingEvent.ConferenceData = new ConferenceData
                {
                    CreateRequest = new CreateConferenceRequest
                    {
                        RequestId = Guid.NewGuid().ToString(),
                        ConferenceSolutionKey = new ConferenceSolutionKey { Type = "hangoutsMeet" }
                    }
                };
                
                await service.Events.Update(existingEvent, "primary", e.ExternalId).ExecuteAsync();
            }

            _logger.LogInformation("Created Google Calendar event {EventId} for user {UserId}", e.ExternalId, userId);
            return e;
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "Google API error creating event for user {UserId}: {Error}", userId, ex.Error.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating Google event for user {UserId}", userId);
                throw;
            }            
        }

        public async Task<AiEvent> UpdateEventAsync(int userId, AiEvent e)
        {
            var service = await GetCalendarServiceAsync(userId);
            
            try
            {
                if (string.IsNullOrEmpty(e.ExternalId))
                    throw new Exception("Event ExternalId is null.");
                var ge = await service.Events.Get("primary", e.ExternalId).ExecuteAsync();

                ge.Summary = e.Title;
                ge.Location = e.Location;
                ge.Description = e.Description;
                ge.Start.DateTimeDateTimeOffset = e.StartUtc;
                ge.End.DateTimeDateTimeOffset   = e.EndUtc;

                if (!string.IsNullOrEmpty(e.AttendeesJson))
                {
                    var attendees = JsonSerializer.Deserialize<List<string>>(e.AttendeesJson);
                    ge.Attendees = attendees?.Select(email => new EventAttendee { Email = email }).ToList() ?? new List<EventAttendee>();
                }

                await service.Events.Update(ge, "primary", ge.Id).ExecuteAsync();
                
                _logger.LogInformation("Updated Google Calendar event {EventId} for user {UserId}", e.ExternalId, userId);
                return e;
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Event {EventId} not found in Google Calendar, creating new one", e.ExternalId);
                return await CreateEventAsync(userId, e);
            }
        }

        public async Task DeleteEventAsync(int userId, string externalId)
        {
            var service = await GetCalendarServiceAsync(userId);
            try
            {
                await service.Events.Delete("primary", externalId).ExecuteAsync();
                _logger.LogInformation("Deleted Google Calendar event {EventId} for user {UserId}", externalId, userId);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Event {EventId} not found in Google Calendar during deletion", externalId);
            }
        }

        public async Task<AiEvent?> GetEventAsync(int userId, string externalId)
        {
            var service = await GetCalendarServiceAsync(userId);
            try
            {
                var ge = await service.Events.Get("primary", externalId).ExecuteAsync();
                
                return new AiEvent
                {
                    Title = ge.Summary,
                    Location = ge.Location,
                    Description = ge.Description,
                    StartUtc = ge.Start?.DateTimeDateTimeOffset ?? DateTimeOffset.MinValue,
                    EndUtc = ge.End?.DateTimeDateTimeOffset ?? DateTimeOffset.MinValue,
                    ExternalId = ge.Id,
                    AttendeesJson = ge.Attendees != null ? 
                        JsonSerializer.Serialize(ge.Attendees.Select(a => a.Email)) : null,
                    Status = ge.Status ?? "Scheduled"
                };
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<List<AiEvent>> ListEventsAsync(int userId, DateTime fromUtc, DateTime toUtc)
        {
            var service = await GetCalendarServiceAsync(userId);
            var request = service.Events.List("primary");

            request.TimeMinDateTimeOffset = fromUtc;
            request.TimeMaxDateTimeOffset = toUtc;
            request.ShowDeleted = false;
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

            var result = await request.ExecuteAsync();

            return result.Items.Select(ge => new AiEvent
            {
                Title = ge.Summary,
                Location = ge.Location,
                Description = ge.Description,
                StartUtc = ge.Start?.DateTimeDateTimeOffset ?? DateTimeOffset.MinValue,
                EndUtc = ge.End?.DateTimeDateTimeOffset ?? DateTimeOffset.MinValue,
                ExternalId = ge.Id,
                AttendeesJson = ge.Attendees != null ? 
                    JsonSerializer.Serialize(ge.Attendees.Select(a => a.Email)) : null,
                Status = ge.Status ?? "Scheduled"
            }).ToList();
        }

        public async Task<List<DateTime>> SuggestAlternatesAsync(int userId, DateTime desiredStart, TimeSpan duration)
        {
            var service = await GetCalendarServiceAsync(userId);
            
            // Check for free/busy information
            var freeBusyRequest = new FreeBusyRequest
            {
                TimeMinDateTimeOffset = desiredStart.AddHours(-2),
                TimeMaxDateTimeOffset = desiredStart.AddHours(4),
                Items = new List<FreeBusyRequestItem> { 
                    new FreeBusyRequestItem { Id = "primary" } 
                }
            };
            
            var freeBusyResponse = await service.Freebusy.Query(freeBusyRequest).ExecuteAsync();
            var calendar = freeBusyResponse.Calendars["primary"];
            
            if (calendar.Busy == null || !calendar.Busy.Any())
            {
                return new List<DateTime> { desiredStart };
            }

            var alternates = new List<DateTime>();
            var checkTime = desiredStart;
            var endTime = desiredStart.Add(duration);

            // Check the next 5 possible slots
            for (int i = 0; i < 5; i++)
            {
                var isAvailable = true;
                
                foreach (var busyPeriod in calendar.Busy)
                {
                    var busyStart = busyPeriod.StartDateTimeOffset ?? DateTimeOffset.MinValue;
                    var busyEnd = busyPeriod.EndDateTimeOffset ?? DateTimeOffset.MinValue;
                    
                    if (checkTime < busyEnd && endTime > busyStart)
                    {
                        isAvailable = false;
                        break;
                    }
                }
                
                if (isAvailable)
                {
                    alternates.Add(checkTime);
                }
                
                checkTime = checkTime.AddMinutes(30);
                endTime = endTime.AddMinutes(30);
            }

            return alternates;
        }

public async Task SyncUserCalendarEvents(int userId, DateTime fromUtc, DateTime toUtc)
{
    try
    {
        var service = await GetCalendarServiceAsync(userId);
        var googleEvents = await ListEventsAsync(userId, fromUtc, toUtc);
        
        var userExists = await _db.Users.AnyAsync(u => u.Id == userId);
        if (!userExists)
        {
            _logger.LogWarning("User {UserId} does not exist, skipping calendar sync", userId);
            return;
        }
        
        var localEvents = await _db.Events
            .Where(e => e.UserId == userId && 
                       e.StartUtc >= fromUtc && 
                       e.StartUtc <= toUtc)
            .ToListAsync();

        foreach (var googleEvent in googleEvents)
        {
            googleEvent.UserId = userId;
            
            var localEvent = localEvents.FirstOrDefault(e => e.ExternalId == googleEvent.ExternalId);
            
            if (localEvent == null)
            {
                _db.Events.Add(googleEvent);
            }
            else
            {
                localEvent.Title = googleEvent.Title;
                localEvent.StartUtc = googleEvent.StartUtc;
                localEvent.EndUtc = googleEvent.EndUtc;
                localEvent.Location = googleEvent.Location;
                localEvent.Description = googleEvent.Description;
                localEvent.AttendeesJson = googleEvent.AttendeesJson;
                
                _db.Events.Update(localEvent);
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Synced Google Calendar events for user {UserId}", userId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to sync calendar for user {UserId}", userId);
        throw;
    }
}

        public async Task SyncAllUserCalendars()
        {
            try
            {
                var usersWithGoogle = await _db.ProviderTokens
                    .Where(t => t.Provider == "Google")
                    .Select(t => t.UserId)
                    .Distinct()
                    .ToListAsync();

                _logger.LogInformation("Syncing Google Calendar for {Count} users", usersWithGoogle.Count);

                var fromDate = DateTime.UtcNow.AddDays(-7);
                var toDate = DateTime.UtcNow.AddDays(30);

                foreach (var userId in usersWithGoogle)
                {
                    try
                    {
                        await SyncUserCalendarEvents(userId, fromDate, toDate);
                        await Task.Delay(1000); // Rate limiting
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to sync calendar for user {UserId}", userId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync all user calendars");
            }
        }
    }
}