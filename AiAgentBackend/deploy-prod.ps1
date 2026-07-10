#!/usr/bin/env pwsh

# Production Deployment Script for AI Agent Backend

param(
    [string]$Environment = "Production",
    [string]$Version = "1.0.0"
)

Write-Host "🚀 Starting deployment of AI Agent Backend v$Version to $Environment" -ForegroundColor Green

# Step 1: Build the application
Write-Host "📦 Building application..." -ForegroundColor Yellow
dotnet clean
dotnet build --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

# Step 2: Run tests
Write-Host "🧪 Running tests..." -ForegroundColor Yellow
dotnet test

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Tests failed!" -ForegroundColor Red
    exit 1
}

# Step 3: Publish
Write-Host "📤 Publishing application..." -ForegroundColor Yellow
dotnet publish --configuration Release --output ./publish --runtime linux-x64 --self-contained false

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Publish failed!" -ForegroundColor Red
    exit 1
}

# Step 4: Create deployment package
Write-Host "📁 Creating deployment package..." -ForegroundColor Yellow
Compress-Archive -Path "./publish/*" -DestinationPath "./deployment-$Version.zip" -Force

# Step 5: Environment setup
Write-Host "🔧 Setting environment variables..." -ForegroundColor Yellow
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:DOTNET_ENVIRONMENT = "Production"

Write-Host "✅ Deployment package ready: deployment-$Version.zip" -ForegroundColor Green
Write-Host "📋 Next steps:" -ForegroundColor Cyan
Write-Host "   1. Upload deployment-$Version.zip to your server" -ForegroundColor White
Write-Host "   2. Extract to deployment directory" -ForegroundColor White
Write-Host "   3. Run: dotnet AiAgentBackend.dll" -ForegroundColor White
Write-Host "   4. Or use: ./AiAgentBackend" -ForegroundColor White