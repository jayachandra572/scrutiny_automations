# Quick Fix - JSON Files Not Being Created

## ✅ Good News: Batch Processor is Working!

You're seeing this message:
```
⚠️ Completed: B2_B1_Multiple_buildings_only basement4 (4.0s) - BUT JSON NOT FOUND
```

This means:
- ✅ AutoCAD is starting successfully
- ✅ DLLs are loading correctly  
- ✅ Your command is running (exit code 0)
- ❌ BUT the command isn't creating JSON files

## 🔍 The Problem

Your `ProcessWithJsonBatch` command is running but **not saving JSON files to the correct location** because it's not reading the parameters from the batch processor.

## 🚀 Quick Fix - Update Your CrxApp Command

### Option 1: Use Environment Variables (Easiest)

Open your CrxApp project and update your `ProcessWithJsonBatch` command:

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
        // READ PARAMETERS FROM ENVIRONMENT VARIABLES
        string outputFolder = Environment.GetEnvironmentVariable("OUTPUT_FOLDER");
        string inputJsonPath = Environment.GetEnvironmentVariable("INPUT_JSON_PATH");
        string drawingName = Environment.GetEnvironmentVariable("DRAWING_NAME");
        
        // Fallback if not set
        if (string.IsNullOrEmpty(drawingName))
        {
            drawingName = Path.GetFileNameWithoutExtension(doc.Name);
        }
        
        // CRITICAL: Log what we received (for debugging)
        ed.WriteMessage($"\n========== BATCH PROCESSING DEBUG ==========");
        ed.WriteMessage($"\n📁 Output Folder: {outputFolder ?? "NOT SET"}");
        ed.WriteMessage($"\n📋 Input JSON: {inputJsonPath ?? "NOT SET"}");
        ed.WriteMessage($"\n📄 Drawing Name: {drawingName}");
        ed.WriteMessage($"\n==========================================\n");
        
        // Check if we have output folder
        if (string.IsNullOrEmpty(outputFolder))
        {
            ed.WriteMessage("\n❌ ERROR: OUTPUT_FOLDER environment variable not set!");
            ed.WriteMessage("\n   This command must be run through the BatchProcessor.");
            ed.WriteMessage("\n   Cannot save JSON file without knowing where to save it.\n");
            return;
        }
        
        // Create output folder if it doesn't exist
        if (!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
            ed.WriteMessage($"\n📁 Created output folder: {outputFolder}");
        }
        
        // ==== YOUR EXISTING PROCESSING LOGIC HERE ====
        ed.WriteMessage("\n🔄 Processing drawing...");
        
        // Example: Read your config JSON
        string configContent = "";
        if (!string.IsNullOrEmpty(inputJsonPath) && File.Exists(inputJsonPath))
        {
            configContent = File.ReadAllText(inputJsonPath);
            ed.WriteMessage($"\n✅ Loaded config from: {inputJsonPath}");
        }
        
        // Do your actual processing here...
        // string jsonResult = YourProcessingMethod(doc, configContent);
        
        // For now, create a test JSON to verify it works
        string jsonResult = @"{
  ""DrawingName"": """ + drawingName + @""",
  ""ProcessedAt"": """ + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + @""",
  ""Status"": ""Success"",
  ""Message"": ""Processed by BatchProcessor""
}";
        
        // ==== END OF YOUR PROCESSING LOGIC ====
        
        // BUILD OUTPUT PATH using the parameters we received
        string outputPath = Path.Combine(outputFolder, $"{drawingName}.json");
        
        // SAVE THE JSON FILE
        File.WriteAllText(outputPath, jsonResult);
        
        ed.WriteMessage($"\n✅ SUCCESS! JSON saved to: {outputPath}");
        ed.WriteMessage($"\n✅ File size: {new FileInfo(outputPath).Length} bytes\n");
    }
    catch (System.Exception ex)
    {
        ed.WriteMessage($"\n❌ ERROR in ProcessWithJsonBatch: {ex.Message}");
        ed.WriteMessage($"\n   Stack Trace: {ex.StackTrace}\n");
        throw; // Re-throw so AutoCAD exit code is non-zero
    }
}
```

### Option 2: Use LISP Variables (More Reliable)

If environment variables don't work, use LISP variables:

```csharp
[CommandMethod("ProcessWithJsonBatch")]
public void ProcessWithJsonBatch()
{
    Document doc = Application.DocumentManager.MdiActiveDocument;
    Editor ed = doc.Editor;
    
    try
    {
        // Try to read from LISP variables first
        string outputFolder = GetLispVariable("BATCH_OUTPUT_FOLDER");
        string inputJsonPath = GetLispVariable("BATCH_INPUT_JSON");
        string drawingName = GetLispVariable("BATCH_DRAWING_NAME");
        
        // Fallback to environment variables if LISP vars not found
        if (string.IsNullOrEmpty(outputFolder))
        {
            outputFolder = Environment.GetEnvironmentVariable("OUTPUT_FOLDER");
            inputJsonPath = Environment.GetEnvironmentVariable("INPUT_JSON_PATH");
            drawingName = Environment.GetEnvironmentVariable("DRAWING_NAME");
            ed.WriteMessage("\n⚠️  Using environment variables (LISP vars not found)");
        }
        else
        {
            ed.WriteMessage("\n✅ Using LISP variables");
        }
        
        if (string.IsNullOrEmpty(drawingName))
        {
            drawingName = Path.GetFileNameWithoutExtension(doc.Name);
        }
        
        ed.WriteMessage($"\n📁 Output: {outputFolder}");
        ed.WriteMessage($"\n📋 Config: {inputJsonPath}");
        ed.WriteMessage($"\n📄 Drawing: {drawingName}\n");
        
        if (string.IsNullOrEmpty(outputFolder))
        {
            ed.WriteMessage("\n❌ ERROR: No output folder specified!\n");
            return;
        }
        
        // Your processing logic...
        string jsonContent = "{ \"test\": \"success\" }";
        
        // Save JSON
        string outputPath = Path.Combine(outputFolder, $"{drawingName}.json");
        File.WriteAllText(outputPath, jsonContent);
        
        ed.WriteMessage($"\n✅ Saved: {outputPath}\n");
    }
    catch (Exception ex)
    {
        ed.WriteMessage($"\n❌ Error: {ex.Message}\n");
        throw;
    }
}

