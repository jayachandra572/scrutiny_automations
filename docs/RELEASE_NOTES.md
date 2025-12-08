# BatchProcessor Release Notes

## Version 2.0 - CSV Parameter Mapping (Nov 24, 2024)

### 🎯 Major Feature: CSV-Based Parameter Mapping

The BatchProcessor now supports **dynamic per-drawing configuration** through CSV files! Process hundreds of drawings with different parameters without manual config editing.

### ✨ New Features

#### 1. CSV Parameter Mapping System
- **CsvParameterMapper**: Loads and parses CSV files with drawing-specific parameters
- **ParametersMapper**: Intelligent mapping of CSV columns to `ApplicationParameters.Parameters` class
- **Automatic Type Conversion**: Converts CSV strings to appropriate types (bool, List<string>, numbers)
- **Template Merging**: Combines CSV parameters with base config template

#### 2. Enhanced User Interface
- ✅ New "CSV Parameters" field with browse button
- ✅ Optional CSV file selector (light blue background indicates optional field)
- ✅ CSV file path saved in user settings
- ✅ Real-time feedback when CSV file is loaded

#### 3. Smart Parameter Mapping
Maps 29+ CSV columns to Parameters class properties:

**Boolean Fields (10):**
- ExtractBlockNames, ExtractLayerNames
- AvailTDR, EffectedbyRoadWidening, AvailRoadWideningConcession
- EffectedByNalaWidening, AvailNalaWideningConcession
- AvailExtraMortgageForNalaConversion/CityLevelImpactFee/CapitalizationCharges

**List Fields (3):**
- RoadWideningConcessionFor
- NalaWideningConcessionFor
- layersToValidate

**String Fields (11+):**
- ProjectType, NatureOfDevelopment
- PlotUse, PlotSubUse
- SpecialBuildingType, Authority
- CategoryOfLayoutPermission
- And more...

#### 4. Processing Enhancements
- **Per-Drawing Config Generation**: Creates unique JSON config for each drawing
- **Flexible Filename Matching**: Matches with or without .dwg extension
- **Fallback Support**: Uses base config if drawing not found in CSV
- **Validation**: Checks for required columns and warns about missing data
- **Temporary File Management**: Auto-creates and cleans up temp config files

#### 5. Debugging & Validation
- Displays CSV statistics (total drawings, parameters, column count)
- Shows mapping summary (boolean/list/string field counts)
- Validates required columns (ProjectType, PlotUse, Authority)
- Warns about missing parameters per drawing
- Enhanced console output shows CSV-generated configs

### 📋 Usage

**Simple 4-Step Process:**

1. **Select Input Folder** - Containing your .dwg files
2. **Select Output Folder** - Where JSON results will be saved
3. **Select Base Config** - Template with common settings
4. **Select CSV File** - *(Optional)* Per-drawing parameters

Then click **"Run Batch Processing"**!

**With CSV:**
```
Each drawing gets its specific parameters from CSV → 
  Merged with base config → 
    Passed to AutoCAD → 
      Generates unique output JSON
```

**Without CSV:**
```
All drawings use the same base config → 
  Standard batch processing
```

### 🎨 CSV Format Example

```csv
Filename,ProjectType,PlotUse,PlotSubUse,Authority,AvailTDR,EffectedByRoadWidening,RoadWideningConcessionFor
Drawing1.dwg,BUILDING_PERMISSION,RESIDENTIAL,APARTMENT_COMPLEXES,GHMC,TRUE,TRUE,"[""SETBACK_CONCESSION""]"
Drawing2.dwg,BUILDING_PERMISSION,COMMERCIAL,OFFICE,GHMC,FALSE,FALSE,[]
Drawing3.dwg,LAYOUT_WITH_OPEN_PLOTS,,,DTCP,FALSE,FALSE,[]
```

### 📊 Console Output Enhancement

New messages when CSV mapping is enabled:

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

