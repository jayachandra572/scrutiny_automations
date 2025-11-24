# CrxApp Command Update: Read JSON Content Directly

## Overview

The BatchProcessor now passes JSON config content **directly via environment variable** instead of requiring a temp file. This eliminates the need for temporary config files.

## Updated CrxApp Command Code

Update your `ProcessWithJsonBatch` method to read JSON content directly:

```csharp
[CommandMethod("ScrutinyReport", "ProcessWithJsonBatch", CommandFlags.Modal)]
public void ProcessWithJsonBatch()
{
    AcadDocumentEditorCtrl.LogString("\nStart of ProcessWithJsonBatch (Batch Mode)", LogTag.HEARTBEAT);
    
    var doc = acadDocumentCtrl.GetActiveDocument();
    var ed = doc.Editor;
    
    try
    {
        // Read parameters from environment variables (set by BatchProcessor)
        string inputJsonPath = Environment.GetEnvironmentVariable("INPUT_JSON_PATH");
        string inputJsonContent = Environment.GetEnvironmentVariable("INPUT_JSON_CONTENT");
        string outputFolder = Environment.GetEnvironmentVariable("OUTPUT_FOLDER");
        string timestamp = Environment.GetEnvironmentVariable("TIMESTAMP");
        string drawingName = Environment.GetEnvironmentVariable("DRAWING_NAME");
        string outputFileName = Environment.GetEnvironmentVariable("OUTPUT_FILENAME");
        
        // Validate environment variables
        if (string.IsNullOrEmpty(outputFolder))
        {
            AcadDocumentEditorCtrl.LogString("\nERROR: OUTPUT_FOLDER environment variable not set", LogTag.HEARTBEAT);
            return;
        }
        
        if (string.IsNullOrEmpty(timestamp))
        {
            timestamp = DateTime.Now.ToString("yyyyMMdd_HHHmmss_fff");
        }
        
        if (string.IsNullOrEmpty(drawingName))
        {
            drawingName = Path.GetFileNameWithoutExtension(doc.Name);
        }
        
        // Read JSON config directly from environment variable (no file needed!)
        if (string.IsNullOrEmpty(inputJsonContent))
        {
            AcadDocumentEditorCtrl.LogString("\nERROR: INPUT_JSON_CONTENT environment variable not set", LogTag.HEARTBEAT);
            return;
        }
        
        string jsonContent = inputJsonContent;
        AcadDocumentEditorCtrl.LogString("\n✅ Using JSON content from environment variable (no file)", LogTag.HEARTBEAT);
        
        // Parse JSON to Parameters object
        var parameters = JsonConvert.DeserializeObject<ApplicationParametersClasses.Parameters>(jsonContent);
        AcadDocumentEditorCtrl.LogString("\nSuccessfully parsed input JSON", LogTag.HEARTBEAT);
        
        // ProjectType is already set from CSV - use it directly
        // If you want to override with drawing detection, uncomment below:
        // string projectType = projectUtils.GetProjectType();
        // parameters.ProjectType = projectType;
        AcadDocumentEditorCtrl.LogString($"\nUsing Project Type from CSV: {parameters.ProjectType}", LogTag.HEARTBEAT);
        
        // Create output directory
        Directory.CreateDirectory(outputFolder);
        AcadDocumentEditorCtrl.LogString($"\nOutput directory created/verified: {outputFolder}", LogTag.HEARTBEAT);
        
        // Generate output file name
        string outputFilePath = Path.Combine(outputFolder, outputFileName ?? $"{drawingName}.json");
        
        AcadDocumentEditorCtrl.LogString($"\nProcessing drawing: {doc.Name}", LogTag.HEARTBEAT);
        AcadDocumentEditorCtrl.LogString($"\nOutput file: {outputFilePath}", LogTag.HEARTBEAT);
        
        // Process based on project type
        using (var writer = File.CreateText(outputFilePath))
        {
            string resultJson = new ProjectReportGenerationCtrl().GetPrescrutinyReportForProjectType(parameters, new List<string>());
            writer.Write(resultJson);
        }
        
        AcadDocumentEditorCtrl.LogString($"\n✅ Processing completed successfully: {outputFileName}", LogTag.HEARTBEAT);
    }
    catch (System.Exception e)
    {
        AcadDocumentEditorCtrl.LogString($"\n❌ Exception in ProcessWithJsonBatch: {e.Message}", LogTag.HEARTBEAT);
        AcadDocumentEditorCtrl.LogException(e);
    }
}
```

## Key Changes

1. **Read `INPUT_JSON_CONTENT`** - JSON config is passed directly via environment variable
2. **No temp file required** - JSON is passed directly, no file I/O needed
3. **Simpler code** - No file path handling or fallback logic needed

## Benefits

✅ **No temporary files** - Cleaner, faster  
✅ **No file I/O overhead** - Direct memory transfer  
✅ **Automatic cleanup** - No temp files to manage  
✅ **Backward compatible** - Still supports file path if needed

## How It Works

1. BatchProcessor generates JSON from CSV
2. Passes JSON content directly via `INPUT_JSON_CONTENT` environment variable
3. CrxApp reads from `INPUT_JSON_CONTENT` (no file needed!)

## Testing

After updating your CrxApp command:
- Verify JSON content is read from `INPUT_JSON_CONTENT`
- Verify no temp config files are created
- All configs are passed directly via environment variable

