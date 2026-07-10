#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Sets up ngrok tunnel and configures Telegram/WhatsApp webhooks for local development.
.DESCRIPTION
    This script:
    1. Starts ngrok on port 5000 to get a public HTTPS URL
    2. Configures Telegram bot webhook to point to the ngrok URL
    3. Updates appsettings.Development.json with the ngrok URL

    Prerequisites:
    - ngrok installed (https://ngrok.com/download)
    - Backend running on port 5000
    - Telegram bot token configured in appsettings.json
.PARAMETER NgrokPath
    Path to ngrok executable. Default: "ngrok"
.PARAMETER Region
    ngrok region (us, eu, au, ap, sa, jp, in). Default: "us"
#>

param(
    [string]$NgrokPath = "ngrok",
    [string]$Region = "us"
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendDir = "$root\AiAgentBackend"
$settingsFile = "$backendDir\appsettings.json"
$devSettingsFile = "$backendDir\appsettings.Development.json"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  AI Agent - Webhook Setup" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# Step 1: Check if backend is running
Write-Host "`n🔍 Checking backend status..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5000/health/real-time" -TimeoutSec 3 -ErrorAction Stop
    Write-Host "  ✅ Backend is running" -ForegroundColor Green
}
catch {
    Write-Host "  ❌ Backend is not running. Start it first with: ./run.ps1 -NoFrontend" -ForegroundColor Red
    exit 1
}

# Step 2: Check ngrok
Write-Host "`n🔍 Checking ngrok..." -ForegroundColor Yellow
try {
    $ngrokVersion = & $NgrokPath version 2>&1
    Write-Host "  ✅ ngrok found: $ngrokVersion" -ForegroundColor Green
}
catch {
    Write-Host "  ❌ ngrok not found. Install from https://ngrok.com/download" -ForegroundColor Red
    Write-Host "  Or provide path: -NgrokPath C:\path\to\ngrok.exe" -ForegroundColor Yellow
    exit 1
}

# Step 3: Start ngrok
Write-Host "`n🚀 Starting ngrok tunnel..." -ForegroundColor Yellow
$ngrokJob = Start-Job -ScriptBlock {
    param($exe, $region)
    & $exe http 5000 --region $region
} -ArgumentList $NgrokPath, $Region

Start-Sleep -Seconds 4

# Step 4: Get ngrok public URL
Write-Host "`n🔍 Fetching ngrok public URL..." -ForegroundColor Yellow
try {
    $ngrokApi = Invoke-RestMethod -Uri "http://127.0.0.1:4040/api/tunnels" -TimeoutSec 5
    $publicUrl = $ngrokApi.tunnels | Where-Object { $_.proto -eq "https" } | Select-Object -First 1 -ExpandProperty public_url
    if (-not $publicUrl) {
        $publicUrl = $ngrokApi.tunnels | Select-Object -First 1 -ExpandProperty public_url
    }
    Write-Host "  ✅ Public URL: $publicUrl" -ForegroundColor Green
}
catch {
    Write-Host "  ❌ Could not get ngrok URL. Waiting longer..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5
    try {
        $ngrokApi = Invoke-RestMethod -Uri "http://127.0.0.1:4040/api/tunnels" -TimeoutSec 5
        $publicUrl = $ngrokApi.tunnels | Select-Object -First 1 -ExpandProperty public_url
        Write-Host "  ✅ Public URL: $publicUrl" -ForegroundColor Green
    }
    catch {
        Write-Host "  ❌ Failed to get ngrok URL" -ForegroundColor Red
        exit 1
    }
}

$webhookUrl = "$publicUrl/api/telegram/webhook"

# Step 5: Get bot token from config
Write-Host "`n🔍 Reading Telegram bot token..." -ForegroundColor Yellow
$config = Get-Content $settingsFile -Raw | ConvertFrom-Json
$botToken = $config.Messaging.Telegram.BotToken
$enableMessaging = $config.Messaging.Telegram.Enabled

if (-not $botToken -or $botToken -eq "YOUR_TELEGRAM_BOT_TOKEN") {
    Write-Host "  ⚠️  Bot token not configured in appsettings.json" -ForegroundColor Yellow
    Write-Host "  Using placeholder - webhook setup will be skipped" -ForegroundColor Yellow
}
else {
    # Step 6: Set Telegram webhook
    Write-Host "`n📡 Setting Telegram webhook..." -ForegroundColor Yellow
    $telegramApi = "https://api.telegram.org/bot$botToken/setWebhook"
    try {
        $body = @{ url = $webhookUrl } | ConvertTo-Json
        $result = Invoke-RestMethod -Uri $telegramApi -Method Post -Body $body -ContentType "application/json" -TimeoutSec 10
        if ($result.ok) {
            Write-Host "  ✅ Telegram webhook set to: $webhookUrl" -ForegroundColor Green
        }
        else {
            Write-Host "  ❌ Telegram API error: $($result.description)" -ForegroundColor Red
        }
    }
    catch {
        Write-Host "  ❌ Failed to set webhook: $_" -ForegroundColor Red
    }

    # Step 7: Get webhook info
    Write-Host "`n🔍 Verifying webhook..." -ForegroundColor Yellow
    try {
        $info = Invoke-RestMethod -Uri "https://api.telegram.org/bot$botToken/getWebhookInfo" -TimeoutSec 10
        Write-Host "  ℹ️  Webhook URL: $($info.result.url)" -ForegroundColor White
        Write-Host "  ℹ️  Pending updates: $($info.result.pending_update_count)" -ForegroundColor White
        Write-Host "  ℹ️  Last error: $($info.result.last_error_message)" -ForegroundColor White
    }
    catch {
        Write-Host "  ❌ Failed to get webhook info: $_" -ForegroundColor Red
    }
}

# Step 8: Update Development settings with ngrok URL
Write-Host "`n📝 Updating development settings..." -ForegroundColor Yellow
try {
    $devConfig = Get-Content $devSettingsFile -Raw | ConvertFrom-Json
    $devConfig.Messaging.Telegram.WebhookUrl = "$publicUrl/api/telegram/webhook"
    $devConfig.Messaging.WhatsApp.WebhookUrl = "$publicUrl/api/whatsapp/webhook"
    $devConfig.Google.RedirectUri = "$publicUrl/api/google/callback"
    $devConfig.AiAgent.BaseUrl = $publicUrl
    $devConfig | ConvertTo-Json -Depth 10 | Set-Content $devSettingsFile
    Write-Host "  ✅ Settings updated with ngrok URL" -ForegroundColor Green
}
catch {
    Write-Host "  ❌ Failed to update settings: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Setup Complete!" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Public URL: $publicUrl" -ForegroundColor White
Write-Host "  Backend:    http://localhost:5000" -ForegroundColor White
Write-Host "  Frontend:   http://localhost:4200" -ForegroundColor White
Write-Host "  ngrok UI:   http://127.0.0.1:4040" -ForegroundColor White
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "⚠️  Keep this terminal open to maintain the tunnel" -ForegroundColor Yellow
Write-Host "Press any key to stop ngrok and exit..."

$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
Stop-Job $ngrokJob
Remove-Job $ngrokJob
