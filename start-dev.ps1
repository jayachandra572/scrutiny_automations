# Start Development Mode - Auto build and run
# Run this script to automatically rebuild and restart on code changes

Write-Host "Starting Development Mode..." -ForegroundColor Cyan
Write-Host "This will automatically rebuild and restart the app when you save files." -ForegroundColor Yellow
Write-Host ""

# Start the auto-build-run script in a new window
Start-Process powershell -ArgumentList "-NoExit", "-ExecutionPolicy", "Bypass", "-File", "`"$PSScriptRoot\auto-build-run.ps1`""

Write-Host "✅ Development mode started in a new window!" -ForegroundColor Green
Write-Host "You can continue working. The app will auto-rebuild on file changes." -ForegroundColor Yellow

