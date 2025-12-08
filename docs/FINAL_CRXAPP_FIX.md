# Final CrxApp Fix - Use OUTPUT_FILENAME Parameter

## ✅ The Best Solution

Instead of changing your CrxApp's existing naming logic, the **BatchProcessor now passes the exact output filename** you should use!

## 🔧 Simple 2-Line Change

In your `ProcessWithJsonBatch` command, replace these lines:

### **BEFORE** (your current code):
```csharp
// Generate output file name with timestamp
string outputFileName = $"{drawingName}_{timestamp}.json";
string outputFilePath = Path.Combine(outputFolder, outputFileName);
```

### **AFTER** (updated code):
```csharp
// Read output filename from environment variable (set by BatchProcessor)
string outputFileName = Environment.GetEnvironmentVariable("OUTPUT_FILENAME");
if (string.IsNullOrEmpty(outputFileName))
{
    // Fallback: use simple naming without timestamp for batch mode
    outputFileName = $"{drawingName}.json";
}
string outputFilePath = Path.Combine(outputFolder, outputFileName);

AcadDocumentEditorCtrl.LogString($"\nOutput filename: {outputFileName}", LogTag.HEARTBEAT);
```

## 📋 Complete Updated Code Section

Here's your full updated section:

```csharp
try
{
    // Read parameters from environment variables (set by BatchProcessor)
    string inputJsonPath = Environment.GetEnvironmentVariable("INPUT_JSON_PATH");
    string outputFolder = Environment.GetEnvironmentVariable("OUTPUT_FOLDER");
    string outputFileName = Environment.GetEnvironmentVariable("OUTPUT_FILENAME");  // ← NEW!
    string timestamp = Environment.GetEnvironmentVariable("TIMESTAMP");
    string drawingName = Environment.GetEnvironmentVariable("DRAWING_NAME");

    // Validate environment variables
    if (string.IsNullOrEmpty(inputJsonPath))
    {
        AcadDocumentEditorCtrl.LogString("\nERROR: INPUT_JSON_PATH environment variable not set", LogTag.HEARTBEAT);
        return;
    }
    
    if (string.IsNullOrEmpty(outputFolder))
    {
        AcadDocumentEditorCtrl.LogString("\nERROR: OUTPUT_FOLDER environment variable not set", LogTag.HEARTBEAT);
        return;
    }
    
    if (string.IsNullOrEmpty(timestamp))
    {
        timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
    }
    
    if (string.IsNullOrEmpty(drawingName))
    {
        drawingName = Path.GetFileNameWithoutExtension(doc.Name);
    }
    
    // Use output filename from BatchProcessor, or generate one if not provided
    if (string.IsNullOrEmpty(outputFileName))
    {
        // Fallback: simple naming without timestamp for batch mode
        outputFileName = $"{drawingName}.json";
        AcadDocumentEditorCtrl.LogString("\nWARNING: OUTPUT_FILENAME not set, using fallback", LogTag.HEARTBEAT);
    }

    AcadDocumentEditorCtrl.LogString($"\nInput JSON: {inputJsonPath}", LogTag.HEARTBEAT);
    AcadDocumentEditorCtrl.LogString($"\nOutput Folder: {outputFolder}", LogTag.HEARTBEAT);
    AcadDocumentEditorCtrl.LogString($"\nOutput Filename: {outputFileName}", LogTag.HEARTBEAT);  // ← NEW LOG!
    AcadDocumentEditorCtrl.LogString($"\nDrawing Name: {drawingName}", LogTag.HEARTBEAT);

    // Read and parse input JSON
    var parameters = JsonConvert.DeserializeObject<ApplicationParametersClasses.Parameters>(File.ReadAllText(inputJsonPath));
    AcadDocumentEditorCtrl.LogString("\nSuccessfully parsed input JSON", LogTag.HEARTBEAT);

    // Get project type from drawing (not from parameters)
    string projectType = projectUtils.GetProjectType();
    parameters.ProjectType = projectType;
    AcadDocumentEditorCtrl.LogString($"\nDetected Project Type: {projectType}", LogTag.HEARTBEAT);

    // Create output directory
    Directory.CreateDirectory(outputFolder);
    AcadDocumentEditorCtrl.LogString($"\nOutput directory created/verified: {outputFolder}", LogTag.HEARTBEAT);

    // Use the filename provided by BatchProcessor
    string outputFilePath = Path.Combine(outputFolder, outputFileName);  // ← CHANGED!

    AcadDocumentEditorCtrl.LogString($"\nProcessing drawing: {doc.Name}", LogTag.HEARTBEAT);
    AcadDocumentEditorCtrl.LogString($"\nOutput file: {outputFilePath}", LogTag.HEARTBEAT);

    // Process based on project type
    using (var writer = File.CreateText(outputFilePath))
    {
        using (var thread = new HeartbeatManager(progressCallback: msg =>
        {
            Logger.LogMessage(msg);
            AcadDocumentEditorCtrl.LogString(msg, LogTag.HEARTBEAT);
        }, heartbeatIntervalMs: 5000))
        {
            string resultJson = new ProjectReportGenerationCtrl().GetPrescrutinyReportForProjectType(parameters, new List<string>());
            AcadDocumentEditorCtrl.LogString("\nReport generation completed", LogTag.HEARTBEAT);
            writer.Write(resultJson);
        }
    }

    AcadDocumentEditorCtrl.LogString($"\n✅ Processing completed successfully: {outputFileName}", LogTag.HEARTBEAT);
}
catch (System.Exception e)
{
    AcadDocumentEditorCtrl.LogString($"\n❌ Exception in ProcessWithJsonBatch: {e.Message}", LogTag.HEARTBEAT);
    AcadDocumentEditorCtrl.LogException(e);
}
```