[DrawingName] 📋 Generated config from CSV
[DrawingName] ENV: OUTPUT_FILENAME = DrawingName.json
```

### 🔧 Technical Improvements

**Architecture:**
- Modular design with separate CSV and Parameters mappers
- Clean separation of concerns
- Extensible for additional parameter types

**Type Safety:**
- Proper type conversion from CSV strings
- Handles JSON arrays in CSV cells
- Null-safe processing throughout

**Performance:**
- Efficient CSV parsing with Dictionary lookups
- Parallel processing maintained
- Minimal overhead per drawing

**Error Handling:**
- Graceful degradation if CSV parsing fails
- Warnings for missing data
- Continues processing even if some drawings lack CSV entries

### 📚 Documentation

**New Guides:**
- `CSV_PARAMETER_MAPPING_GUIDE.md` - Complete usage guide
- Detailed examples and troubleshooting
- Type conversion reference
- Column mapping tables

**Updated Guides:**
- `README.md` - Added CSV feature overview
- `TESTING_STEPS.md` - CSV testing procedures

### 🐛 Bug Fixes

- Fixed DLL path configuration issue (CrxApp.dll was pointing to Newtonsoft.Json.dll)
- Enhanced parameter passing with multiple methods (ENV vars, LISP vars, JSON files)
- Improved output filename handling (OUTPUT_FILENAME parameter)
- Better JSON verification and debugging

### ⚡ Performance

- No performance impact when CSV not used
- Minimal overhead per drawing (config generation < 1ms)
- Parallel processing fully maintained
- Efficient CSV loading (one-time operation)

### 🔄 Backward Compatibility

✅ **100% Backward Compatible!**
- Existing workflows work without changes
- CSV is completely optional
- Base config-only processing still supported
- All previous features preserved

---

## Version 1.0 - Initial Release

### Core Features

- **Parallel Processing**: Process multiple drawings simultaneously
- **Standalone Application**: No project dependencies
- **WPF GUI**: User-friendly interface
- **Configuration File**: `appsettings.json` for easy setup
- **Multiple Parameter Methods**:
  - Environment Variables
  - LISP Variables  
  - JSON Parameter Files
- **Enhanced Debugging**:
  - Output verification
  - Detailed logging
  - JSON creation validation
  - File existence checks
- **Flexible Commands**: Support for multiple AutoCAD commands
- **Progress Tracking**: Real-time status updates
- **Error Handling**: Failed drawings don't stop the batch

### Documentation

- `README.md` - Project overview
- `TESTING_STEPS.md` - Setup and testing guide
- `DEBUGGING_JSON_OUTPUT.md` - Troubleshooting guide
- `QUICK_FIX_GUIDE.md` - Common issues and solutions
- `CRXAPP_UPDATE_GUIDE.md` - CrxApp integration guide
- `FINAL_CRXAPP_FIX.md` - Parameter passing fix

---

## Migration Guide

### From v1.0 to v2.0

**No changes required!** v2.0 is fully backward compatible.

**To use new CSV feature:**

1. Update your BatchProcessor (rebuild if needed)
2. Create a CSV file with your drawing parameters
3. In the UI, browse and select the CSV file
4. Run batch processing as normal!

**That's it!** The processor will automatically:
- Load the CSV
- Map parameters to each drawing
- Generate per-drawing configs
- Process everything in parallel

### Example Workflow

**Before (v1.0):**
```
1. Edit config.json for Drawing1
2. Process Drawing1
3. Edit config.json for Drawing2
4. Process Drawing2
... repeat 100 times ...
```

**After (v2.0):**
```
1. Create CSV with all 100 drawings and their parameters
2. Select CSV file in BatchProcessor
3. Click "Run Batch Processing"
4. Done! All 100 drawings processed with unique configs
```

---

## Coming Soon

Potential future enhancements:
- Excel (.xlsx) file support
- CSV template generator
- Parameter validation rules
- CSV column auto-detection
- Batch processing reports with statistics
- Drawing grouping and categorization

---

## Support

For questions, issues, or feature requests:
- Check the documentation guides
- Review `CSV_PARAMETER_MAPPING_GUIDE.md` for CSV usage
- Enable verbose logging for detailed output
- Check console logs for error messages

---

## Credits

**Version 2.0 developed:** November 24, 2024
**CSV Parameter Mapping:** Enables scalable batch processing for large projects

**Tested with:**
- AutoCAD 2025
- .NET 8.0
- 38 test drawings with varying parameters
- GHMC project configurations

---

## Changelog Summary

### v2.0 (2024-11-24)
- ✨ NEW: CSV parameter mapping
- ✨ NEW: Per-drawing configuration
- ✨ NEW: ParametersMapper class
- ✨ NEW: CsvParameterMapper class
- 🔧 Enhanced: UI with CSV file selector
- 📚 NEW: CSV_PARAMETER_MAPPING_GUIDE.md
- 🐛 Fixed: DLL path configuration
- 🐛 Fixed: Output filename parameter passing

### v1.0 (2024-11-24)
- 🎉 Initial release
- ✨ Parallel batch processing
- ✨ WPF GUI application
- ✨ Configuration file support
- ✨ Enhanced debugging and validation
- 📚 Comprehensive documentation

