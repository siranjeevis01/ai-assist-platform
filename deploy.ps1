# AI Agent Deployment Script
param(
    [switch]$Build,
    [switch]$Up,
    [switch]$Down,
    [switch]$Logs,
    [switch]$ProdBuild
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Show-Usage {
    Write-Host @"
AI Agent Deployment Script
Usage: .\deploy.ps1 [options]

Options:
  -Build     Build Docker images (docker-compose build)
  -Up        Start all services (docker-compose up -d)
  -Down      Stop all services (docker-compose down)
  -Logs      View service logs (docker-compose logs -f)
  -ProdBuild Build Angular for production + .NET

Examples:
  .\deploy.ps1 -Build -Up        # Build and start
  .\deploy.ps1 -Up               # Start existing containers
  .\deploy.ps1 -Down             # Stop everything
  .\deploy.ps1 -Logs             # Watch logs
"@
}

if ($ProdBuild) {
    Write-Host "🏗️  Building Angular frontend for production..." -ForegroundColor Yellow
    
    Push-Location "$root\ai-agent-frontend"
    $env:NODE_OPTIONS = "--max-old-space-size=2048"
    npm ci --legacy-peer-deps
    npm run build
    Pop-Location
    
    Write-Host "🏗️  Building .NET backend..." -ForegroundColor Yellow
    Push-Location "$root\AiAgentBackend"
    dotnet build -c Release
    Pop-Location
    
    Write-Host "✅ Production build complete!" -ForegroundColor Green
}

if ($Build) {
    Write-Host "🐳 Building Docker images..." -ForegroundColor Yellow
    Push-Location $root
    docker-compose build
    Pop-Location
}

if ($Up) {
    Write-Host "🚀 Starting AI Agent services..." -ForegroundColor Green
    Push-Location $root
    
    # Check if .env exists
    if (-not (Test-Path ".env")) {
        Write-Host "⚠️  .env file not found! Copying from .env.example..." -ForegroundColor Yellow
        Copy-Item ".env.example" ".env"
        Write-Host "⚠️  Please edit .env with your actual credentials!" -ForegroundColor Red
    }
    
    docker-compose up -d
    
    Write-Host @"
========================================
  AI Agent - Running
========================================
  App:      http://localhost:${env:FRONTEND_PORT:-80}
  API:      http://localhost:${env:BACKEND_PORT:-5000}
  Swagger:  http://localhost:${env:BACKEND_PORT:-5000}/swagger
  Health:   http://localhost:${env:BACKEND_PORT:-5000}/health/real-time
========================================
"@ -ForegroundColor Cyan
    
    Pop-Location
}

if ($Down) {
    Write-Host "🛑 Stopping AI Agent services..." -ForegroundColor Yellow
    Push-Location $root
    docker-compose down
    Pop-Location
    Write-Host "✅ All services stopped" -ForegroundColor Green
}

if ($Logs) {
    Push-Location $root
    docker-compose logs -f
    Pop-Location
}

if (-not $Build -and -not $Up -and -not $Down -and -not $Logs -and -not $ProdBuild) {
    Show-Usage
}
