# CREATE_RELATIONS_FOR_ENTITIES Implementation Plan

## Overview
This document outlines the implementation plan for a new feature that processes drawing files using AutoCAD GUI (full AutoCAD.exe instead of accoreconsole.exe) to run the `CREATE_RELATIONS_FOR_ENTITIES` command. This is needed because the UIPlugin DLL requires the full AutoCAD UI.

## Requirements

### Functional Requirements
1. **Use Full AutoCAD GUI** - Launch `acad.exe` instead of `accoreconsole.exe`
2. **Load UIPlugin DLL** - Load the UIPlugin DLL which depends on UI
3. **Run Command** - Execute `CREATE_RELATIONS_FOR_ENTITIES` command for each drawing
4. **Automatic Popup Handling** - Automatically skip/dismiss any popups or alerts
5. **Sequential Processing** - Process one drawing at a time (sequential, not parallel)
6. **Workflow per Drawing**:
   - Open AutoCAD
   - Open drawing file
   - Load UIPlugin DLL
   - Run `CREATE_RELATIONS_FOR_ENTITIES` command
   - Save drawing file
   - Close drawing
   - Move to next drawing

### UI Requirements
1. **Separate Window** - Create a new form/window similar to `JsonDiffWindow`
2. **Input Fields**:
   - UIPlugin DLL path (browse button)
   - AutoCAD EXE path (browse button) - defaults to `acad.exe`
   - Input folder (browse button) - folder containing DWG files
3. **Button in Main Window** - Add button in `ModeSelectionWindow` similar to JSON Diff button
4. **Vertical Layout** - Ensure buttons don't overlap (stack vertically)

## Implementation Steps

### Phase 1: UI Components

#### 1.1 Update ModeSelectionWindow
- **File**: `ModeSelectionWindow.xaml` and `ModeSelectionWindow.xaml.cs`
- **Changes**:
  - Add third mode option: "Relations Creation" or "UI Plugin Processing"
  - Update layout to vertical stack (3 buttons) or grid with 3 columns
  - Add new enum value: `SelectedMode.RelationsCreation`
  - Add button/option for the new mode

#### 1.2 Create RelationsWindow
- **Files**: 
  - `RelationsWindow.xaml` - New window XAML
  - `RelationsWindow.xaml.cs` - Code-behind
- **Layout**:
  - Header with back button (similar to JsonDiffWindow)
  - Input section:
    - UIPlugin DLL path (TextBox + Browse button)
    - AutoCAD EXE path (TextBox + Browse button)
    - Input folder (TextBox + Browse button)
  - Action buttons: "Start Processing" button
  - Progress section: Current file, progress bar, status
  - Log output section: TextBox for logs
  - Settings persistence: Save/load user settings (similar to other windows)

### Phase 2: AutoCAD Automation Core

#### 2.1 Create AutoCADGuiProcessor Class
- **File**: `AutoCADGuiProcessor.cs`
- **Purpose**: Handle AutoCAD GUI automation
- **Key Methods**:
  - `ProcessDrawingAsync(string drawingPath, string uiPluginDll, string autocadExe, CancellationToken ct)`
  - `LaunchAutoCAD(string autocadExe, string drawingPath)`
  - `LoadUIPlugin(string dllPath)`
  - `RunCommand(string command)`
  - `SaveDrawing()`
  - `CloseDrawing()`
  - `HandlePopups()` - Monitor and dismiss popups automatically

#### 2.2 AutoCAD Automation Approach

**Option A: COM Automation (Recommended)**
- Use AutoCAD COM API (`Autodesk.AutoCAD.Interop` or similar)
- Pros: Direct control, reliable
- Cons: Requires AutoCAD COM registration

**Option B: UI Automation + Script Injection**
- Use `System.Windows.Automation` for popup detection
- Send commands via command line or script file
- Pros: Works without COM registration
- Cons: Less reliable, depends on UI state

