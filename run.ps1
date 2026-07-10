param(
    [switch]$NoFrontend,
    [switch]$NoBackend,
    [switch]$RebuildFrontend
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $NoBackend) {
    Write-Host "🚀 Starting Backend (serves API + Angular SPA)..." -ForegroundColor Green
    $backendJob = Start-Job -ScriptBlock {
        param($dir)
        Set-Location $dir
        dotnet run
    } -ArgumentList "$root\AiAgentBackend"
}

if ($RebuildFrontend) {
    Write-Host "🏗️  Rebuilding Frontend..." -ForegroundColor Yellow
    Push-Location "$root\ai-agent-frontend"
    $env:NODE_OPTIONS = "--max-old-space-size=2048"
    npm run build
    Pop-Location
}

if (-not $NoFrontend) {
    Write-Host ""
    Write-Host "📌 Note: Backend now serves the Angular SPA directly at http://localhost:5000" -ForegroundColor Cyan
    Write-Host "📌 Note: ng serve is only needed if you're developing the frontend." -ForegroundColor Cyan
    Write-Host ""
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AI Agent - Running" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  App:      http://localhost:5000" -ForegroundColor White
if (-not $NoFrontend -and (Get-Job -Name "*ng*" -ErrorAction SilentlyContinue)) {
    Write-Host "  Dev:      http://localhost:4200" -ForegroundColor White
}
Write-Host "  Swagger:  http://localhost:5000/swagger" -ForegroundColor White
Write-Host "  Hangfire: http://localhost:5000/hangfire" -ForegroundColor White
Write-Host "  Health:   http://localhost:5000/health/real-time" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press Ctrl+C to stop all services" -ForegroundColor Yellow

# Wait for jobs
if (-not $NoBackend) { Wait-Job $backendJob }
if (-not $NoFrontend -and (Get-Job -Name "*ng*" -ErrorAction SilentlyContinue)) { Wait-Job $frontendJob }
