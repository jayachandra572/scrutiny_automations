# JSON Diff Comparison Feature - Implementation Plan

## Overview
This document outlines the plan to implement a JSON diff comparison feature in the BatchProcessor application. The feature will compare JSON files between two folders (reference and latest) and display differences.

## Requirements

### Functional Requirements
1. **Input**: Two folder paths
   - Reference folder (baseline/expected JSON files)
   - Latest folder (current/actual JSON files)

2. **Comparison Logic**:
   - Match JSON files by base filename (ignoring timestamps)
   - Compare JSON structure and values
   - Identify added, removed, and modified properties
   - Handle nested objects and arrays
   - Support deep comparison

3. **Output**:
   - Visual display of differences in the UI
   - Summary statistics (total files, matches, differences)
   - **Automatic file output**: Diff report automatically saved to output folder when differences are found
     - JSON format (structured, machine-readable)
     - Text format (human-readable, easy to review)
     - Timestamped filename (e.g., `diff_report_20241122_143025.json`)
   - Optional manual export (HTML/CSV) for additional formats

### Non-Functional Requirements
- Performance: Handle large JSON files efficiently
- User Experience: Clear, intuitive UI with color-coded differences
- Error Handling: Graceful handling of malformed JSON files
- Progress Feedback: Show progress during comparison

---

## Architecture

### Components

#### 1. **JsonDiffComparer.cs** (Core Service)
- **Location**: Root directory
- **Responsibilities**:
  - Compare two JSON files
  - Compare two folders of JSON files
  - Generate diff results
  - Handle file matching logic

**Key Methods**:
```csharp
public class JsonDiffComparer
{
    // Compare single JSON files
    public JsonDiffResult CompareFiles(string referencePath, string latestPath);
    
    // Compare folders and optionally save results to output folder
    public DiffReport CompareFolders(string referenceFolder, string latestFolder, string outputFolder = null);
    
    // Match files by base name (ignore timestamps)
    private string GetBaseFileName(string fullPath);
    
    // Deep JSON comparison
    private JsonDiffResult CompareJsonObjects(JsonElement reference, JsonElement latest, string path = "");
}
```

#### 2. **Data Models**
- **Location**: `Models/` directory (new)

**JsonDiffResult.cs**:
```csharp
public class JsonDiffResult
{
    public string FileName { get; set; }
    public bool FilesMatch { get; set; }
    public List<PropertyDifference> Differences { get; set; }
    public string ErrorMessage { get; set; }
}

public class PropertyDifference
{
    public string Path { get; set; }  // JSON path like "plotData.plotAreaData.grossPlotArea"
    public DifferenceType Type { get; set; }  // Added, Removed, Modified
    public object ReferenceValue { get; set; }
    public object LatestValue { get; set; }
}

public enum DifferenceType
{
    Added,      // Property exists in latest but not in reference
    Removed,    // Property exists in reference but not in latest
    Modified    // Property exists in both but values differ
}
```

**DiffReport.cs**:
```csharp
public class DiffReport
{
    public string ReferenceFolder { get; set; }
    public string LatestFolder { get; set; }
    public DateTime ComparisonDate { get; set; }
    public int TotalFiles { get; set; }
    public int MatchingFiles { get; set; }
    public int DifferentFiles { get; set; }
    public int MissingInLatest { get; set; }
    public int MissingInReference { get; set; }
    public List<JsonDiffResult> FileResults { get; set; }
}
```

#### 3. **UI Components**

**MainWindow.xaml** - Add new section:
- Reference folder selection (TextBox + Browse button)
- Latest folder selection (TextBox + Browse button)
- "Compare" button
- Results display area (TreeView or DataGrid)
- Export button (JSON/HTML/CSV)

**MainWindow.xaml.cs** - Add event handlers:
- `BtnBrowseReferenceFolder_Click`
- `BtnBrowseLatestFolder_Click`
- `BtnCompareDiff_Click` - Automatically saves diff report to output folder
- `BtnExportDiffReport_Click` - Optional manual export (HTML/CSV)
- `DisplayDiffResults(DiffReport report, string savedFilePath)` - Shows results and saved file path

#### 4. **Export Functionality**

**DiffReportExporter.cs**:
```csharp
public class DiffReportExporter
{
    // Automatically save diff report to output folder (if differences found)
    public string SaveDiffReport(DiffReport report, string outputFolder, bool saveOnlyIfDifferent = true);
    
    // Export to JSON format (structured)
    public void ExportToJson(DiffReport report, string outputPath);
    
    // Export to text format (human-readable)
    public void ExportToText(DiffReport report, string outputPath);
    
    // Optional: Export to HTML/CSV for additional formats
    public void ExportToHtml(DiffReport report, string outputPath);
    public void ExportToCsv(DiffReport report, string outputPath);
    
    // Generate timestamped filename
    private string GenerateTimestampedFileName(string baseName, string extension);
}
```

---

## Implementation Steps

### Phase 1: Core Comparison Engine
1. ✅ Create `Models/` directory
2. ✅ Implement data models (`JsonDiffResult.cs`, `DiffReport.cs`, `PropertyDifference.cs`)
3. ✅ Implement `JsonDiffComparer.cs` with basic comparison logic
4. ✅ Add unit tests for comparison logic

