# BatchProcessor - Testing Steps

## 🎯 Quick Testing Guide for Standalone BatchProcessor

This project is **OUTSIDE** the scrutiny folder - fully independent!

---

## 🚀 Quick Running Commands

### Interactive Mode (Easiest for Testing)
```powershell
cd C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0
.\BatchProcessor.exe
```
Then enter paths when prompted and select which command to run:
- Option 1: ProcessWithJsonBatch
- Option 2: ExtractScrutinyMetricsBatch

### Command Line Mode (Full Command)
```powershell
cd C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"
```

### Command Line with Specific Command
```powershell
# Run ExtractScrutinyMetricsBatch
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json" "" 4 "ExtractScrutinyMetricsBatch"

# Run ProcessWithJsonBatch
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json" "" 4 "ProcessWithJsonBatch"
```

### Help Command
```powershell
.\BatchProcessor.exe --help
```

---

## ✅ Before You Start - Quick Checklist

**Make sure you have:**
- [ ] .NET 8.0 SDK installed
- [ ] AutoCAD installed (with accoreconsole.exe)
- [ ] Built the scrutiny/CrxApp project (creates required DLLs)
- [ ] Built the BatchProcessor project (creates .exe)
- [ ] At least one .dwg file to test with
- [ ] Configured `appsettings.json` with correct paths

**Quick verification commands:**
```powershell
# Check .NET is installed
dotnet --version

# Check BatchProcessor.exe exists
Test-Path "C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0\BatchProcessor.exe"

# Check appsettings.json exists
Test-Path "C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0\appsettings.json"

# Check AutoCAD exists
Test-Path "C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe"
```

**If any return False, follow the steps below! ⬇️**

---

## 📁 Project Location

```
C:\Users\Jaya\Desktop\scrutiny_projects\
  ├── scrutiny\                        ← Your main project (builds CrxApp.dll)
  └── BatchProcessor\                  ← This standalone project
```

---

## ✅ Step 1: Build CrxApp (in scrutiny project)

### 📝 Choose Your IDE:

<details>
<summary><b>Option A: Visual Studio 2025 (Full IDE)</b> ✨ Click to expand</summary>

#### Method 1: Using Solution Explorer (Easiest)
1. Open `scrutiny.sln` in Visual Studio 2025
2. In **Solution Explorer**, right-click on **`CrxApp`** project
3. Click **Build**
4. Right-click on **`CommonUtils`** project
5. Click **Build**

#### Method 2: Using Build Menu
1. Open `scrutiny.sln` in Visual Studio 2025
2. Click **Build → Build Solution** (or press `Ctrl+Shift+B`)

#### Method 3: Using Terminal in Visual Studio
1. Click **View → Terminal**
2. Run the commands in Option B below

**✅ Look for "Build succeeded" in the Output window (bottom of VS)**

</details>

<details>
<summary><b>Option B: Visual Studio Code</b> 📝 Click to expand</summary>

VS Code doesn't have a "Build" button - you must use the **Terminal**:
1. Press `` Ctrl + ` `` (backtick) to open the integrated terminal
2. Or click **Terminal → New Terminal** from the menu
3. Run the `dotnet build` commands below

</details>

---

### Commands (for VS Code or Terminal):

First, build the DLLs that BatchProcessor will use:

```powershell
cd C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny
dotnet build CrxApp\CrxApp.csproj
dotnet build CommonUtils\CommonUtils.csproj
```

**Result:** Creates DLLs in:
- `scrutiny\CrxApp\bin\Debug\net8.0\CrxApp.dll`
- `scrutiny\CrxApp\bin\Debug\net8.0\Newtonsoft.Json.dll`
- `scrutiny\CommonUtils\bin\Debug\net8.0\CommonUtils.dll`

**What you should see:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## ✅ Step 2: Build BatchProcessor

### 📝 Choose Your IDE:

<details>
<summary><b>Option A: Visual Studio 2025 (Full IDE)</b> ✨ Click to expand</summary>

#### Method 1: Open Project Directly
1. In Visual Studio, click **File → Open → Project/Solution**
2. Navigate to `C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor`
3. Open **`BatchProcessor.csproj`**
4. Click **Build → Build Solution** (or press `Ctrl+Shift+B`)

#### Method 2: From Solution Explorer
1. If you already have the project open
2. Right-click on **`BatchProcessor`** project
3. Click **Build**

**✅ Look for "Build succeeded" in the Output window**

</details>

<details>
<summary><b>Option B: Visual Studio Code</b> 📝 Click to expand</summary>

Use the integrated terminal:
1. Press `` Ctrl + ` `` to open terminal
2. Run the `dotnet build` command below

