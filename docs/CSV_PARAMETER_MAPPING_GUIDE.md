# CSV Parameter Mapping Guide

## 🎯 Overview

The BatchProcessor now supports **CSV-based parameter mapping**, allowing you to specify different application parameters for each drawing file. This is perfect for batch processing many drawings with varying configurations!

## ✨ Key Features

✅ **Per-Drawing Configuration** - Each drawing gets its own parameters from CSV  
✅ **Automatic Type Conversion** - Converts CSV strings to proper types (bool, lists, numbers)  
✅ **Smart Property Mapping** - Maps CSV columns to ApplicationParameters.Parameters class  
✅ **Template Support** - Merge CSV data with base template config  
✅ **Validation** - Checks for required columns and provides warnings  

## 📋 CSV Format

### Required Columns

- **Filename** - Drawing filename (with or without .dwg extension)
- **ProjectType** - Type of project (BUILDING_PERMISSION, LAYOUT_WITH_OPEN_PLOTS, etc.)
- **PlotUse** - Plot usage type (RESIDENTIAL, COMMERCIAL, etc.)
- **Authority** - Governing authority (GHMC, DTCP, etc.)

### Supported Parameters

All columns from the CSV are mapped to the `Parameters` class:

| CSV Column | Parameter Property | Type | Example |
|------------|-------------------|------|---------|
| Filename | - | string | `GHMC_BP_New_V10.dwg` |
| ProjectType | ProjectType | string | `BUILDING_PERMISSION` |
| NatureOfDevelopment | NatureOfDevelopment | string | `NEW` |
| PlotUse | PlotUse | string | `RESIDENTIAL` |
| PlotSubUse | PlotSubUse | string | `APARTMENT_COMPLEXES` |
| SpecialBuildingType | SpecialBuildingType | string | `NOT_APPLICABLE` |
| AvailTDR | AvailTDR | bool | `TRUE` |
| EffectedByRoadWidening | EffectedbyRoadWidening | bool | `FALSE` |
| AvailRoadWideningConcession | AvailRoadWideningConcession | bool | `TRUE` |
| RoadWideningConcessionFor | RoadWideningConcessionFor | List<string> | `["SETBACK_CONCESSION"]` |
| EffectedByNalaWidening | EffectedByNalaWidening | bool | `FALSE` |
| AvailNalaWideningConcession | AvailNalaWideningConcession | bool | `FALSE` |
| NalaWideningConcessionFor | NalaWideningConcessionFor | List<string> | `["ADDITIONAL_FLOOR"]` |
| DoYouWantToAvailExtraMortgageForNalaConversion | AvailExtraMortgageForNalaConversion | bool | `FALSE` |
| DoYouWantToAvailExtraMortgageForCityLevelImpactFee | AvailExtraMortgageForCityLevelImpactFee | bool | `FALSE` |
| DoYouWantToAvailExtraMortgageForCapitalizationCharges | AvailExtraMortgageForCapitalizationCharges | bool | `FALSE` |
| Authority | Authority | string | `GHMC` |
| CategoryOfLayoutPermission | CategoryOfLayoutPermission | string | `DRAFT_LAYOUT` |

### Default Values

Parameters not in CSV get these defaults:
- `ExtractBlockNames`: `true`
- `ExtractLayerNames`: `true`
- `layersToValidate`: `[]` (empty list)
- `PluginVersion`: `"1.0"`

## 📝 CSV Example

```csv
Filename,ProjectType,PlotUse,PlotSubUse,Authority,AvailTDR,EffectedByRoadWidening,RoadWideningConcessionFor
Drawing1.dwg,BUILDING_PERMISSION,RESIDENTIAL,APARTMENT_COMPLEXES,GHMC,TRUE,TRUE,"[""SETBACK_CONCESSION""]"
Drawing2.dwg,BUILDING_PERMISSION,COMMERCIAL,OFFICE,GHMC,FALSE,FALSE,[]
Drawing3.dwg,LAYOUT_WITH_OPEN_PLOTS,,,DTCP,FALSE,FALSE,[]
```

## 🎨 Three Modes of Operation

### Mode 1: CSV Only (No Base Config)
**Use when:** CSV has ALL parameters needed for processing

```
✅ CSV File: parameters.csv
❌ Config JSON: (leave empty)
```

All parameters come from CSV. Perfect for completely different drawing types!