// Helper method to read LISP variables
private string GetLispVariable(string varName)
{
    try
    {
        Document doc = Application.DocumentManager.MdiActiveDocument;
        
        // Try to evaluate the LISP variable
        var acadDoc = doc.GetAcadDocument();
        object result = acadDoc.Eval(varName);
        
        if (result != null && result.ToString() != "nil")
        {
            return result.ToString();
        }
    }
    catch
    {
        // Variable doesn't exist or can't be read
    }
    
    return string.Empty;
}
```

## 📋 Step-by-Step Instructions

### Step 1: Update CrxApp Code

1. Open your CrxApp project in Visual Studio
2. Find the file containing `ProcessWithJsonBatch` command
3. Replace the command with one of the options above
4. **Important**: Keep your existing processing logic, just add the parameter reading at the start

### Step 2: Rebuild CrxApp

```powershell
cd C:\Users\Jaya\Desktop\scrutiny_projects\scrutiny
dotnet build CrxApp\CrxApp.csproj
```

### Step 3: Test Again

The BatchProcessor is already running. Just use it to process your drawings again.

### Step 4: Check the Output

You should now see:

```
[DrawingName] 📁 Output Folder: C:\Users\Jaya\Documents\UrbanFloww\drawing_files\ReportJSONs
[DrawingName] 📋 Input JSON: C:\Users\Jaya\Documents\UrbanFloww\drawing_files\config.json
[DrawingName] 📄 Drawing Name: DrawingName
[DrawingName] ✅ SUCCESS! JSON saved to: C:\...\DrawingName.json
✅ Completed: DrawingName (2.5s) - JSON created
```

## 🐛 If It Still Doesn't Work

### Check 1: Are you seeing your debug messages?

Look in the BatchProcessor log. Do you see lines like:
```
[DrawingName] ========== BATCH PROCESSING DEBUG ==========
[DrawingName] 📁 Output Folder: ...
```

- **YES** = Command is running, check if output folder path is correct
- **NO** = Command isn't executing, check DLL loading

### Check 2: Enable Verbose Logging

In the BatchProcessor UI, enable "Verbose Logging" checkbox and run again. This will show you:
- The exact script being generated
- All AutoCAD console output
- Parameter file contents

### Check 3: Check Output Folder Permissions

```powershell
# Test if you can write to the output folder
New-Item "C:\Users\Jaya\Documents\UrbanFloww\drawing_files\ReportJSONs\test.txt" -ItemType File -Force
```

If this fails, you don't have write permissions.

## 🎯 Testing Just the Parameter Passing

Create a minimal test command to verify parameters are being passed:

```csharp
[CommandMethod("TestBatchParams")]
public void TestBatchParams()
{
    Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
    
    ed.WriteMessage("\n========== TESTING BATCH PARAMETERS ==========");
    
    // Test Environment Variables
    ed.WriteMessage("\n\nEnvironment Variables:");
    ed.WriteMessage($"\n  OUTPUT_FOLDER = {Environment.GetEnvironmentVariable("OUTPUT_FOLDER") ?? "NOT SET"}");
    ed.WriteMessage($"\n  INPUT_JSON_PATH = {Environment.GetEnvironmentVariable("INPUT_JSON_PATH") ?? "NOT SET"}");
    ed.WriteMessage($"\n  DRAWING_NAME = {Environment.GetEnvironmentVariable("DRAWING_NAME") ?? "NOT SET"}");
    
    // Test LISP Variables
    ed.WriteMessage("\n\nLISP Variables:");
    ed.WriteMessage($"\n  BATCH_OUTPUT_FOLDER = {GetLispVariable("BATCH_OUTPUT_FOLDER") ?? "NOT SET"}");
    ed.WriteMessage($"\n  BATCH_INPUT_JSON = {GetLispVariable("BATCH_INPUT_JSON") ?? "NOT SET"}");
    ed.WriteMessage($"\n  BATCH_DRAWING_NAME = {GetLispVariable("BATCH_DRAWING_NAME") ?? "NOT SET"}");
    
    ed.WriteMessage("\n\n==============================================\n");
}
```

Then in your batch processor, temporarily change the command to `TestBatchParams` to see what's being passed.

## 📞 Common Issues

### "Environment variables are empty"
- Environment variables may not persist in accoreconsole.exe
- Use LISP variables instead (Option 2)

### "LISP variables are nil"
- Check the script file being generated
- Enable verbose logging in BatchProcessor
- Verify script contains `(setq BATCH_OUTPUT_FOLDER ...)`

### "Access denied when saving"
- Output folder doesn't have write permissions
- Try a different output folder (e.g., your Desktop)

### "Command doesn't seem to run"
- Check DLL is loading correctly
- Look for NETLOAD errors in the output
- Make sure command name matches exactly: `ProcessWithJsonBatch`

## ✅ Success Checklist

- [ ] Updated CrxApp command to read parameters
- [ ] Added debug logging to show what parameters are received
- [ ] Rebuilt CrxApp.dll
- [ ] Tested with BatchProcessor
- [ ] Saw debug messages in the log
- [ ] JSON files created in output folder
- [ ] No more "JSON NOT FOUND" warnings

## 🎉 Once It Works

After you fix the command, the warnings will change from:

❌ `⚠️ Completed: Drawing (4.0s) - BUT JSON NOT FOUND`

To:

✅ `✅ Completed: Drawing (4.0s) - JSON created`

---

**Need the CrxApp source?** The BatchProcessor is working perfectly - you just need to update your CrxApp command to read the parameters it's passing!