</details>

---

### Commands (for VS Code or Terminal):

Now build the standalone BatchProcessor:

```powershell
cd C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor
dotnet build
```

**Result:** Creates `BatchProcessor\bin\Debug\net8.0\BatchProcessor.exe`

**What you should see:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

✅ The `appsettings.json` file is automatically copied to the output folder during build

---

## ✅ Step 3: Configure DLL Paths

### 3a. First, verify the DLLs exist from Step 1:

```powershell
# Check that all required DLLs were built successfully
Test-Path "C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny\CommonUtils\bin\Debug\net8.0\CommonUtils.dll"
Test-Path "C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny\CrxApp\bin\Debug\net8.0\Newtonsoft.Json.dll"
Test-Path "C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny\CrxApp\bin\Debug\net8.0\CrxApp.dll"
```

**Each should return `True`**. If `False`, go back to Step 1 and rebuild.

### 3b. Verify AutoCAD exists:

```powershell
# Check AutoCAD path (adjust version if needed)
Test-Path "C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe"
```

**Should return `True`**. If `False`, find your actual AutoCAD path:
```powershell
# List all AutoCAD versions on your system
Get-ChildItem "C:\Program Files\Autodesk" -Directory | Where-Object {$_.Name -like "*AutoCAD*"}
```

### 3c. Now edit the configuration:

Open and edit `BatchProcessor\bin\Debug\net8.0\appsettings.json`:

```json
{
  "BatchProcessorSettings": {
    "AutoCADPath": "C:\\Program Files\\Autodesk\\AutoCAD 2025\\accoreconsole.exe",
    "DllsToLoad": [
      "C:\\Users\\Jaya\\Desktop\\scrutiny_projects\\scrutiny\\CommonUtils\\bin\\Debug\\net8.0\\CommonUtils.dll",
      "C:\\Users\\Jaya\\Desktop\\scrutiny_projects\\scrutiny\\CrxApp\\bin\\Debug\\net8.0\\Newtonsoft.Json.dll",
      "C:\\Users\\Jaya\\Desktop\\scrutiny_projects\\scrutiny\\CrxApp\\bin\\Debug\\net8.0\\CrxApp.dll"
    ],
    "AvailableCommands": [
      "ProcessWithJsonBatch",
      "ExtractScrutinyMetricsBatch"
    ],
    "MainCommand": "ProcessWithJsonBatch",
    "MaxParallelProcesses": 4
  }
}
```

**Important Notes:**
- ⚠️ Edit the file in **bin\Debug\net8.0\appsettings.json** (NOT the root folder)
- ✏️ Use double backslashes `\\` in JSON paths
- 📝 Replace paths with YOUR actual paths if different
- 🎯 `AvailableCommands` lists commands you can choose from when running
- 📌 `MainCommand` is the default if you don't select one
- 💾 Save the file after editing

---

## ✅ Step 4: Prepare Test Data

```powershell
# Create test folders
cd C:\Users\Jaya\Documents
mkdir AutoCADTests
cd AutoCADTests
mkdir input_drawings
mkdir output_results
```

