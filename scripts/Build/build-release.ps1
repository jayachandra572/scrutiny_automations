# Build Release Script for BatchProcessor
# This script builds the application in Release mode for distribution

param(
    [switch]$SelfContained = $false,
    [string]$Runtime = "win-x64"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  BatchProcessor Release Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$ErrorActionPreference = "Stop"

# Check if .NET SDK is installed
Write-Host "Checking .NET SDK..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    Write-Host "[OK] .NET SDK $dotnetVersion found" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] .NET SDK not found. Please install .NET 8.0 SDK." -ForegroundColor Red
    exit 1
}

# Clean previous builds
Write-Host ""
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
dotnet clean -c Release | Out-Null
if (Test-Path "bin\Release") {
    Remove-Item -Path "bin\Release" -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "[OK] Cleaned" -ForegroundColor Green

# Build and publish
Write-Host ""
Write-Host "Building Release version..." -ForegroundColor Yellow
Write-Host "  Self-Contained: $SelfContained" -ForegroundColor Gray
Write-Host "  Runtime: $Runtime" -ForegroundColor Gray

$publishArgs = @(
    "publish",
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", $SelfContained.ToString().ToLower(),
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true"
)

if ($SelfContained) {
    $publishArgs += "-p:PublishTrimmed=false"  # Don't trim to avoid issues
}

dotnet $publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Build successful!" -ForegroundColor Green

# Output location
$publishPath = "bin\Release\net8.0-windows\$Runtime\publish"
if (Test-Path $publishPath) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Build Complete!" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Output location: $publishPath" -ForegroundColor Green
    Write-Host ""
    
    # Show file count
    $fileCount = (Get-ChildItem -Path $publishPath -File).Count
    $folderSize = (Get-ChildItem -Path $publishPath -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host "Files: $fileCount" -ForegroundColor Gray
    Write-Host "Size: $([math]::Round($folderSize, 2)) MB" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Review files in: $publishPath" -ForegroundColor White
    Write-Host "  2. Run create-distribution.ps1 to create distribution package" -ForegroundColor White
    Write-Host ""
} else {
    Write-Host "✗ Publish folder not found!" -ForegroundColor Red
    exit 1
}