## 🎯 What Changed

1. **Added**: Read `OUTPUT_FILENAME` environment variable
2. **Changed**: Use `outputFileName` from parameter instead of generating with timestamp
3. **Added**: Fallback if `OUTPUT_FILENAME` not set (for backward compatibility)
4. **Added**: Log the output filename being used

## 📊 Parameters Now Available

The BatchProcessor now provides these parameters:

| Parameter | Environment Variable | LISP Variable | JSON Param File |
|-----------|---------------------|---------------|-----------------|
| Input JSON path | `INPUT_JSON_PATH` | `BATCH_INPUT_JSON` | `InputJsonPath` |
| Output folder | `OUTPUT_FOLDER` | `BATCH_OUTPUT_FOLDER` | `OutputFolder` |
| **Output filename** | **`OUTPUT_FILENAME`** | **`BATCH_OUTPUT_FILENAME`** | **`OutputFileName`** |
| Drawing name | `DRAWING_NAME` | `BATCH_DRAWING_NAME` | `DrawingName` |
| Timestamp | `TIMESTAMP` | - | `Timestamp` |

## 🚀 Build and Test

### Step 1: Update your CrxApp code
Make the changes above

### Step 2: Rebuild CrxApp
```powershell
cd C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny
dotnet build CrxApp\CrxApp.csproj
```

### Step 3: Start BatchProcessor
```powershell
cd C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0-windows
.\BatchProcessor.exe
```

### Step 4: Process your drawings

You should now see:

```
[DrawingName] ENV: OUTPUT_FOLDER = C:\...\ReportJSONs
[DrawingName] ENV: OUTPUT_FILENAME = DrawingName.json
[DrawingName] ENV: INPUT_JSON_PATH = C:\...\config.json
[DrawingName] ENV: DRAWING_NAME = DrawingName
[DrawingName] Input JSON: C:\...\config.json
[DrawingName] Output Folder: C:\...\ReportJSONs
[DrawingName] Output Filename: DrawingName.json
[DrawingName] Drawing Name: DrawingName
[DrawingName] ✅ Processing completed successfully: DrawingName.json
✅ Completed: DrawingName (4.0s) - JSON created
```

## ✨ Benefits of This Approach

✅ **No breaking changes** - Your existing code still works for non-batch scenarios  
✅ **Flexible naming** - BatchProcessor controls the naming convention  
✅ **Future-proof** - Can change naming without updating CrxApp  
✅ **Backward compatible** - Falls back to simple naming if parameter not provided  
✅ **Clear debugging** - Logs show exactly what filename is being used  

## 🔍 Alternative: Read from Parameter JSON File

If you prefer, you can also read from the JSON parameter file:

```csharp
string paramFilePath = Environment.GetEnvironmentVariable("BATCH_PARAM_FILE");
if (!string.IsNullOrEmpty(paramFilePath) && File.Exists(paramFilePath))
{
    var json = File.ReadAllText(paramFilePath);
    var batchParams = JsonConvert.DeserializeObject<dynamic>(json);
    
    outputFolder = batchParams.OutputFolder;
    outputFileName = batchParams.OutputFileName;
    inputJsonPath = batchParams.InputJsonPath;
    
    AcadDocumentEditorCtrl.LogString($"\n✅ Read parameters from: {paramFilePath}", LogTag.HEARTBEAT);
}
```

The parameter JSON file looks like this:

```json
{
  "InputJsonPath": "C:\\path\\to\\config.json",
  "OutputFolder": "C:\\path\\to\\output",
  "DrawingName": "DrawingName",
  "OutputFileName": "DrawingName.json",
  "OutputFilePath": "C:\\path\\to\\output\\DrawingName.json",
  "Timestamp": "20241124_143025_123"
}
```

## 🎉 Summary

**Change these 3 lines:**

```csharp
// OLD:
string outputFileName = $"{drawingName}_{timestamp}.json";

// NEW:
string outputFileName = Environment.GetEnvironmentVariable("OUTPUT_FILENAME");
if (string.IsNullOrEmpty(outputFileName))
    outputFileName = $"{drawingName}.json";  // Fallback without timestamp
```

That's it! The BatchProcessor will tell your command exactly what filename to use, and the warnings will disappear! 🎊