### Copy Test Files:
1. Copy 2-3 .dwg files to `input_drawings\`
2. Create `config.json`:

```json
{
  "ProjectType": "BUILDING_PERMISSION",
  "layersToValidate": [],
  "ExtractBlockNames": true,
  "ExtractLayerNames": true
}
```

---

## ✅ Step 5: Run BatchProcessor

### Option A: Interactive Mode (Recommended for First Test)

```powershell
cd C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0
.\BatchProcessor.exe
```

**Then enter when prompted:**
```
Input folder (containing .dwg files): C:\Users\Jaya\Documents\AutoCADTests\input_drawings
Output folder (for JSON results): C:\Users\Jaya\Documents\AutoCADTests\output_results
Input JSON configuration file: C:\Users\Jaya\Documents\AutoCADTests\config.json

🎯 Select command to execute:
  1. ProcessWithJsonBatch
  2. ExtractScrutinyMetricsBatch
Enter number (1-2) or press Enter for default (ProcessWithJsonBatch): [Type 1 or 2, or press Enter]

AutoCAD path (press Enter for default): [Press Enter to use default from appsettings.json]
Max parallel processes (press Enter for default: 4): [Press Enter or type a number]
```

### Option B: Command Line Mode (For Automation)

```powershell
cd C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0

.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"
```

### Option C: Command Line with Custom AutoCAD Version

```powershell
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json" "C:\Program Files\Autodesk\AutoCAD 2024\accoreconsole.exe"
```

### Option D: Command Line with Custom Parallel Count

```powershell
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json" "C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe" 8
```

### Option E: Command Line with Specific Command

```powershell
# Run ExtractScrutinyMetricsBatch command
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json" "" 4 "ExtractScrutinyMetricsBatch"

# Run ProcessWithJsonBatch command
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json" "" 4 "ProcessWithJsonBatch"
```

### Option F: Using Full Paths (from any location)

```powershell
# Run from anywhere by providing full paths
C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"

# With specific command
C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json" "" 4 "ExtractScrutinyMetricsBatch"
```

### Getting Help

```powershell
.\BatchProcessor.exe --help
# or
.\BatchProcessor.exe -h
# or
.\BatchProcessor.exe /?
```

---

## 📋 Expected Output

```
╔══════════════════════════════════════════════════════════════╗
║          AutoCAD Batch Processor v1.0                       ║
║          Standalone - Process Multiple DWG Files            ║
╚══════════════════════════════════════════════════════════════╝

✅ Configuration loaded from appsettings.json
✅ Found 3 .dwg file(s)
✅ Output folder: C:\...\output_results
✅ Config JSON found
✅ AutoCAD found
✅ 3 DLL(s) configured
✅ All DLLs found

────────────────────────────────────────────────────────────────
Starting batch processing...
────────────────────────────────────────────────────────────────

╔══════════════════════════════════════════════════════════════╗
║  AutoCAD Batch Processor (Standalone Mode)                  ║
╚══════════════════════════════════════════════════════════════╝

📁 Input Folder:  C:\...\input_drawings
📁 Output Folder: C:\...\output_results
📋 Input JSON:    C:\...\config.json
📄 Total Files:   3
⚙️  Max Parallel:  4
🔧 AutoCAD:       C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe
📦 DLLs to Load:  3
🎯 Command:       ProcessWithJsonBatch

────────────────────────────────────────────────────────────────

[20241122_150123_456] ⏳ Processing: Test1
[20241122_150123_789] ⏳ Processing: Test2
[20241122_150123_012] ⏳ Processing: Test3
[20241122_150125_345] ✅ Completed: Test1 (1.9s)
[20241122_150125_678] ✅ Completed: Test2 (1.9s)
[20241122_150126_901] ✅ Completed: Test3 (2.0s)

═════════════════════════════════════════════════════════════════
  PROCESSING SUMMARY
═════════════════════════════════════════════════════════════════

  ✅ Successful: 3
  ❌ Failed:     0
  📊 Total:      3
  ⏱️  Duration:   0.10 minutes
  ⚡ Avg Speed:  2.00 sec/file

═════════════════════════════════════════════════════════════════

✅ All processing complete!

