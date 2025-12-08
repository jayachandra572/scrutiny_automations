# CrxApp Command Update Guide - Fix Missing JSON Outputs

## Problem Summary
Your `ProcessWithJsonBatch` command worked before when run manually, but now the Batch Processor completes successfully WITHOUT creating JSON output files.

## Root Cause
The batch processor now passes parameters through **3 different methods** (for maximum compatibility):
1. **Environment Variables** - `OUTPUT_FOLDER`, `INPUT_JSON_PATH`, `DRAWING_NAME`
2. **LISP Variables** - `BATCH_OUTPUT_FOLDER`, `BATCH_INPUT_JSON`, `BATCH_DRAWING_NAME`, `BATCH_PARAM_FILE`
3. **JSON Parameter File** - A temp JSON file with all parameters

Your AutoCAD command needs to read these parameters to know where to save output files.

## Solution: Update Your CrxApp Command

### Method 1: Read LISP Variables (Recommended - Most Reliable)

Update your `ProcessWithJsonBatch` command in CrxApp to read LISP variables:

```csharp
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.IO;

[CommandMethod("ProcessWithJsonBatch")]
public void ProcessWithJsonBatch()
{
    Document doc = Application.DocumentManager.MdiActiveDocument;
    Editor ed = doc.Editor;
    
    try
    {
        // Method 1: Read from LISP variables (set by batch processor script)
        string outputFolder = GetLispVariable("BATCH_OUTPUT_FOLDER");
        string inputJsonPath = GetLispVariable("BATCH_INPUT_JSON");
        string drawingName = GetLispVariable("BATCH_DRAWING_NAME");
        
        // Fallback to environment variables if LISP vars not set
        if (string.IsNullOrEmpty(outputFolder))
        {
            outputFolder = Environment.GetEnvironmentVariable("OUTPUT_FOLDER");
            inputJsonPath = Environment.GetEnvironmentVariable("INPUT_JSON_PATH");
            drawingName = Environment.GetEnvironmentVariable("DRAWING_NAME");
        }
        
        // Validate we have the required parameters
        if (string.IsNullOrEmpty(outputFolder))
        {
            ed.WriteMessage("\n❌ ERROR: OUTPUT_FOLDER not set! Cannot save JSON.");
            ed.WriteMessage("\n   Batch processor may not be passing parameters correctly.");
            return;
        }
        
        if (string.IsNullOrEmpty(drawingName))
        {
            // Fallback: use current drawing name
            drawingName = Path.GetFileNameWithoutExtension(doc.Name);
        }
        
        // Log what we received (helpful for debugging)
        ed.WriteMessage($"\n✅ Received parameters:");
        ed.WriteMessage($"\n   Output Folder: {outputFolder}");
        ed.WriteMessage($"\n   Input JSON: {inputJsonPath}");
        ed.WriteMessage($"\n   Drawing Name: {drawingName}");
        
        // YOUR EXISTING PROCESSING LOGIC HERE
        // ... process the drawing ...
        // ... generate your JSON content ...
        
        // Build output path
        string outputPath = Path.Combine(outputFolder, $"{drawingName}.json");
        
        // CRITICAL: Save JSON to the output path
        string jsonContent = GenerateYourJsonContent(); // Your existing method
        File.WriteAllText(outputPath, jsonContent);
        
        ed.WriteMessage($"\n✅ Saved JSON: {outputPath}");
    }
    catch (Exception ex)
    {
        ed.WriteMessage($"\n❌ Error in ProcessWithJsonBatch: {ex.Message}");
        ed.WriteMessage($"\n   Stack: {ex.StackTrace}");
    }
}

// Helper method to read LISP variables
private string GetLispVariable(string variableName)
{
    try
    {
        Document doc = Application.DocumentManager.MdiActiveDocument;
        
        // Use AutoLISP to get the variable value
        object result = Application.Invoke(
            new Autodesk.AutoCAD.Interop.Common.AcadDocument(), 
            "Eval",
            variableName
        );
        
        return result?.ToString() ?? string.Empty;
    }
    catch
    {
        // Variable not set
        return string.Empty;
    }
}
```