**Option C: Hybrid Approach**
- Launch AutoCAD with script file
- Use UI Automation to handle popups
- Send commands via command line or script

**Recommended: Hybrid Approach**
- Launch AutoCAD process
- Use script file to load DLL and run command
- Monitor process for popups using UI Automation
- Auto-dismiss popups when detected

#### 2.3 Popup Handling Strategy

1. **Monitor for Popup Windows**:
   - Use `System.Windows.Automation.AutomationElement` to find dialog windows
   - Poll every 100-200ms during processing
   - Look for common AutoCAD dialog class names or titles

2. **Identify Popup Types**:
   - Error dialogs
   - Warning dialogs
   - Confirmation dialogs
   - Save prompts

3. **Auto-Dismiss Strategy**:
   - Find "OK", "Yes", "Skip", or "Close" buttons
   - Send Enter key or click button
   - Timeout after 5 seconds if popup doesn't dismiss

#### 2.4 Script Generation

- **File**: `RelationsScriptGenerator.cs` or inline in `AutoCADGuiProcessor`
- **Generate Script File**:
  ```lisp
  ; Load UIPlugin DLL
  (command "NETLOAD" "path/to/UIPlugin.dll")
  ; Run command
  (command "CREATE_RELATIONS_FOR_ENTITIES")
  ; Save and close
  (command "QSAVE")
  (command "QUIT")
  ```

### Phase 3: Processing Logic

#### 3.1 Sequential Processing
- **File**: `RelationsWindow.xaml.cs`
- **Method**: `BtnStartProcessing_Click`
- **Flow**:
  1. Validate inputs
  2. Get all DWG files from input folder
  3. For each file (sequential loop):
     - Create `AutoCADGuiProcessor` instance
     - Call `ProcessDrawingAsync()`
     - Update progress UI
     - Log results
  4. Show completion summary

#### 3.2 Error Handling
- Handle AutoCAD crashes
- Handle popup timeouts
- Handle file access errors
- Log all errors but continue with next file

#### 3.3 Progress Tracking
- Show current file name
- Show progress (X of Y files)
- Update status text
- Enable/disable start button during processing

### Phase 4: Integration

#### 4.1 Update Navigation
- **File**: All window code-behind files
- **Changes**:
  - Add case for `SelectedMode.RelationsCreation` in back button handlers
  - Create `RelationsWindow` instance when mode selected

#### 4.2 Settings Management
- **File**: `RelationsWindow.xaml.cs`
- **User Settings**:
  - Save settings to `user_settings_relations.json`
  - Load settings on window open
  - Store: UIPlugin DLL path, AutoCAD EXE path, Input folder

## Technical Implementation Details

### AutoCAD Launch
```csharp
var startInfo = new ProcessStartInfo
{
    FileName = autocadExePath, // e.g., "C:\Program Files\Autodesk\AutoCAD 2025\acad.exe"
    Arguments = $"/nologo /b \"{scriptPath}\" \"{drawingPath}\"",
    UseShellExecute = false,
    CreateNoWindow = false // Show AutoCAD window
};
```

### Script File Template
```lisp
; AutoCAD Script for CREATE_RELATIONS_FOR_ENTITIES
; Parameters: %1 = Drawing Path, %2 = UIPlugin DLL Path

; Open drawing
(command "OPEN" "%1")

; Load UIPlugin DLL
(command "NETLOAD" "%2")

; Run command
(command "CREATE_RELATIONS_FOR_ENTITIES")

; Save
(command "QSAVE")

; Close
(command "QUIT" "Y")
```

### Popup Detection (UI Automation)
```csharp
// Monitor for dialog windows
var root = AutomationElement.RootElement;
var dialogs = root.FindAll(
    TreeScope.Children,
    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)
);

// Find AutoCAD dialog
foreach (AutomationElement dialog in dialogs)
{
    var name = dialog.Current.Name;
    if (name.Contains("AutoCAD") || IsPopupWindow(dialog))
    {
        DismissPopup(dialog);
    }
}
```