Press any key to exit...
```

---

## ✅ Step 6: Verify Output

Check `C:\Users\Jaya\Documents\AutoCADTests\output_results\`:

```
Test1_20241122_150125_345.json
Test2_20241122_150125_678.json
Test3_20241122_150126_901.json
```

Open a JSON file to verify it contains data.

---

## 🧪 Additional Tests

### Test 1: Change AutoCAD Version

Edit `appsettings.json`:
```json
"AutoCADPath": "C:\\Program Files\\Autodesk\\AutoCAD 2024\\accoreconsole.exe"
```

Run again - **no rebuild needed!**

```powershell
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"
```

### Test 2: Change Parallel Count

Edit `appsettings.json`:
```json
"MaxParallelProcesses": 2
```

Run again - processes 2 at a time instead of 4.

```powershell
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"
```

### Test 3: Enable Verbose Logging

Edit `appsettings.json`:
```json
"EnableVerboseLogging": true
```

Run again - see detailed output including script content.

```powershell
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"
```

### Test 4: Different Command Selection

**Option A: Interactive Selection**
```powershell
# Run in interactive mode
.\BatchProcessor.exe

# When prompted, select command 2 (ExtractScrutinyMetricsBatch)
# Then enter your input/output paths
```

**Option B: Command Line with Specific Command**
```powershell
# Run ExtractScrutinyMetricsBatch
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json" "" 4 "ExtractScrutinyMetricsBatch"
```

**Option C: Change Default Command**

Edit `appsettings.json`:
```json
"MainCommand": "ExtractScrutinyMetricsBatch"
```

Then run without specifying command:
```powershell
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"
```

### Test 5: Process Single File

Create a folder with just one .dwg file:

```powershell
mkdir C:\Users\Jaya\Documents\AutoCADTests\single_test
# Copy one .dwg file to single_test folder

.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\single_test" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"
```

### Test 6: Large Batch (10+ Files)

```powershell
mkdir C:\Users\Jaya\Documents\AutoCADTests\large_batch
# Copy 10+ .dwg files to large_batch folder

# Use higher parallelism for faster processing
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\large_batch" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json" "C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe" 8
```

---

## 🔄 Common Workflows

### Workflow 1: When CrxApp Changes

```powershell
# 1. Rebuild CrxApp in scrutiny project
cd C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny
dotnet build CrxApp\CrxApp.csproj

# 2. Run BatchProcessor (no rebuild needed!)
cd ..\BatchProcessor\bin\Debug\net8.0
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"
```

BatchProcessor automatically uses the updated CrxApp.dll!

### Workflow 2: Daily Testing Routine

```powershell
# Create a batch script for repeated testing
# Save as: test_batch_processor.bat

@echo off
echo Starting BatchProcessor Test...
cd /d C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0
BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"
pause
```

**Run the batch script:**
```powershell
.\test_batch_processor.bat
```

### Workflow 3: Test Multiple Configurations

```powershell
cd C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0

# Test with different config files
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_1" "C:\Users\Jaya\Documents\AutoCADTests\config1.json"

.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_2" "C:\Users\Jaya\Documents\AutoCADTests\config2.json"

.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_3" "C:\Users\Jaya\Documents\AutoCADTests\config3.json"
```

### Workflow 4: Quick Retest After Changes

```powershell
# If you're in the workspace root
cd bin\Debug\net8.0

# Quick test with shortened paths (if you've set them before)
.\BatchProcessor.exe
# Then press ENTER for all defaults (uses previous paths from memory if in same session)
```

---

## 🐛 Troubleshooting

### Running Issues

#### Issue: "BatchProcessor.exe is not recognized"

**Cause:** Not in the correct directory or path is incorrect.

**Solution:**
```powershell
# Make sure you're in the output directory
cd C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0

# Or use the full path
C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0\BatchProcessor.exe
```

#### Issue: "No .dwg files found"

**Cause:** Input folder is empty or contains no .dwg files.

**Solution:**
```powershell
# Check if .dwg files exist in your input folder
Get-ChildItem "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" -Filter "*.dwg"