### Mode 2: Config Only (No CSV)
**Use when:** All drawings use the same parameters

```
❌ CSV File: (leave empty)
✅ Config JSON: config.json
```

Traditional batch processing - all drawings get same config.

### Mode 3: CSV + Config (Recommended)
**Use when:** You have common settings + per-drawing variations

```
✅ CSV File: parameters.csv
✅ Config JSON: base_config.json
```

CSV parameters are **merged** with base config:
- Base config provides defaults and common settings
- CSV overrides specific parameters per drawing
- Best of both worlds!

## 🚀 How to Use

### Method 1: Using the UI

1. **Open BatchProcessor**
2. **Fill in the basic fields**:
   - Input Folder (containing .dwg files) - **Required**
   - Output Folder (for JSON results) - **Required**
   - Config JSON (base/template configuration) - **Optional*** 
   - CSV Parameters - **Optional***
3. **Browse for CSV file**:
   - Click "Browse..." next to "CSV Parameters"
   - Select your CSV file
4. **Click "Run Batch Processing"**

***Note:** You must provide **either** a Config JSON **or** a CSV file (or both!):
- **CSV only**: All parameters come from CSV
- **Config only**: All drawings use same config
- **CSV + Config**: CSV parameters merged with base config template

### Method 2: Programmatic Usage

```csharp
var processor = new DrawingBatchProcessor(
    accoreconsoleExePath: @"C:\...\accoreconsole.exe",
    dllsToLoad: dllList,
    mainCommand: "ProcessWithJsonBatch",
    maxParallelism: 4
);

// Enable CSV mapping
bool csvEnabled = processor.EnableCsvMapping("parameters.csv");

// Process all drawings
await processor.ProcessFolderAsync(inputFolder, outputFolder, baseConfigPath);
```

## 📊 How It Works

### Processing Flow

```
1. Load CSV file
   ↓
2. Parse and validate columns
   ↓
3. For each drawing:
   ├─ Find matching row in CSV (by filename)
   ├─ Generate drawing-specific config JSON
   │  ├─ Start with base template config
   │  ├─ Map CSV columns to Parameters properties
   │  ├─ Convert types (strings → bools, lists, numbers)
   │  └─ Merge with template
   ├─ Create temporary config file
   ├─ Pass to AutoCAD via environment variables
   └─ Clean up temp config after processing
   ↓
4. Generate output JSON with drawing-specific parameters
```

### Parameter Generation

**Input CSV Row:**
```csv
Drawing1.dwg,BUILDING_PERMISSION,RESIDENTIAL,APARTMENT_COMPLEXES,GHMC,TRUE,FALSE,[]
```

**Generated Config JSON:**
```json
{
  "ExtractBlockNames": true,
  "ExtractLayerNames": true,
  "ProjectType": "BUILDING_PERMISSION",
  "PlotUse": "RESIDENTIAL",
  "PlotSubUse": "APARTMENT_COMPLEXES",
  "Authority": "GHMC",
  "AvailTDR": true,
  "EffectedbyRoadWidening": false,
  "RoadWideningConcessionFor": [],
  "layersToValidate": [],
  "PluginVersion": "1.0"
}
```

## 🎨 Type Conversions

### Boolean Values
```csv
TRUE → true
FALSE → false
1 → true
0 → false
```

### List Values
```csv
"[""item1"", ""item2""]" → ["item1", "item2"]
"item1, item2" → ["item1", "item2"]
"[]" → []
```

### Numeric Values
```csv
"1234.56" → 1234.56
"500" → 500
```

## ⚙️ Advanced Usage

### Using Base Template Config

Create a base `config.json` with common settings:

```json
{
  "ExtractBlockNames": true,
  "ExtractLayerNames": true,
  "layersToValidate": ["_PLOT", "_BUILDING"],
  "PluginVersion": "2.0",
  "CustomSetting": "value"
}
```

The CSV parameters will be merged with this template, allowing you to:
- Set common defaults in the template
- Override specific values per-drawing via CSV
- Keep custom settings not in CSV

### Handling Missing Drawings

If a drawing file is NOT found in the CSV:
- ⚠️ Warning logged: "No parameters found in CSV for: DrawingName.dwg"
- ✅ Falls back to base config.json
- ✅ Processing continues normally

### Column Mapping

The mapper intelligently handles column name variations:

