# AI Agent Backend

A comprehensive AI-powered assistant that integrates WhatsApp, Google Calendar, Gmail, and Trello into a single intelligent workflow.

## Features

- **WhatsApp Integration**: Natural language commands for scheduling, reminders, and task management
- **Google Calendar Integration**: Automatic event creation, updates, and conflict resolution
- **Gmail Integration**: Email processing, smart replies, and urgent email detection
- **Trello Integration**: Task management with automatic card creation and status updates
- **AI Capabilities**: Natural language processing with OpenAI integration
- **Real-time Updates**: SignalR for live dashboard updates
- **Background Processing**: Scheduled jobs for reminders and synchronization

## Prerequisites

- .NET 8.0 SDK
- Node.js 18+ (for WhatsApp bot)
- MySQL 8.0+
- Redis (for caching and SignalR backplane)

## Quick Start

### 1. Clone the Repository

```bash
git clone https://github.com/your-username/ai-agent-backend.git
cd ai-agent-backend
  
2. Configure Environment Variables
Create appsettings.Development.json:

{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Port=3307;Database=AiAgentDb;User=root;Password=Jeevi@123;"
  },
  "Jwt": {
    "Issuer": "AiAgent",
    "Audience": "AiAgentAudience",
    "Key": "your-super-secret-jwt-key-at-least-32-characters",
    "AccessTokenMinutes": 120
  },
  "OpenAI": {
    "ApiKey": "your-openai-api-key",
    "Model": "gpt-4o-mini"
  },
  "Google": {
    "ClientId": "your-google-client-id",
    "ClientSecret": "your-google-client-secret",
    "RedirectUri": "https://localhost:5001/api/google/callback"
  },
  "Trello": {
    "ApiKey": "your-trello-api-key",
    "ApiToken": "your-trello-api-token",
    "DefaultBoardId": "your-trello-board-id"
  },
  "WhatsApp": {
    "BotPath": "./whatsapp-bot",
    "BotPort": "3001",
    "BotApiUrl": "http://localhost:3001"
  },
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "Username": "your-email@gmail.com",
    "Password": "your-app-password"
  }
}

3. Setup Database

# Run MySQL container
docker-compose up mysql -d

# Apply migrations
dotnet ef database update

4. Start Services

# Start backend
dotnet run

# Start WhatsApp bot (in separate terminal)
cd whatsapp-bot
npm install
npm start

5. Using Docker Compose (Recommended)

# Start all services
docker-compose up -d

# View logs
docker-compose logs -f


API Documentation
Once running, access the Swagger documentation at:

https://localhost:5001/swagger (Development)

http://localhost:5000/swagger (Production)

Key Endpoints
Authentication
POST /api/Auth/register - User registration

POST /api/Auth/login - User login

POST /api/Auth/refresh - Refresh JWT token

POST /api/Auth/forgot-password - Password reset request

POST /api/Auth/reset-password - Password reset

Events
GET /api/Events - List events

POST /api/Events - Create event

PATCH /api/Events/{id} - Update event

DELETE /api/Events/{id} - Delete event

Tasks
GET /api/Tasks - List tasks

POST /api/Tasks - Create task

PATCH /api/Tasks/{id} - Update task

DELETE /api/Tasks/{id} - Delete task

Messages
POST /api/Messages/send - Send message to AI

GET /api/Messages/history - Get chat history

Integrations
GET /api/google/connect - Connect Google account

GET /api/google/callback - Google OAuth callback

GET /api/google/status - Check Google connection status

Webhooks
POST /webhooks/whatsapp - WhatsApp incoming messages

POST /webhooks/google - Google Calendar updates

POST /webhooks/trello - Trello card updates

WhatsApp Commands
The AI agent understands natural language commands like:

"Schedule a meeting tomorrow at 3 PM"

"Remind me to send the report on Friday"

"Create a Trello card for project tasks"

"What's on my calendar today?"

"Show me my pending tasks"

Architecture


Frontend → API Gateway → Backend Services → External APIs
    │          │              │
    │          │              ├── WhatsApp Service → Baileys WhatsApp Bot
    │          │              ├── Google Service → Calendar & Gmail APIs
    │          │              ├── Trello Service → Trello API
    │          │              └── AI Service → OpenAI API
    │          │
    │          └── Database (MySQL) ↔️ Cache (Redis)
    │
    └── Real-time Updates (SignalR)


Development
Project Structure

AiAgentBackend/
├── Controllers/          # API controllers
├── Services/            # Business logic services
│   ├── Integrations/    # External service integrations
│   ├── Auth/           # Authentication services
│   ├── NLP/            # Natural language processing
│   └── Orchestration/  # Command orchestration
├── Models/             # Data models
├── DTOs/              # Data transfer objects
├── Configuration/      # Configuration classes
├── Hub/               # SignalR hubs
├── Jobs/              # Background jobs
├── Utils/             # Utility classes
├── Middleware/        # Custom middleware
└── Data/              # Database context