# If empty, copy some .dwg files there
Copy-Item "C:\Path\To\Your\Drawings\*.dwg" "C:\Users\Jaya\Documents\AutoCADTests\input_drawings\"
```

#### Issue: "Configuration file not found"

**Cause:** config.json path is wrong or file doesn't exist.

**Solution:**
```powershell
# Check if config.json exists
Test-Path "C:\Users\Jaya\Documents\AutoCADTests\config.json"

# If False, create it
New-Item "C:\Users\Jaya\Documents\AutoCADTests\config.json" -ItemType File
# Then edit it with the required JSON content
```

#### Issue: All files fail to process

**Possible causes:**
1. **DLL paths are wrong** - Update `appsettings.json` with correct paths
2. **Command doesn't exist** - Make sure `ProcessWithJsonBatch` exists in CrxApp.dll
3. **AutoCAD version mismatch** - Verify AutoCAD path is correct

**Solution:**
```powershell
# Verify all DLLs exist
Test-Path "C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny\CommonUtils\bin\Debug\net8.0\CommonUtils.dll"
Test-Path "C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny\CrxApp\bin\Debug\net8.0\CrxApp.dll"
Test-Path "C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny\CrxApp\bin\Debug\net8.0\Newtonsoft.Json.dll"

# Verify AutoCAD
Test-Path "C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe"

# Then run again
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"
```

#### Issue: Process is too slow

**Solution:** Increase parallel processes

**Edit `appsettings.json`:**
```json
"MaxParallelProcesses": 8
```

**Or pass as command line argument:**
```powershell
.\BatchProcessor.exe "input" "output" "config.json" "C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe" 8
```

#### Issue: Want to see more details about what's happening

**Solution:** Enable verbose logging

**Edit `appsettings.json`:**
```json
"EnableVerboseLogging": true
```

**Then run:**
```powershell
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"
```

### Build Issues

#### Issue: "Assets file 'project.assets.json' not found" (NETSDK1004)

**This means NuGet packages need to be restored.**

**Solution (Visual Studio):**
1. Right-click on **Solution** or **Project** in Solution Explorer
2. Click **Restore NuGet Packages**
3. Wait for completion
4. Build again (`Ctrl+Shift+B`)

**Solution (Command Line):**
```powershell
cd C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor
dotnet restore
dotnet build
```

**Alternative:** Click **Build → Rebuild Solution** (auto-restores packages)

---

### Issue: "DLL not found"

**Check:** Are the paths in `appsettings.json` correct?

```powershell
# Verify DLL exists
Test-Path "C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny\CrxApp\bin\Debug\net8.0\CrxApp.dll"
```

### Issue: "Configuration loaded from appsettings.json" not shown

**Solution:** `appsettings.json` not in output folder. Rebuild BatchProcessor.

### Issue: "AutoCAD not found"

**Solution:** Update `AutoCADPath` in `appsettings.json`.

### Issue: All drawings fail

**Solution:** Check that `ProcessWithJsonBatch` command exists in CrxApp.

---

## 📊 Testing Checklist

- [ ] Built scrutiny/CrxApp successfully
- [ ] Built BatchProcessor successfully
- [ ] `appsettings.json` exists in output folder
- [ ] Updated DLL paths in `appsettings.json`
- [ ] All DLL files exist at specified paths
- [ ] AutoCAD path is correct
- [ ] Test .dwg files prepared
- [ ] Test config.json created
- [ ] Batch processing runs successfully
- [ ] JSON outputs created
- [ ] Can change config without rebuilding
- [ ] Verbose logging works
- [ ] Different AutoCAD versions work

---

## 🎉 Success Criteria

✅ BatchProcessor.exe runs independently  
✅ Loads all 3 DLLs successfully  
✅ Processes all drawings  
✅ Creates JSON output files  
✅ Can change settings in appsettings.json without rebuild  
✅ No dependencies on scrutiny solution  

---

## 📝 Notes

- **Standalone:** BatchProcessor is completely separate from scrutiny
- **No copying:** Just points to DLLs via configuration
- **Easy updates:** Update CrxApp, BatchProcessor picks it up automatically
- **Flexible:** Different configs for different environments

---

## 💡 Power User Tips

### Create PowerShell Alias

Add to your PowerShell profile for quick access:

```powershell
# Edit your PowerShell profile
notepad $PROFILE