### Phase 2: UI Integration
5. ✅ Update `MainWindow.xaml` with diff comparison UI section
6. ✅ Add event handlers in `MainWindow.xaml.cs`
7. ✅ Implement file matching logic (handle timestamped filenames)
8. ✅ Display results in TreeView with color coding

### Phase 3: Auto-Save & Polish
9. ✅ Implement `DiffReportExporter.cs` with auto-save functionality
10. ✅ Auto-save diff reports to output folder (JSON + Text formats)
11. ✅ Add progress indicator during comparison
12. ✅ Add error handling and validation
13. ✅ Update user settings to save folder paths
14. ✅ Display saved file path in UI when comparison completes

---

## File Matching Strategy

### Problem
JSON files are generated with timestamps:
- `Drawing1_20241122_143025_345.json`
- `Drawing1_20241122_143027_678.json`

### Solution
Extract base filename by removing timestamp pattern:
```csharp
private string GetBaseFileName(string fullPath)
{
    string fileName = Path.GetFileNameWithoutExtension(fullPath);
    // Remove timestamp pattern: _YYYYMMDD_HHMMSS_mmm
    var match = Regex.Match(fileName, @"^(.+?)_\d{8}_\d{6}_\d{3}$");
    if (match.Success)
        return match.Groups[1].Value;
    return fileName;
}
```

---

## UI Layout

### New Section in MainWindow.xaml
```
┌─────────────────────────────────────────────────────────┐
│ 🔍 JSON Diff Comparison                                 │
├─────────────────────────────────────────────────────────┤
│ Reference Folder: [________________] [Browse...]        │
│ Latest Folder:    [________________] [Browse...]        │
│                                                          │
│ Output Folder:   [________________] [Browse...]          │
│                                                          │
│ [Compare Folders]  [Export Report...]                   │
│                                                          │
│ Summary:                                                 │
│   Total Files: 25                                        │
│   Matching: 20  |  Different: 3  |  Missing: 2          │
│                                                          │
│ ✅ Diff report saved:                                    │
│    diff_report_20241122_143025.json                      │
│    diff_report_20241122_143025.txt                       │
│                                                          │
│ Differences:                                             │
│ ┌─────────────────────────────────────────────────────┐ │
│ │ 📄 Drawing1.json                                    │ │
│ │   ├─ plotData.plotAreaData.grossPlotArea           │ │
│ │   │   Reference: 21773.11  →  Latest: 22000.00    │ │
│ │   └─ plotData.plotAreaData.netPlotArea             │ │
│ │       Reference: 20234.28  →  Latest: 20500.00    │ │
│ │                                                      │ │
│ │ 📄 Drawing2.json                                    │ │
│ │   └─ [No differences]                               │ │
│ └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

---

## JSON Comparison Algorithm

### Deep Comparison Logic
1. Parse both JSON files into `JsonDocument`
2. Recursively compare properties:
   - If both are objects: Compare each property
   - If both are arrays: Compare elements (by index or key if available)
   - If both are primitives: Compare values
3. Track differences with full JSON path
4. Handle type mismatches gracefully

### Example Comparison
```json
// Reference
{
  "plotData": {
    "plotAreaData": {
      "grossPlotArea": 21773.11
    }
  }
}

// Latest
{
  "plotData": {
    "plotAreaData": {
      "grossPlotArea": 22000.00,
      "netPlotArea": 20500.00
    }
  }
}

// Differences:
// 1. Modified: plotData.plotAreaData.grossPlotArea (21773.11 → 22000.00)
// 2. Added: plotData.plotAreaData.netPlotArea (20500.00)
```

---

## Auto-Save Behavior

### When Differences Are Found
When the comparison completes and differences are detected:
1. **Automatically save** diff report to the output folder
2. **Two files are created**:
   - `diff_report_YYYYMMDD_HHMMSS.json` - Structured JSON format
   - `diff_report_YYYYMMDD_HHMMSS.txt` - Human-readable text format
3. **Display file paths** in the UI for easy access
4. **Only save if differences found** (configurable option)

### When No Differences Found
- Option 1: Don't save any file (default)
- Option 2: Save a summary file indicating "No differences found"
- User preference can be configured

### File Naming Convention
```
diff_report_20241122_143025.json
diff_report_20241122_143025.txt
```
- Format: `diff_report_YYYYMMDD_HHMMSS.{ext}`
- Timestamp ensures no overwrites
- Matches the pattern used by batch processor output files

---

## Export Formats

### JSON Export (Auto-Saved)
```json
{
  "referenceFolder": "C:\\Reference",
  "latestFolder": "C:\\Latest",
  "comparisonDate": "2024-11-22T10:30:00",
  "summary": {
    "totalFiles": 25,
    "matchingFiles": 20,
    "differentFiles": 3,
    "missingInLatest": 1,
    "missingInReference": 1
  },
  "fileResults": [
    {
      "fileName": "Drawing1.json",
      "filesMatch": false,
      "differences": [
        {
          "path": "plotData.plotAreaData.grossPlotArea",
          "type": "Modified",
          "referenceValue": 21773.11,
          "latestValue": 22000.00
        }
      ]
    }
  ]
}
```

### HTML Export
- Color-coded differences (green=added, red=removed, yellow=modified)
- Collapsible sections for each file
- Summary statistics at top
- Side-by-side comparison view option

### Text Export (Auto-Saved)
```
═══════════════════════════════════════════════════════════════
JSON Diff Comparison Report
═══════════════════════════════════════════════════════════════

