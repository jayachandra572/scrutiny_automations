# AutoCAD Batch Processor

**Standalone tool** to process multiple AutoCAD drawing files in parallel without opening the AutoCAD GUI.

## ✨ Features

✅ **Fully Standalone** - No project dependencies, works independently  
✅ **Configuration File** - Change settings without recompiling  
✅ **Multiple DLL Support** - Load all required DLLs (CommonUtils, CrxApp, etc.)  
✅ **Parallel Processing** - Process multiple drawings simultaneously  
✅ **No GUI Required** - Uses accoreconsole.exe (headless mode)  
✅ **Progress Tracking** - Real-time status updates  
✅ **Error Handling** - Failed drawings don't stop the batch  
✅ **Timestamped Output** - Never overwrite previous results  

---

## 🚀 Quick Start

### 1. Build the Project

```bash
dotnet build BatchProcessor.csproj
```

Or in Visual Studio: Right-click project → Build

### 2. Configure `appsettings.json`

Edit `bin\Debug\net8.0\appsettings.json`:

```json
{
  "BatchProcessorSettings": {
    "AutoCADPath": "C:\\Program Files\\Autodesk\\AutoCAD 2025\\accoreconsole.exe",
    "DllsToLoad": [
      "C:\\Path\\To\\scrutiny\\CommonUtils\\bin\\Debug\\net8.0\\CommonUtils.dll",
      "C:\\Path\\To\\scrutiny\\CrxApp\\bin\\Debug\\net8.0\\Newtonsoft.Json.dll",
      "C:\\Path\\To\\scrutiny\\CrxApp\\bin\\Debug\\net8.0\\CrxApp.dll"
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

### 3. Run It

**Interactive Mode (with command selection menu):**
```bash
cd bin\Debug\net8.0
.\BatchProcessor.exe
```
You'll be prompted to select which command to run from the available options.

**Command Line Mode:**
```bash
.\BatchProcessor.exe "C:\input" "C:\output" "config.json"
```

**Command Line with Specific Command:**
```bash
.\BatchProcessor.exe "C:\input" "C:\output" "config.json" "" 4 "ExtractScrutinyMetricsBatch"
```

---

## ⚙️ Configuration

All settings are in `appsettings.json` - **no recompile needed!**

### Settings:

| Setting | Description | Example |
|---------|-------------|---------|
| **AutoCADPath** | Path to accoreconsole.exe | `C:\\...\\accoreconsole.exe` |
| **DllsToLoad** | List of DLLs (full paths, in order) | See below |
| **AvailableCommands** | Commands to choose from at runtime | `["ProcessWithJsonBatch", "ExtractScrutinyMetricsBatch"]` |
| **MainCommand** | Default AutoCAD command | `ProcessWithJsonBatch` |
| **MaxParallelProcesses** | Number of parallel processes | `4` |
| **TempScriptFolder** | Temp script folder (empty = system temp) | `` |
| **EnableVerboseLogging** | Show detailed logs | `false` |

### Required DLLs:

```json
"DllsToLoad": [
  "C:\\scrutiny\\CommonUtils\\bin\\Debug\\net8.0\\CommonUtils.dll",
  "C:\\scrutiny\\CrxApp\\bin\\Debug\\net8.0\\Newtonsoft.Json.dll",
  "C:\\scrutiny\\CrxApp\\bin\\Debug\\net8.0\\CrxApp.dll"
]
```

**Order matters!** Dependencies must be loaded before CrxApp.dll.

---

## 📋 Usage

### Interactive Mode (Easiest)

```bash
> BatchProcessor.exe

Input folder (containing .dwg files): C:\drawings
Output folder (for JSON results): C:\results
Input JSON configuration file: config.json

🎯 Select command to execute:
  1. ProcessWithJsonBatch
  2. ExtractScrutinyMetricsBatch