### Method 2: Read from JSON Parameter File (Alternative)

The batch processor also creates a JSON file with all parameters:

```csharp
[CommandMethod("ProcessWithJsonBatch")]
public void ProcessWithJsonBatch()
{
    Document doc = Application.DocumentManager.MdiActiveDocument;
    Editor ed = doc.Editor;
    
    try
    {
        // Get path to parameter file from LISP variable
        string paramFilePath = GetLispVariable("BATCH_PARAM_FILE");
        
        if (!string.IsNullOrEmpty(paramFilePath) && File.Exists(paramFilePath))
        {
            // Read parameters from JSON file
            string json = File.ReadAllText(paramFilePath);
            var parameters = JsonConvert.DeserializeObject<BatchParameters>(json);
            
            string outputFolder = parameters.OutputFolder;
            string inputJsonPath = parameters.InputJsonPath;
            string drawingName = parameters.DrawingName;
            
            ed.WriteMessage($"\n✅ Read parameters from: {paramFilePath}");
            
            // YOUR PROCESSING LOGIC HERE
            // ... process drawing ...
            
            // Save output
            string outputPath = Path.Combine(outputFolder, $"{drawingName}.json");
            File.WriteAllText(outputPath, yourJsonContent);
            
            ed.WriteMessage($"\n✅ Saved: {outputPath}");
        }
        else
        {
            ed.WriteMessage("\n❌ Parameter file not found!");
        }
    }
    catch (Exception ex)
    {
        ed.WriteMessage($"\n❌ Error: {ex.Message}");
    }
}

// Parameter class
public class BatchParameters
{
    public string InputJsonPath { get; set; }
    public string OutputFolder { get; set; }
    public string DrawingName { get; set; }
    public string Timestamp { get; set; }
}
```

### Method 3: Simplified LISP Variable Reader

If the COM Interop approach doesn't work, use this simpler method:

```csharp
private string GetLispVariable(string variableName)
{
    try
    {
        Document doc = Application.DocumentManager.MdiActiveDocument;
        
        // Execute LISP command to print variable
        object result = doc.GetAcadDocument().Eval(variableName);
        
        if (result != null && result.ToString() != "nil")
        {
            return result.ToString();
        }
    }
    catch (Exception ex)
    {
        // Variable doesn't exist or error reading
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
            $"\n⚠️  Could not read LISP var {variableName}: {ex.Message}"
        );
    }
    
    return string.Empty;
}
```

## What the Batch Processor Now Does

When processing each drawing, the AutoCAD script file contains:

```autocad
NETLOAD "C:\path\to\CommonUtils.dll"
NETLOAD "C:\path\to\Newtonsoft.Json.dll"
NETLOAD "C:\path\to\CrxApp.dll"
(setq BATCH_INPUT_JSON "C:\\path\\to\\config.json")
(setq BATCH_OUTPUT_FOLDER "C:\\path\\to\\output")
(setq BATCH_DRAWING_NAME "DrawingName")
(setq BATCH_PARAM_FILE "C:\\temp\\params_20241124_143025_123_DrawingName.json")
ProcessWithJsonBatch
QUIT
```

The parameter JSON file contains:

```json
{
  "InputJsonPath": "C:\\path\\to\\config.json",
  "OutputFolder": "C:\\path\\to\\output",
  "DrawingName": "DrawingName",
  "Timestamp": "20241124_143025_123"
}
```

## Testing Your Updated Command

### Step 1: Update CrxApp Code

1. Open your CrxApp project in Visual Studio
2. Find your `ProcessWithJsonBatch` command
3. Add the parameter reading code (use Method 1 above)
4. Build the project

### Step 2: Test with Batch Processor

1. Close any running BatchProcessor instances
2. Run the updated batch processor:

```powershell
cd C:\Users\Jaya\Desktop\scrutiny_projects\BatchProcessor\bin\Debug\net8.0-windows
.\BatchProcessor.exe
```

### Step 3: Check the Output

Look for these new messages in the log:

