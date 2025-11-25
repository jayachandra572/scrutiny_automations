# Monitor BatchProcessor and AutoCAD processes in real-time
# Usage: .\monitor-processes.ps1 [interval_seconds]

param(
    [int]$IntervalSeconds = 2
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Process Monitor for BatchProcessor" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Monitoring processes... Press Ctrl+C to stop" -ForegroundColor Yellow
Write-Host "Refresh interval: $IntervalSeconds seconds" -ForegroundColor Gray
Write-Host ""

try {
    while ($true) {
        Clear-Host
        $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        Write-Host "=== Process Monitor - $timestamp ===" -ForegroundColor Cyan
        Write-Host ""
        
        # Check BatchProcessor
        $batchProc = Get-Process -Name "BatchProcessor" -ErrorAction SilentlyContinue
        if ($batchProc) {
            Write-Host "✅ BatchProcessor: RUNNING" -ForegroundColor Green
            Write-Host "   PID: $($batchProc.Id)" -ForegroundColor Gray
            Write-Host "   Memory: $([math]::Round($batchProc.WorkingSet/1MB, 2)) MB" -ForegroundColor Gray
            Write-Host "   CPU Time: $($batchProc.CPU) seconds" -ForegroundColor Gray
        } else {
            Write-Host "❌ BatchProcessor: NOT RUNNING" -ForegroundColor Red
        }
        
        Write-Host ""
        
        # Check AutoCAD processes
        $acadProcesses = Get-Process | Where-Object {
            $_.ProcessName -like "*accore*" -or 
            $_.ProcessName -like "*acad*" -or
            $_.ProcessName -eq "acadConsole"
        }
        
        if ($acadProcesses) {
            $count = $acadProcesses.Count
            Write-Host "AutoCAD Processes: $count running" -ForegroundColor Yellow
            Write-Host ""
            
            $acadProcesses | ForEach-Object {
                $memMB = [math]::Round($_.WorkingSet/1MB, 2)
                $cpuTime = $_.CPU
                $runtime = if ($_.StartTime) { 
                    (Get-Date) - $_.StartTime 
                } else { 
                    "N/A" 
                }
                
                Write-Host "  • $($_.ProcessName) (PID: $($_.Id))" -ForegroundColor White
                Write-Host "    Memory: $memMB MB | CPU: $cpuTime s | Runtime: $runtime" -ForegroundColor Gray
            }
        } else {
            Write-Host "✅ No AutoCAD processes running" -ForegroundColor Green
        }
        
        Write-Host ""
        Write-Host "─" * 50 -ForegroundColor DarkGray
        Write-Host "Refreshing in $IntervalSeconds seconds... (Ctrl+C to stop)" -ForegroundColor Gray
        Start-Sleep -Seconds $IntervalSeconds
    }
}
catch {
    Write-Host ""
    Write-Host "Monitoring stopped." -ForegroundColor Yellow
}