Enter number (1-2) or press Enter for default (ProcessWithJsonBatch): 2
```

### Command Line Mode (Automation)

```bash
BatchProcessor.exe "input_folder" "output_folder" "config.json"
```

### With Custom AutoCAD Path

```bash
BatchProcessor.exe "input" "output" "config.json" "C:\...\AutoCAD 2024\accoreconsole.exe"
```

### With Custom Parallel Count

```bash
BatchProcessor.exe "input" "output" "config.json" "C:\...\accoreconsole.exe" 8
```

### With Specific Command

```bash
BatchProcessor.exe "input" "output" "config.json" "C:\...\accoreconsole.exe" 4 "ExtractScrutinyMetricsBatch"
```

Or use empty string for default AutoCAD path:

```bash
BatchProcessor.exe "input" "output" "config.json" "" 4 "ExtractScrutinyMetricsBatch"
```

---

## 📁 Input JSON Format

Create a `config.json` with your processing parameters:

```json
{
  "ProjectType": "BUILDING_PERMISSION",
  "layersToValidate": ["_Door", "_Window"],
  "ExtractBlockNames": true,
  "ExtractLayerNames": true
}
```

---

## 📤 Output

Each drawing generates a timestamped JSON file:

```
output_folder/
  ├── Drawing1_20241122_143025_345.json
  ├── Drawing2_20241122_143027_678.json
  └── Drawing3_20241122_143029_901.json
```

---

## 🔧 How It Works

```
1. BatchProcessor.exe loads appsettings.json
2. Finds all .dwg files in input folder
3. Spawns multiple accoreconsole.exe processes (parallel)
4. Each process:
   - Loads CommonUtils.dll
   - Loads Newtonsoft.Json.dll
   - Loads CrxApp.dll
   - Runs ProcessWithJsonBatch command
   - Generates output JSON
5. Collects results and shows summary
```

---

## 🎯 Dependencies

### At Build Time:
- .NET 8.0 SDK
- Microsoft.Extensions.Configuration (NuGet)

### At Runtime:
- .NET 8.0 Runtime
- AutoCAD (with accoreconsole.exe)
- DLLs specified in `appsettings.json`:
  - CommonUtils.dll
  - Newtonsoft.Json.dll
  - CrxApp.dll

**No project references!** Fully standalone!

---

## 🐛 Troubleshooting

| Issue | Solution |
|-------|----------|
| Build fails | Run `dotnet build` |
| exe not found | Check `bin\Debug\net8.0\` folder |
| appsettings.json not found | Rebuild project (should copy automatically) |
| AutoCAD not found | Update `AutoCADPath` in appsettings.json |
| DLL not found | Update `DllsToLoad` paths in appsettings.json |
| All files fail | Check DLL paths are correct |

---

## 📚 Documentation

For detailed guides, see the main scrutiny project:
- Configuration Guide
- Testing Guide  
- Console App Explanation
- Batch Processing Guide

---

## 🚀 Deployment

### For Production:

1. Build in Release mode:
   ```bash
   dotnet build -c Release
   ```

2. Copy `bin\Release\net8.0\` folder to deployment location

3. Edit `appsettings.json` for production:
   ```json
   {
     "BatchProcessorSettings": {
       "AutoCADPath": "C:\\Program Files\\Autodesk\\AutoCAD 2025\\accoreconsole.exe",
       "DllsToLoad": [
         "C:\\Production\\CommonUtils.dll",
         "C:\\Production\\Newtonsoft.Json.dll",
         "C:\\Production\\CrxApp.dll"
       ],
       "MaxParallelProcesses": 8
     }
   }
   ```

4. Run:
   ```bash
   BatchProcessor.exe "input" "output" "config.json"
   ```

---

## ✨ Benefits

### vs. Integrated in Solution:
✅ **No build coupling** - Build scrutiny and BatchProcessor separately  
✅ **Smaller executable** - No Design Automation dependencies  
✅ **Easy distribution** - Just one folder  
✅ **Independent versioning** - Update without affecting scrutiny  

### vs. Hard-coded Paths:
✅ **Configuration file** - Change DLL paths without recompiling  
✅ **Environment-specific** - Different configs for dev/prod  
✅ **Easy maintenance** - Update paths in JSON, not code  

---

## 📝 Version History

- **v1.0.0** - Initial standalone release
  - Configuration file support
  - Multiple DLL loading
  - Parallel processing
  - Fully independent from scrutiny project

---

## 🤝 Contributing

This is a standalone tool that consumes CrxApp.dll from the scrutiny project.

To update:
1. Build scrutiny project (creates updated CrxApp.dll)
2. Update `DllsToLoad` paths in appsettings.json
3. Run BatchProcessor

---

## 📞 Support

For issues:
1. Check appsettings.json paths are correct
2. Verify all DLLs exist
3. Ensure AutoCAD is installed
4. Check console error messages

---

## 📜 License

Part of the Scrutiny project.

