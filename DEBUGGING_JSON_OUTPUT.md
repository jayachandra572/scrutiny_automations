# Debugging Missing JSON Output Files

## Problem
Batch processing completes successfully (exit code 0), but output JSON files are not appearing in the output folder.

## Root Cause
The `DrawingBatchProcessor` passes environment variables to AutoCAD, but your **CrxApp.dll** command (`ProcessWithJsonBatch`) must read these environment variables to know where to save JSON files.

## Environment Variables Passed
```
INPUT_JSON_PATH  → Path to your config.json file
OUTPUT_FOLDER    → Where JSON files should be saved
TIMESTAMP        → Unique timestamp for this run
DRAWING_NAME     → Current drawing filename (without .dwg)
```

## Debugging Improvements Added

The code has been updated with enhanced debugging output:

1. **Environment Variable Logging**: Now shows what env vars are being passed to AutoCAD
2. **Output Verification**: Checks if JSON files were actually created after processing
3. **File Listing**: Shows what files exist in output folder if JSON is missing
4. **Console Output**: Always displays AutoCAD console output (not just verbose mode)

## How to Debug

### Step 1: Run the Application with New Debugging
1. Close the current application window
2. The application has been rebuilt with debugging improvements
3. Run the batch processor again

### Step 2: Look for These Debug Messages

**Environment Variables** (logged for each drawing):
```
[DrawingName] ENV: OUTPUT_FOLDER = C:\path\to\output
[DrawingName] ENV: INPUT_JSON_PATH = C:\path\to\config.json
[DrawingName] ENV: DRAWING_NAME = drawing_name
```

**Output Verification** (after processing):
```
✅ Completed: DrawingName (2.5s) - JSON created
   OR
⚠️  Completed: DrawingName (2.5s) - BUT JSON NOT FOUND at: C:\path\to\output\DrawingName.json
🔍 Check if CrxApp command is reading environment variables!
📂 Files in output folder: 0
```

**AutoCAD Console Output** (what your command prints):
```
[DrawingName] <output from your CrxApp command>
```

### Step 3: Check Your CrxApp.dll Command

Your AutoCAD command **MUST** read environment variables. In C# AutoCAD plugin:

```csharp
[CommandMethod("ProcessWithJsonBatch")]
public void ProcessWithJsonBatch()
{
    // ✅ CORRECT: Read environment variables
    string outputFolder = Environment.GetEnvironmentVariable("OUTPUT_FOLDER");
    string inputJsonPath = Environment.GetEnvironmentVariable("INPUT_JSON_PATH");
    string drawingName = Environment.GetEnvironmentVariable("DRAWING_NAME");
    
    // Verify they exist
    if (string.IsNullOrEmpty(outputFolder))
    {
        Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
        ed.WriteMessage("\n❌ ERROR: OUTPUT_FOLDER environment variable not set!");
        return;
    }
    
    // Build output path
    string outputPath = Path.Combine(outputFolder, $"{drawingName}.json");
    
    // ... your processing logic ...
    
    // Save JSON to outputPath
    File.WriteAllText(outputPath, jsonContent);
    
    ed.WriteMessage($"\n✅ Saved: {outputPath}");
}
```

### Step 4: Common Issues & Solutions

#### Issue 1: Environment Variables Not Being Read
**Symptoms**: Warning message "JSON NOT FOUND"

**Solution**: 
- Ensure your CrxApp command uses `Environment.GetEnvironmentVariable()` (not hardcoded paths)
- Add debug output in your command to verify vars are received
- Print the values to AutoCAD console for verification

#### Issue 2: Wrong Output Path
**Symptoms**: Files created but in wrong location

**Solution**:
- Check if your command has hardcoded paths
- Verify `outputFolder` variable is actually being used
- Look at "Files in output folder" count in debug output

#### Issue 3: Permission Issues
**Symptoms**: Access denied errors in AutoCAD output

**Solution**:
- Check folder permissions
- Try a different output folder (e.g., Desktop for testing)
- Run as administrator if needed

#### Issue 4: Command Not Executing
**Symptoms**: No AutoCAD console output at all

**Solution**:
- Verify DLL is loading correctly (check for NETLOAD errors)
- Ensure command name matches exactly: `ProcessWithJsonBatch`
- Check if command exists in CrxApp.dll

### Step 5: Test with Verbose Logging

Enable "Verbose Logging" checkbox in the UI to see:
- Script file contents
- All DLL load commands
- Detailed AutoCAD startup output

## Expected Output (When Working Correctly)

```
╔══════════════════════════════════════════════════════════════╗
║  AutoCAD Batch Processor (Standalone Mode)                  ║
╚══════════════════════════════════════════════════════════════╝

📁 Input Folder:  C:\...\ventilation_test_cases
📁 Output Folder: C:\...\ReportJSONs
📋 Input JSON:    C:\...\config.json
📄 Total Files:   4
────────────────────────────────────────────────────────────

[20251124_143522_123] ⏳ Processing: Drawing1
  [Drawing1] ENV: OUTPUT_FOLDER = C:\...\ReportJSONs
  [Drawing1] ENV: INPUT_JSON_PATH = C:\...\config.json
  [Drawing1] ENV: DRAWING_NAME = Drawing1
  [Drawing1] Loading config from: C:\...\config.json
  [Drawing1] Processing ventilation objects...
  [Drawing1] ✅ Saved: C:\...\ReportJSONs\Drawing1.json
[20251124_143524_456] ✅ Completed: Drawing1 (2.3s) - JSON created
```

## Next Steps

1. **Run the updated application**
2. **Look at the console output** in the log window
3. **Identify which issue** matches your symptoms
4. **Update your CrxApp.dll command** if needed
5. **Test with a single drawing** first

## Need More Help?

If JSON files still don't appear:
1. Share the console output from the log window
2. Check if your CrxApp command has the environment variable code
3. Verify the command is actually running (should see output)
4. Try running a single drawing manually in AutoCAD to test