## File Structure

```
BatchProcessor/
├── RelationsWindow.xaml              [NEW]
├── RelationsWindow.xaml.cs           [NEW]
├── AutoCADGuiProcessor.cs            [NEW]
├── RelationsScriptGenerator.cs       [NEW]
├── ModeSelectionWindow.xaml          [MODIFY]
├── ModeSelectionWindow.xaml.cs       [MODIFY]
├── JsonDiffWindow.xaml.cs            [MODIFY - add navigation case]
├── MainWindow.xaml.cs                [MODIFY - add navigation case]
└── Models/
    └── RelationsUserSettings.cs      [NEW - optional, for settings]
```

## Dependencies

### NuGet Packages
- `System.Windows.Automation` (built-in) - For popup detection
- May need: `Interop.AutoCAD` if using COM (but script approach preferred)

## Testing Considerations

1. **Test with various popups**:
   - File save dialogs
   - Error messages
   - Warning dialogs
   - License dialogs

2. **Test with multiple drawings**:
   - Small batch (5-10 files)
   - Verify each file is processed

3. **Test error scenarios**:
   - Missing DLL
   - Invalid drawing file
   - AutoCAD crash
   - Popup that can't be dismissed

4. **Test performance**:
   - Sequential processing timing
   - Popup detection overhead

## Known Challenges

1. **Popup Detection Reliability**:
   - Different AutoCAD versions may have different popup styles
   - Need robust detection logic

2. **AutoCAD Startup Time**:
   - AutoCAD takes time to launch
   - Need proper wait logic

3. **Command Completion**:
   - Need to detect when command finishes
   - May require polling or event handling

4. **File Locking**:
   - AutoCAD may lock files
   - Need proper cleanup

## Alternative Approach: Process Automation

If COM or UI Automation proves unreliable, consider:
1. Use command-line AutoCAD with `/b` flag for script
2. Use separate script file per drawing
3. Monitor process exit codes
4. Use file timestamps to verify completion

## Implementation Order

1. ✅ Create RelationsWindow UI (XAML + code-behind skeleton)
2. ✅ Update ModeSelectionWindow with third option
3. ✅ Create AutoCADGuiProcessor class with basic structure
4. ✅ Implement AutoCAD launch logic
5. ✅ Implement script generation
6. ✅ Implement popup detection and dismissal
7. ✅ Implement sequential processing loop
8. ✅ Add error handling and logging
9. ✅ Add settings persistence
10. ✅ Test and refine

## UI Mockup Concept

```
ModeSelectionWindow:
┌─────────────────────────────────────┐
│  🎯 AutoCAD Batch Processor         │
│                                     │
│  ┌──────────┐ ┌──────────┐         │
│  │ Commands │ │ JSON Diff│         │
│  │Execution │ │ Compare  │         │
│  └──────────┘ └──────────┘         │
│                                     │
│  ┌──────────┐                      │
│  │ Create   │                      │
│  │ Relations│                      │
│  └──────────┘                      │
└─────────────────────────────────────┘

RelationsWindow:
┌─────────────────────────────────────┐
│  ⬅  🔗 Create Relations             │
├─────────────────────────────────────┤
│  UIPlugin DLL: [path] [Browse]     │
│  AutoCAD EXE:  [path] [Browse]     │
│  Input Folder: [path] [Browse]     │
│                                     │
│  [▶ Start Processing]               │
│                                     │
│  Progress: File 3/10                │
│  Status: Processing drawing.dwg     │
│                                     │
│  Log Output:                        │
│  ┌─────────────────────────────┐   │
│  │ ...                         │   │
│  └─────────────────────────────┘   │
└─────────────────────────────────────┘
```

## Next Steps

1. Review and approve this plan
2. Start with Phase 1 (UI Components)
3. Prototype AutoCAD automation approach
4. Iterate based on testing results


