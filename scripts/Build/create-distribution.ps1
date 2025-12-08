# Create Distribution Package Script
# This script creates a ready-to-distribute package for external users

param(
    [string]$OutputFolder = "BatchProcessor_Distribution",
    [string]$Version = "1.0.0"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Create Distribution Package" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$ErrorActionPreference = "Stop"

# Find the latest publish folder
$publishPaths = @(
    "bin\Release\net8.0-windows\win-x64\publish",
    "bin\Release\net8.0-windows\publish",
    "bin\Release\net8.0\publish"
)

$publishPath = $null
foreach ($path in $publishPaths) {
    if (Test-Path $path) {
        $publishPath = $path
        break
    }
}

if (-not $publishPath) {
    Write-Host "✗ No publish folder found!" -ForegroundColor Red
    Write-Host "  Please run build-release.ps1 first" -ForegroundColor Yellow
    exit 1
}

Write-Host "Found publish folder: $publishPath" -ForegroundColor Green
Write-Host ""

# Create distribution folder
if (Test-Path $OutputFolder) {
    Write-Host "Removing existing distribution folder..." -ForegroundColor Yellow
    Remove-Item -Path $OutputFolder -Recurse -Force
}

Write-Host "Creating distribution folder structure..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
New-Item -ItemType Directory -Path "$OutputFolder\DLLs" -Force | Out-Null
New-Item -ItemType Directory -Path "$OutputFolder\Examples" -Force | Out-Null
Write-Host "✓ Created folder structure" -ForegroundColor Green

# Copy application files
Write-Host ""
Write-Host "Copying application files..." -ForegroundColor Yellow
Copy-Item -Path "$publishPath\*" -Destination $OutputFolder -Recurse -Force
Write-Host "✓ Copied application files" -ForegroundColor Green

# Copy configuration template
Write-Host ""
Write-Host "Creating configuration template..." -ForegroundColor Yellow
$appsettingsTemplate = @"
{
  "BatchProcessorSettings": {
    "AutoCADPath": "C:\\Program Files\\Autodesk\\AutoCAD 2025\\accoreconsole.exe",
    "DllsToLoad": [
      "C:\\Path\\To\\DLLs\\CommonUtils.dll",
      "C:\\Path\\To\\DLLs\\Newtonsoft.Json.dll",
      "C:\\Path\\To\\DLLs\\CrxApp.dll"
    ],
    "AvailableCommands": [
      "RunPreScrutinyValidationsBatch",
      "GenerateScrutinyReportBatch",
      "TestBatchCrxCommands"
    ],
    "MainCommand": "RunPreScrutinyValidationsBatch",
    "MaxParallelProcesses": 4,
    "TempScriptFolder": "",
    "EnableVerboseLogging": false
  },
  "_comments": {
    "AutoCADPath": "IMPORTANT: Update this path to match your AutoCAD installation",
    "DllsToLoad": "IMPORTANT: Update these paths to point to your DLL files",
    "AvailableCommands": "List of commands available in the application",
    "MainCommand": "Default command to execute if none selected",
    "MaxParallelProcesses": "Number of drawings to process simultaneously (recommended: 2-8)",
    "TempScriptFolder": "Leave empty to use system temp folder",
    "EnableVerboseLogging": "Set to true for detailed logging (slower)"
  }
}
"@

$appsettingsTemplate | Out-File -FilePath "$OutputFolder\appsettings.json.template" -Encoding UTF8
Write-Host "✓ Created appsettings.json.template" -ForegroundColor Green

# Copy README if exists
if (Test-Path "README.md") {
    Copy-Item -Path "README.md" -Destination "$OutputFolder\README.md" -Force
    Write-Host "✓ Copied README.md" -ForegroundColor Green
}

# Create installation guide
Write-Host ""
Write-Host "Creating installation guide..." -ForegroundColor Yellow
$installGuide = @"
# Installation Guide - BatchProcessor

## Prerequisites

Before installing BatchProcessor, ensure you have:

1. **Windows 10/11** (64-bit)
2. **AutoCAD** installed (any version with accoreconsole.exe)
3. **.NET 8.0 Runtime** (if using framework-dependent deployment)
   - Download from: https://dotnet.microsoft.com/download/dotnet/8.0
   - Install the "Desktop Runtime" version
4. **Required DLLs** (provided separately or by your administrator):
   - CommonUtils.dll
   - Newtonsoft.Json.dll
   - CrxApp.dll
   - UIPlugin.dll (if required)

## Installation Steps

### Step 1: Extract Files
1. Extract the distribution package to a folder (e.g., `C:\BatchProcessor`)
2. Keep all files together in the same folder

### Step 2: Install .NET Runtime (if needed)
1. Check if .NET 8.0 is installed:
   - Open Command Prompt
   - Run: `dotnet --version`
   - If you see version 8.x.x, you're good!
2. If not installed:
   - Download .NET 8.0 Desktop Runtime from Microsoft
   - Install it
   - Restart your computer

### Step 3: Configure appsettings.json
1. Copy `appsettings.json.template` to `appsettings.json`
2. Edit `appsettings.json` with a text editor (Notepad is fine)
3. Update the following settings:

   **AutoCADPath:**
   - Find your AutoCAD installation folder
   - Common locations:
     - `C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe`
     - `C:\Program Files\Autodesk\AutoCAD 2024\accoreconsole.exe`
   - Update the path in appsettings.json

   **DllsToLoad:**
   - Place your DLL files in a folder (e.g., `C:\BatchProcessor\DLLs`)
   - Update the paths in appsettings.json to point to your DLL files
   - Example:
     ```json
     "DllsToLoad": [
       "C:\\BatchProcessor\\DLLs\\CommonUtils.dll",
       "C:\\BatchProcessor\\DLLs\\Newtonsoft.Json.dll",
       "C:\\BatchProcessor\\DLLs\\CrxApp.dll"
     ]
     ```
   - **Important:** Keep the order the same!

### Step 4: Test Installation
1. Double-click `BatchProcessor.exe`
2. If a window opens, installation is successful!
3. If you see an error, check:
   - .NET Runtime is installed
   - appsettings.json paths are correct
   - All DLL files exist at specified paths

## Usage

### GUI Mode (Recommended for beginners)
1. Double-click `BatchProcessor.exe`
2. Select your mode (Commands Execution or JSON Diff Comparison)
3. Follow the on-screen instructions

### Console Mode
1. Open Command Prompt
2. Navigate to the BatchProcessor folder
3. Run:
   ```
   BatchProcessor.exe "input_folder" "output_folder" "config.json"
   ```

For more details, see README.md

## Troubleshooting

### "Missing .NET Runtime" Error
- Install .NET 8.0 Desktop Runtime
- Or use the self-contained version (larger download)

### "DLL not found" Error
- Check that DLL paths in appsettings.json are correct
- Verify DLL files exist at those locations
- Use forward slashes (/) or double backslashes (\\) in paths

### "AutoCAD not found" Error
- Verify AutoCAD is installed
- Check the AutoCADPath in appsettings.json
- Try the full path: `C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe`

### Application Won't Start
- Check Windows Event Viewer for detailed errors
- Ensure you have administrator rights (if required)
- Try running from Command Prompt to see error messages

## Support

For issues or questions, contact your system administrator or support team.

Version: $Version
"@

$installGuide | Out-File -FilePath "$OutputFolder\INSTALLATION_GUIDE.md" -Encoding UTF8
Write-Host "✓ Created INSTALLATION_GUIDE.md" -ForegroundColor Green

# Create example config.json
Write-Host ""
Write-Host "Creating example config.json..." -ForegroundColor Yellow
$exampleConfig = @"
{
  "ProjectType": "BUILDING_PERMISSION",
  "layersToValidate": ["_Door", "_Window"],
  "ExtractBlockNames": true,
  "ExtractLayerNames": true
}
"@

$exampleConfig | Out-File -FilePath "$OutputFolder\Examples\config.json.example" -Encoding UTF8
Write-Host "✓ Created example config.json" -ForegroundColor Green

# Create version file
$versionInfo = @"
BatchProcessor Distribution Package
Version: $Version
Created: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
"@

$versionInfo | Out-File -FilePath "$OutputFolder\VERSION.txt" -Encoding UTF8

# Calculate size
$totalSize = (Get-ChildItem -Path $OutputFolder -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
$fileCount = (Get-ChildItem -Path $OutputFolder -Recurse -File).Count

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Distribution Package Created!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Location: $OutputFolder" -ForegroundColor Green
Write-Host "Files: $fileCount" -ForegroundColor Gray
Write-Host "Size: $([math]::Round($totalSize, 2)) MB" -ForegroundColor Gray
Write-Host "Version: $Version" -ForegroundColor Gray
Write-Host ""

# Ask to create ZIP
$createZip = Read-Host "Create ZIP archive? (Y/N)"
if ($createZip -eq "Y" -or $createZip -eq "y") {
    $zipName = "BatchProcessor_v$Version.zip"
    Write-Host ""
    Write-Host "Creating ZIP archive..." -ForegroundColor Yellow
    Compress-Archive -Path "$OutputFolder\*" -DestinationPath $zipName -Force
    $zipSize = (Get-Item $zipName).Length / 1MB
    Write-Host "✓ Created: $zipName ($([math]::Round($zipSize, 2)) MB)" -ForegroundColor Green
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Review the distribution folder: $OutputFolder" -ForegroundColor White
Write-Host "  2. Test on a clean machine if possible" -ForegroundColor White
Write-Host "  3. Add required DLLs to DLLs folder (if distributing)" -ForegroundColor White
Write-Host "  4. Share the package with users" -ForegroundColor White
Write-Host ""