Comparison Date: 2024-11-22 14:30:25
Reference Folder: C:\Reference
Latest Folder: C:\Latest

Summary:
  Total Files: 25
  Matching Files: 20
  Different Files: 3
  Missing in Latest: 1
  Missing in Reference: 1

═══════════════════════════════════════════════════════════════
DIFFERENCES FOUND
═══════════════════════════════════════════════════════════════

📄 Drawing1.json
───────────────────────────────────────────────────────────────
  MODIFIED: plotData.plotAreaData.grossPlotArea
    Reference: 21773.11
    Latest:    22000.00

  ADDED: plotData.plotAreaData.netPlotArea
    Latest: 20500.00

📄 Drawing2.json
───────────────────────────────────────────────────────────────
  REMOVED: plotData.plotAreaData.waterBodiesArea
    Reference: 500.00

═══════════════════════════════════════════════════════════════
FILES MISSING IN LATEST
═══════════════════════════════════════════════════════════════
  - Drawing3.json

═══════════════════════════════════════════════════════════════
FILES MISSING IN REFERENCE
═══════════════════════════════════════════════════════════════
  - Drawing4.json

═══════════════════════════════════════════════════════════════
```

### CSV Export (Optional Manual Export)
```csv
FileName,PropertyPath,DifferenceType,ReferenceValue,LatestValue
Drawing1.json,plotData.plotAreaData.grossPlotArea,Modified,21773.11,22000.00
Drawing1.json,plotData.plotAreaData.netPlotArea,Added,,20500.00
```

---

## Error Handling

### Scenarios to Handle
1. **Invalid JSON files**: Log error, mark file as failed, continue with others
2. **Missing files**: Track in "Missing" category
3. **File access errors**: Show user-friendly error message
4. **Large files**: Use streaming JSON parser if needed
5. **Memory issues**: Process files one at a time, not all in memory

---

## Testing Strategy

### Unit Tests
- Test JSON comparison with various structures
- Test file matching logic
- Test edge cases (empty objects, null values, arrays)

### Integration Tests
- Test folder comparison with sample data
- Test export functionality
- Test UI interactions

### Manual Testing
- Test with real JSON files from batch processing
- Test with large folders (100+ files)
- Test error scenarios

---

## Future Enhancements (Optional)

1. **Filtering**: Filter differences by type (added/removed/modified)
2. **Search**: Search within differences
3. **Ignore paths**: Configure paths to ignore during comparison
4. **Tolerance**: Numeric comparison with tolerance (e.g., 0.01 for floating point)
5. **Visual diff**: Side-by-side JSON viewer
6. **History**: Save comparison history
7. **Batch comparison**: Compare multiple reference/latest pairs

---

## Dependencies

### NuGet Packages
- **System.Text.Json** (already in .NET 8.0, no additional package needed)
- Consider **Newtonsoft.Json** if more advanced JSON handling is needed (already referenced via DLLs)

### No Additional Dependencies Required
- All functionality can be built with .NET 8.0 standard libraries

---

## Timeline Estimate

- **Phase 1** (Core Engine): 2-3 hours
- **Phase 2** (UI Integration): 2-3 hours
- **Phase 3** (Export & Polish): 1-2 hours
- **Testing & Bug Fixes**: 1-2 hours

**Total**: ~6-10 hours

---

## Implementation Details

### Auto-Save Logic
```csharp
// In BtnCompareDiff_Click handler
var report = comparer.CompareFolders(referenceFolder, latestFolder, outputFolder);

if (report.DifferentFiles > 0 || report.MissingInLatest > 0 || report.MissingInReference > 0)
{
    var exporter = new DiffReportExporter();
    string jsonPath = exporter.SaveDiffReport(report, outputFolder, saveOnlyIfDifferent: true);
    string textPath = exporter.ExportToText(report, outputFolder);
    
    LogMessage($"✅ Diff report saved:");
    LogMessage($"   JSON: {Path.GetFileName(jsonPath)}");
    LogMessage($"   Text: {Path.GetFileName(textPath)}");
}
else
{
    LogMessage("✅ No differences found - no report saved");
}
```

### Output Folder Selection
- Use the same "Output Folder" from the main batch processing section
- Or add a separate "Diff Output Folder" option
- Default to main output folder if not specified

## Notes

- The comparison will be case-sensitive for property names
- Array comparison is by index (order matters)
- Floating-point comparisons use exact match (consider adding tolerance option)
- Large JSON files may take time; consider async processing with progress updates
- **Diff reports are automatically saved** to output folder when differences are found
- Both JSON and text formats are saved for maximum usability
- Timestamped filenames prevent overwriting previous reports