```
[DrawingName] 📝 Parameter file: C:\temp\params_...json
[DrawingName] ENV: OUTPUT_FOLDER = C:\path\to\output
[DrawingName] ENV: INPUT_JSON_PATH = C:\path\to\config.json
[DrawingName] ENV: DRAWING_NAME = DrawingName
[DrawingName] ✅ Received parameters:
[DrawingName]    Output Folder: C:\path\to\output
[DrawingName]    Input JSON: C:\path\to\config.json  
[DrawingName]    Drawing Name: DrawingName
[DrawingName] ✅ Saved JSON: C:\path\to\output\DrawingName.json
✅ Completed: DrawingName (2.3s) - JSON created
```

If you see "⚠️ Completed ... BUT JSON NOT FOUND", then your command isn't reading the parameters yet.

## Quick Fix: Minimal Code Change

If you want the MINIMAL change to your existing code, just add this at the start of your command:

```csharp
[CommandMethod("ProcessWithJsonBatch")]
public void ProcessWithJsonBatch()
{
    Document doc = Application.DocumentManager.MdiActiveDocument;
    Editor ed = doc.Editor;
    
    // ===== ADD THIS BLOCK =====
    string batchOutputFolder = GetLispVariable("BATCH_OUTPUT_FOLDER");
    string batchInputJson = GetLispVariable("BATCH_INPUT_JSON");
    string batchDrawingName = GetLispVariable("BATCH_DRAWING_NAME");
    
    // Fallback to environment variables
    if (string.IsNullOrEmpty(batchOutputFolder))
    {
        batchOutputFolder = Environment.GetEnvironmentVariable("OUTPUT_FOLDER");
        batchInputJson = Environment.GetEnvironmentVariable("INPUT_JSON_PATH");
        batchDrawingName = Environment.GetEnvironmentVariable("DRAWING_NAME");
    }
    
    // If still empty, use fallback defaults
    if (string.IsNullOrEmpty(batchOutputFolder))
    {
        batchOutputFolder = @"C:\default\output";  // Your default
    }
    if (string.IsNullOrEmpty(batchDrawingName))
    {
        batchDrawingName = Path.GetFileNameWithoutExtension(doc.Name);
    }
    
    ed.WriteMessage($"\n📁 Output will be saved to: {batchOutputFolder}");
    // ===== END OF NEW BLOCK =====
    
    // YOUR EXISTING CODE...
    // Just make sure you use batchOutputFolder and batchDrawingName 
    // when constructing your output path:
    
    string outputJsonPath = Path.Combine(batchOutputFolder, $"{batchDrawingName}.json");
    
    // ... rest of your existing processing logic ...
    
    File.WriteAllText(outputJsonPath, yourJsonContent);
    ed.WriteMessage($"\n✅ Saved: {outputJsonPath}");
}
```

## Common Issues

### Issue 1: "Cannot access Eval method"

**Solution**: Use COM Interop or environment variables instead:

```csharp
string outputFolder = Environment.GetEnvironmentVariable("OUTPUT_FOLDER");
```

### Issue 2: "LISP variables are nil/empty"

**Solution**: The batch processor is working. Check:
- Are you running through the NEW batch processor?
- Check the script file in temp folder to verify LISP vars are being set

### Issue 3: "Still no JSON files created"

**Solution**:
1. Add `ed.WriteMessage()` statements everywhere in your command
2. Check the batch processor log - do you see YOUR messages?
3. If no messages, the command isn't running at all
4. If messages appear but no file, check file permissions on output folder

## Verification Checklist

- [ ] Updated CrxApp command to read LISP variables or environment variables
- [ ] Added logging to show what parameters were received
- [ ] Rebuilt CrxApp.dll
- [ ] Closed old Batch

Processor
- [ ] Ran new BatchProcessor
- [ ] Checked log output shows parameter values
- [ ] Verified JSON files are created in output folder

## Need More Help?

If JSON files still aren't being created:
1. Share the console output from the batch processor
2. Add `ed.WriteMessage()` logging throughout your command
3. Check if your command is actually executing (should see your log messages)
4. Verify output folder path is correct and has write permissions

---

**Key Takeaway**: The batch processor now passes parameters 3 different ways. Your command just needs to READ them and use them when saving the JSON file!

