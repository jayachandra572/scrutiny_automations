# How to Check if Process is Complete

## In the Code (Current Implementation)

The code uses multiple methods to verify process completion:

### 1. **WaitForExitAsync()** - Primary Method
```csharp
await process.WaitForExitAsync(cts.Token);
```
- Waits for the process to exit (with timeout)
- Returns when process exits or timeout occurs

### 2. **HasExited Property** - Verification
```csharp
if (!process.HasExited)
{
    // Process is still running - force kill
    process.Kill(entireProcessTree: true);
}
```
- Checks if process has actually exited
- Used as a safety check after WaitForExitAsync()

### 3. **ExitCode Property** - Status Check
```csharp
int exitCode = process.HasExited ? process.ExitCode : -1;
```
- Exit code 0 = Success
- Non-zero = Error or failure
- -1 = Process didn't exit or was killed

### 4. **WaitForExit()** - Synchronous Wait
```csharp
process.WaitForExit(10000); // Wait up to 10 seconds
```
- Blocks until process exits or timeout
- Returns true if process exited, false if timeout

---

## Practical Ways to Monitor Processes

### Method 1: Task Manager (GUI)

1. Open **Task Manager** (Ctrl+Shift+Esc)
2. Go to **Details** tab
3. Look for processes:
   - `accoreconsole.exe` - AutoCAD console processes
   - `BatchProcessor.exe` - Your batch processor
4. **Check Status:**
   - If process is in the list = Still running
   - If process disappears = Completed/terminated

**Filter by name:**
- Type "accore" in the search box to filter AutoCAD processes
- Count how many are running (should match your MaxParallel setting)

---

### Method 2: PowerShell Commands

#### Check if AutoCAD processes are running:
```powershell
Get-Process | Where-Object {$_.ProcessName -like "*accore*" -or $_.ProcessName -like "*acad*"}
```

#### Count running AutoCAD processes:
```powershell
(Get-Process | Where-Object {$_.ProcessName -like "*accore*"}).Count
```

#### Monitor processes in real-time (updates every 2 seconds):
```powershell
while ($true) {
    Clear-Host
    $processes = Get-Process | Where-Object {$_.ProcessName -like "*accore*" -or $_.ProcessName -eq "BatchProcessor"}
    Write-Host "=== Process Status ===" -ForegroundColor Cyan
    Write-Host "Time: $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor Yellow
    Write-Host ""
    if ($processes) {
        $processes | Format-Table ProcessName, Id, CPU, WorkingSet -AutoSize
        Write-Host "Total AutoCAD processes: $($processes.Count)" -ForegroundColor Green
    } else {
        Write-Host "No AutoCAD processes running" -ForegroundColor Green
    }
    Start-Sleep -Seconds 2
}
```

#### Check specific process by ID:
```powershell
$processId = 12345  # Replace with actual process ID
$process = Get-Process -Id $processId -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "Process $processId is running: $($process.ProcessName)"
} else {
    Write-Host "Process $processId is not running (completed or doesn't exist)"
}
```

---

### Method 3: Command Prompt (CMD)

#### List AutoCAD processes:
```cmd
tasklist | findstr /i "accore acad BatchProcessor"
```

#### Count processes:
```cmd
tasklist | findstr /i "accore" | find /c "accore"
```

---

### Method 4: Check Process Exit Code

In PowerShell, you can check if a process exited successfully:

```powershell
# Get process by name
$process = Get-Process -Name "accoreconsole" -ErrorAction SilentlyContinue

if ($process) {
    Write-Host "Process is still running (PID: $($process.Id))"
} else {
    Write-Host "Process has completed or doesn't exist"
}
```

**Note:** Once a process exits, you can't get its exit code from `Get-Process`. Exit codes are only available while the process is still running or immediately after it exits (via Process object).

---

### Method 5: Monitor Output Files

Check if output files are being created:

```powershell
# Watch output folder for new files
$outputFolder = "C:\path\to\output"
Get-ChildItem -Path $outputFolder -Filter "*.json" | 
    Sort-Object LastWriteTime -Descending | 
    Select-Object Name, LastWriteTime, Length | 
    Format-Table -AutoSize
```