```
CSV Column                                          → Parameter Property
------------------------------------------------------------------
"EffectedByRoadWidening"                           → EffectedbyRoadWidening
"DoYouWantToAvailExtraMortgageForNalaConversion"   → AvailExtraMortgageForNalaConversion
"DoYouWantToAvailExtraMortgageForCityLevelImpactFee" → AvailExtraMortgageForCityLevelImpactFee
```

## 📊 Console Output

When CSV mapping is enabled, you'll see:

```
📊 Enabling CSV parameter mapping...
✅ CSV Parameter Mapping Enabled
CSV Statistics:
  - Total Drawings: 38
  - Parameters per Drawing: 29
  - CSV File: GHMC_15-NOV-2025_38FILES.csv

CSV to Parameters Mapping:
  Boolean fields: 10
  List fields: 3
  String fields: 11
  Total mapped columns: 24

────────────────────────────────────────────────────────────────
Processing drawings...
────────────────────────────────────────────────────────────────

[20251124_103045_123] ⏳ Processing: Drawing1
  [Drawing1] 📋 Generated config from CSV
  [Drawing1] 📝 Parameter file: C:\Temp\params_....json
  [Drawing1] ENV: OUTPUT_FOLDER = C:\output
  [Drawing1] ENV: OUTPUT_FILENAME = Drawing1.json
  ...
```

## 🐛 Troubleshooting

### Issue: "No parameters found in CSV"

**Causes:**
- Filename in CSV doesn't match drawing filename
- CSV uses different filename format

**Solutions:**
1. Ensure CSV filename matches drawing filename exactly
2. Or CSV filename without .dwg extension matches
3. Check for extra spaces or special characters

### Issue: "Missing recommended columns"

**Warning Message:**
```
⚠️ Warning: Missing recommended columns: ProjectType, Authority
```

**Solution:**
- Add the required columns to your CSV
- Or ignore if those parameters aren't needed

### Issue: List values not parsing

**Problem:** `RoadWideningConcessionFor` shows as string instead of array

**Solution:** Use proper JSON array format in CSV:
```csv
"[""SETBACK_CONCESSION"", ""ADDITIONAL_FLOOR""]"
```

Not:
```csv
SETBACK_CONCESSION, ADDITIONAL_FLOOR
```

## 📚 Complete Example

### Your CSV File (`parameters.csv`):
```csv
Filename,ProjectType,NatureOfDevelopment,PlotUse,PlotSubUse,Authority,AvailTDR,EffectedByRoadWidening,AvailRoadWideningConcession,RoadWideningConcessionFor
GHMC_BP_New_Residential_V10.dwg,BUILDING_PERMISSION,NEW,RESIDENTIAL,APARTMENT_COMPLEXES,GHMC,TRUE,TRUE,TRUE,"[""SETBACK_CONCESSION""]"
GHMC_BP_New_Commercial_V10.dwg,BUILDING_PERMISSION,NEW,COMMERCIAL,OFFICE,GHMC,FALSE,FALSE,FALSE,[]
GHMC_LWOP_New_V10.dwg,LAYOUT_WITH_OPEN_PLOTS,NEW,,,GHMC,FALSE,FALSE,FALSE,[]
```

### Base Config (`config.json`):
```json
{
  "ExtractBlockNames": true,
  "ExtractLayerNames": true,
  "layersToValidate": []
}
```

### Running BatchProcessor:

1. Select input folder with the 3 .dwg files
2. Select output folder
3. Select base `config.json`
4. Select `parameters.csv`
5. Click "Run Batch Processing"

### Result:

Each drawing is processed with its specific parameters from the CSV, merged with the base config!

## 🎉 Benefits

✅ **No more manual config editing** for each drawing  
✅ **Bulk processing** with different parameters  
✅ **Easy to maintain** - just edit CSV in Excel/Google Sheets  
✅ **Version control friendly** - CSV files track changes well  
✅ **Scalable** - process hundreds of drawings with unique configs  
✅ **Flexible** - use template config + per-drawing overrides  

## 💡 Tips

1. **Use Excel/Google Sheets** to create and edit your CSV
2. **Test with a few drawings first** before processing hundreds
3. **Keep a backup** of your CSV file
4. **Use the base config** for common settings across all drawings
5. **Enable verbose logging** to see generated configs for debugging

---

**Need help?** Check the log output for detailed information about CSV loading and parameter mapping!

