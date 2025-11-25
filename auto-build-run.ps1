# Auto Build and Run Script - Runs in background
# This script watches for code changes and automatically rebuilds and restarts the application

$ProjectPath = "BatchProcessor.csproj"
$ExePath = "bin\Debug\net8.0-windows\BatchProcessor.exe"
$process = $null

function Stop-Application {
    if ($process -and !$process.HasExited) {
        try {
            $process.Kill()
            Start-Sleep -Milliseconds 300
        } catch { }
        $process = $null
    }
}

function Build-And-Run {
    Write-Host "`n[$(Get-Date -Format 'HH:mm:ss')] Building..." -ForegroundColor Cyan
    
    Stop-Application
    
    $buildResult = dotnet build $ProjectPath 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Build successful! Starting application..." -ForegroundColor Green
        
        $fullExePath = Join-Path $PSScriptRoot $ExePath
        if (Test-Path $fullExePath) {
            $script:process = Start-Process -FilePath $fullExePath -PassThru -WindowStyle Hidden
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Application started (PID: $($process.Id))" -ForegroundColor Green
        }
    } else {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Build failed!" -ForegroundColor Red
    }
}

# Initial build and run
Build-And-Run

# Watch for file changes
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $PSScriptRoot
$watcher.Filter = "*.*"
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true

$watchExtensions = @('.cs', '.xaml', '.csproj')
$debounceTimer = $null
$debounceDelay = 1500

$onChanged = Register-ObjectEvent $watcher "Changed" -Action {
    $file = $Event.SourceEventArgs.FullPath
    $ext = [System.IO.Path]::GetExtension($file)
    
    if ($watchExtensions -contains $ext -and $file -notlike "*\bin\*" -and $file -notlike "*\obj\*") {
        if ($script:debounceTimer) {
            $script:debounceTimer.Dispose()
        }
        
        $script:debounceTimer = New-Object System.Timers.Timer
        $script:debounceTimer.Interval = $debounceDelay
        $script:debounceTimer.AutoReset = $false
        $script:debounceTimer.Add_Elapsed({
            Build-And-Run
            $script:debounceTimer.Dispose()
            $script:debounceTimer = $null
        })
        $script:debounceTimer.Start()
    }
}

Write-Host "`n✅ Auto build and run is active!" -ForegroundColor Green
Write-Host "Watching for changes in .cs, .xaml, .csproj files..." -ForegroundColor Yellow
Write-Host "Press Ctrl+C to stop`n" -ForegroundColor Yellow

try {
    while ($true) {
        Start-Sleep -Seconds 1
        if ($process -and $process.HasExited) {
            $process = $null
        }
    }
} finally {
    Stop-Application
    if ($watcher) { $watcher.Dispose() }
    if ($debounceTimer) { $debounceTimer.Dispose() }
}