---

## Enhanced Logging in the Application

The application already logs process status. Look for these messages in the log output:

### Success Indicators:
- ✅ `Completed processing: [filename] (Success: True)`
- ✅ `PROCESSING COMPLETE: [filename]`
- ✅ `All validations passed`

### Failure Indicators:
- ❌ `Process timed out after 6 minutes`
- ❌ `Process still running after WaitForExitAsync`
- ❌ `Process did not exit after kill`
- ❌ `FAILED: [filename]`

### Progress Indicators:
- 🔄 `Starting processing: [filename]`
- 📊 `Progress: X/Y files completed`

---

## Real-Time Monitoring Script

Save this as `monitor-processes.ps1`:

```powershell
# Monitor BatchProcessor and AutoCAD processes
param(
    [int]$IntervalSeconds = 2
)

Write-Host "Monitoring processes... Press Ctrl+C to stop" -ForegroundColor Cyan
Write-Host ""

while ($true) {
    Clear-Host
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Host "=== Process Monitor - $timestamp ===" -ForegroundColor Cyan
    Write-Host ""
    
    # Check BatchProcessor
    $batchProc = Get-Process -Name "BatchProcessor" -ErrorAction SilentlyContinue
    if ($batchProc) {
        Write-Host "✅ BatchProcessor: RUNNING (PID: $($batchProc.Id))" -ForegroundColor Green
    } else {
        Write-Host "❌ BatchProcessor: NOT RUNNING" -ForegroundColor Red
    }
    
    Write-Host ""
    
    # Check AutoCAD processes
    $acadProcesses = Get-Process | Where-Object {$_.ProcessName -like "*accore*" -or $_.ProcessName -like "*acad*"}
    if ($acadProcesses) {
        Write-Host "AutoCAD Processes: $($acadProcesses.Count) running" -ForegroundColor Yellow
        $acadProcesses | Format-Table ProcessName, Id, @{Label="CPU(s)";Expression={$_.CPU}}, @{Label="Memory(MB)";Expression={[math]::Round($_.WorkingSet/1MB,2)}} -AutoSize
    } else {
        Write-Host "✅ No AutoCAD processes running" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "Refreshing in $IntervalSeconds seconds... (Ctrl+C to stop)" -ForegroundColor Gray
    Start-Sleep -Seconds $IntervalSeconds
}
```

**Usage:**
```powershell
.\monitor-processes.ps1
# Or with custom interval:
.\monitor-processes.ps1 -IntervalSeconds 5
```

---

## What to Look For

### ✅ Normal Operation:
- Number of `accoreconsole.exe` processes ≤ MaxParallel setting
- Processes appear and disappear as files are processed
- Exit codes are 0 (success) or non-zero (expected for validation failures)
- Output files are being created in the output folder

### ⚠️ Problems:
- Processes stay running for > 6 minutes (should timeout and be killed)
- More processes than MaxParallel setting (processes not terminating)
- Processes accumulate over time (memory leak or not cleaning up)
- No processes running but batch processing is active (all hung)

---

## Quick Check Commands

### One-liner to check process count:
```powershell
Write-Host "AutoCAD processes: $((Get-Process | Where-Object {$_.ProcessName -like '*accore*'}).Count)"
```

### Check if specific process exists:
```powershell
$exists = Get-Process -Name "accoreconsole" -ErrorAction SilentlyContinue; if ($exists) { "Running" } else { "Not running" }
```

### Get all process details:
```powershell
Get-Process | Where-Object {$_.ProcessName -like "*accore*" -or $_.ProcessName -eq "BatchProcessor"} | Select-Object ProcessName, Id, StartTime, CPU, WorkingSet | Format-Table -AutoSize
```

---

## In the Application Log

The application logs process completion status. Check the log output for:

1. **Start:** `🔄 Starting processing: [filename]`
2. **Completion:** `✅ Completed processing: [filename]`
3. **Progress:** `📊 Progress: X/Y files completed`
4. **Errors:** `❌ FAILED: [filename]` or timeout warnings

If you see processes starting but not completing, check for:
- Timeout warnings
- Process kill warnings
- Error messages in the log

