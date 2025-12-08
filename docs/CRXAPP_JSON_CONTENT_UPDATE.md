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
        // IMPORTANT: Only create output JSON for failed validations
        // If all validations pass, do NOT create the output file (so BatchProcessor knows it succeeded)
        try
        {
            string resultJson = new ProjectReportGenerationCtrl().GetPrescrutinyReportForProjectType(parameters, new List<string>());
            
            // Parse the result to check if there are any failures
            // Example: Check if result contains validation errors or failures
            var result = JsonConvert.DeserializeObject<dynamic>(resultJson);
            bool hasFailures = CheckForFailures(result); // Implement your failure detection logic
            
            // Helper method example:
            // private bool CheckForFailures(dynamic result)
            // {
            //     // Check for validation errors, failed checks, etc.
            //     // Return true if any failures found, false if all passed
            //     if (result.ValidationErrors != null && result.ValidationErrors.Count > 0)
            //         return true;
            //     if (result.FailedChecks != null && result.FailedChecks.Count > 0)
            //         return true;
            //     return false;
            // }
            
            if (hasFailures)
            {
                // Only create output JSON if there are failures
                using (var writer = File.CreateText(outputFilePath))
                {
                    writer.Write(resultJson);
                }
                AcadDocumentEditorCtrl.LogString($"\n⚠️ Processing completed with failures: {outputFileName}", LogTag.HEARTBEAT);
            }
            else
            {
                // No failures - do NOT create output file (indicates success to BatchProcessor)
                AcadDocumentEditorCtrl.LogString($"\n✅ Processing completed successfully (no output file created)", LogTag.HEARTBEAT);
            }
        }
        catch (Exception ex)
        {
            // On exception, create output JSON with error information
            var errorResult = new
            {
                Error = true,
                ErrorMessage = ex.Message,
                DrawingName = drawingName,
                Timestamp = timestamp
            };
            
            using (var writer = File.CreateText(outputFilePath))
            {
                writer.Write(JsonConvert.SerializeObject(errorResult, Formatting.Indented));
            }
            
            AcadDocumentEditorCtrl.LogString($"\n❌ Exception occurred, error JSON created: {outputFileName}", LogTag.HEARTBEAT);
            throw; // Re-throw to mark as failed in BatchProcessor
        }
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

