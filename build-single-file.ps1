# Build Single-File Self-Contained Executable
# This creates ONE exe file that includes everything (except external DLLs)

param(
    [string]$Runtime = "win-x64"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Building Single-File Executable" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$ErrorActionPreference = "Stop"

# Check .NET SDK
Write-Host "Checking .NET SDK..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    Write-Host "[OK] .NET SDK $dotnetVersion found" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] .NET SDK not found. Please install .NET 8.0 SDK." -ForegroundColor Red
    exit 1
}

# Clean
Write-Host ""
Write-Host "Cleaning..." -ForegroundColor Yellow
dotnet clean -c Release | Out-Null
if (Test-Path "bin\Release") {
    Remove-Item -Path "bin\Release" -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "[OK] Cleaned" -ForegroundColor Green

# Build single-file self-contained
Write-Host ""
Write-Host "Building single-file self-contained executable..." -ForegroundColor Yellow
Write-Host "  This will create ONE exe file (~70-100 MB)" -ForegroundColor Gray
Write-Host "  Includes: .NET Runtime + all dependencies" -ForegroundColor Gray
Write-Host ""

dotnet publish `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Build failed!" -ForegroundColor Red
    exit 1
}

# Find the exe
$publishPath = "bin\Release\net8.0-windows\$Runtime\publish"
$exePath = Join-Path $publishPath "BatchProcessor.exe"

if (Test-Path $exePath) {
    $exeSize = (Get-Item $exePath).Length / 1MB
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Build Complete!" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "[OK] Single-file executable created!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Location: $exePath" -ForegroundColor Green
    Write-Host "Size: $([math]::Round($exeSize, 2)) MB" -ForegroundColor Gray
    Write-Host ""
    Write-Host "What to give users:" -ForegroundColor Yellow
    Write-Host "  1. BatchProcessor.exe (this file)" -ForegroundColor White
    Write-Host "  2. appsettings.json (configuration)" -ForegroundColor White
    Write-Host "  3. DLLs folder (CommonUtils.dll, CrxApp.dll, etc.)" -ForegroundColor White
    Write-Host ""
    Write-Host "Note: Users still need to:" -ForegroundColor Yellow
    Write-Host "  - Edit appsettings.json with their paths" -ForegroundColor White
    Write-Host "  - Provide the external DLLs (CommonUtils, CrxApp, etc.)" -ForegroundColor White
    Write-Host ""
} else {
    Write-Host "[ERROR] Executable not found!" -ForegroundColor Red
    exit 1
}