# Add this line:
function Run-BatchProcessor { 
    & "C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0\BatchProcessor.exe" $args 
}
Set-Alias bp Run-BatchProcessor

# Save and reload
. $PROFILE
```

**Now you can run from anywhere:**
```powershell
bp "C:\Drawings" "C:\Results" "config.json"
```

### Create Desktop Shortcut

1. Right-click Desktop → New → Shortcut
2. Target: `C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0\BatchProcessor.exe`
3. Name: `AutoCAD Batch Processor`
4. Double-click to run in interactive mode!

### Environment Variable (Optional)

Add to system PATH:

```powershell
# Add to PATH (Run PowerShell as Administrator)
$path = [Environment]::GetEnvironmentVariable("Path", "User")
$newPath = "C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0"
[Environment]::SetEnvironmentVariable("Path", "$path;$newPath", "User")

# Restart PowerShell, then run from anywhere:
BatchProcessor.exe "input" "output" "config.json"
```

### Schedule Automated Runs

Use Windows Task Scheduler:

1. Open Task Scheduler
2. Create Basic Task
3. Action: Start a program
4. Program: `C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0\BatchProcessor.exe`
5. Arguments: `"C:\Drawings\input" "C:\Drawings\output" "C:\Drawings\config.json"`
6. Schedule: Daily, Weekly, etc.

---

## 🚀 Next Steps

1. **Test with real drawings** from your project
2. **Create production config** with production paths
3. **Automate** with batch scripts or Task Scheduler
4. **Distribute** by copying bin folder + editing appsettings.json
5. **Create shortcuts** for quick access (see Power User Tips above)

---

## 📋 Quick Reference Card

### Most Common Commands (Copy & Paste Ready)

```powershell
# Navigate to output folder
cd C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0

# Interactive mode
.\BatchProcessor.exe

# Command line mode
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"

# With custom parallel count (8 processes)
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json" "C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe" 8

# With specific command (ExtractScrutinyMetricsBatch)
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json" "" 4 "ExtractScrutinyMetricsBatch"

# With specific command (ProcessWithJsonBatch)
.\BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json" "" 4 "ProcessWithJsonBatch"

# Help
.\BatchProcessor.exe --help

# Check for .dwg files
Get-ChildItem "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" -Filter "*.dwg"

# Verify DLLs exist
Test-Path "C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny\CommonUtils\bin\Debug\net8.0\CommonUtils.dll"
Test-Path "C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny\CrxApp\bin\Debug\net8.0\CrxApp.dll"
Test-Path "C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny\CrxApp\bin\Debug\net8.0\Newtonsoft.Json.dll"

# Verify AutoCAD
Test-Path "C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe"

# Rebuild CrxApp (when needed)
cd C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny
dotnet build CrxApp\CrxApp.csproj

# Rebuild BatchProcessor (when needed)
cd C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor
dotnet build
```

### Create Test Batch Script

Save as `run_batch_test.bat` in `BatchProcessor\bin\Debug\net8.0\`:

```batch
@echo off
echo ╔══════════════════════════════════════════════════════════════╗
echo ║          Running BatchProcessor Test                        ║
echo ╚══════════════════════════════════════════════════════════════╝
echo.

cd /d "%~dp0"

echo Current directory: %CD%
echo.

BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"

echo.
echo ╔══════════════════════════════════════════════════════════════╗
echo ║          Test Complete                                       ║
echo ╚══════════════════════════════════════════════════════════════╝
pause
```

**Run it:**
```powershell
.\run_batch_test.bat
```

---

**Ready to test?** Start with Step 1! 🎯

