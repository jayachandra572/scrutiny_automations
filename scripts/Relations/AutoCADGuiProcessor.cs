using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace BatchProcessor.Relations
{
    /// <summary>
    /// Results from batch processing multiple drawings
    /// </summary>
    public class ProcessingResults
    {
        public int TotalFiles { get; set; }
        public List<string> ProcessedFiles { get; set; } = new List<string>();
        public List<string> FailedFiles { get; set; } = new List<string>();
    }

    /// <summary>
    /// Handles automation of AutoCAD GUI for processing drawings with UIPlugin DLL
    /// </summary>
    public class AutoCADGuiProcessor
    {
        private readonly string _autocadExePath;
        private readonly string _uiPluginDllPath;
        private const int CommandTimeoutSeconds = 300; // 5 minutes max per file
        private const int PopupCheckIntervalMs = 200;
        private Process? _autocadProcess;

        public AutoCADGuiProcessor(string autocadExePath, string uiPluginDllPath)
        {
            _autocadExePath = autocadExePath ?? throw new ArgumentNullException(nameof(autocadExePath));
            _uiPluginDllPath = uiPluginDllPath ?? throw new ArgumentNullException(nameof(uiPluginDllPath));
        }

        /// <summary>
        /// Process all drawing files in a single AutoCAD session
        /// </summary>
        public async Task<ProcessingResults> ProcessAllDrawingsAsync(
            string[] drawingPaths,
            CancellationToken cancellationToken,
            Action<string>? logCallback = null,
            IProgress<(int current, int total, string currentFile)>? progress = null)
        {
            var results = new ProcessingResults
            {
                TotalFiles = drawingPaths.Length,
                ProcessedFiles = new List<string>(),
                FailedFiles = new List<string>()
            };

            if (drawingPaths.Length == 0)
            {
                logCallback?.Invoke("No drawing files to process");
                return results;
            }

            logCallback?.Invoke($"📊 Processing {drawingPaths.Length} file(s) - each in separate AutoCAD session");

            // Process each file in a separate AutoCAD session
            for (int i = 0; i < drawingPaths.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    logCallback?.Invoke("⚠️ Processing was cancelled");
                    break;
                }

                string drawingPath = drawingPaths[i];
                string fileName = Path.GetFileName(drawingPath);

                // Validate file exists
                if (string.IsNullOrWhiteSpace(drawingPath) || !File.Exists(drawingPath))
                {
                    logCallback?.Invoke($"❌ Skipping invalid or missing file: {fileName}");
                    results.FailedFiles.Add(fileName);
                    progress?.Report((i + 1, drawingPaths.Length, fileName));
                    continue;
                }

                logCallback?.Invoke($"");
                logCallback?.Invoke($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                logCallback?.Invoke($"📄 Processing file {i + 1}/{drawingPaths.Length}: {fileName}");
                logCallback?.Invoke($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                string tempScriptPath = "";
                string logFilePath = "";
                try
                {
                    // Create script for this single file
                    tempScriptPath = CreateProcessingScript(drawingPath, out logFilePath);
                    logCallback?.Invoke($"📝 Created script: {Path.GetFileName(tempScriptPath)}");
                    if (!string.IsNullOrEmpty(logFilePath))
                    {
                        logCallback?.Invoke($"📋 Log file: {Path.GetFileName(logFilePath)}");
                    }

                    // Ensure any previous AutoCAD instances are closed
                    CloseExistingAutoCADInstances();
                    await Task.Delay(1000, cancellationToken); // Wait a bit for processes to fully close

                    // Launch AutoCAD for this file
                    logCallback?.Invoke("🚀 Launching AutoCAD...");
                    _autocadProcess = LaunchAutoCAD(tempScriptPath, logCallback);

                    if (_autocadProcess == null)
                    {
                        logCallback?.Invoke("❌ Failed to launch AutoCAD");
                        results.FailedFiles.Add(fileName);
                        progress?.Report((i + 1, drawingPaths.Length, fileName));
                        continue;
                    }

                    // Wait for AutoCAD to start
                    await Task.Delay(3000, cancellationToken);
                    logCallback?.Invoke("⏳ Waiting for AutoCAD to initialize...");

                    // Monitor for popups and handle them
                    var popupMonitorTask = MonitorAndHandlePopupsAsync(cancellationToken, logCallback);

                    // Wait for script to complete or timeout
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(CommandTimeoutSeconds), cancellationToken);
                    var processTask = WaitForProcessCompletionAsync(_autocadProcess, cancellationToken);

                    var completedTask = await Task.WhenAny(processTask, timeoutTask, popupMonitorTask);

                    if (completedTask == timeoutTask)
                    {
                        logCallback?.Invoke($"⚠️ Processing timed out after {CommandTimeoutSeconds} seconds");
                        KillAutoCADProcess();
                        results.FailedFiles.Add(fileName);
                        progress?.Report((i + 1, drawingPaths.Length, fileName));
                        continue;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        logCallback?.Invoke("⚠️ Processing was cancelled");
                        KillAutoCADProcess();
                        break;
                    }

                    // Wait a bit more to ensure process has fully exited
                    await Task.Delay(2000, cancellationToken);

                    // Read AutoCAD log file if it exists
                    if (!string.IsNullOrEmpty(logFilePath) && File.Exists(logFilePath))
                    {
                        try
                        {
                            await Task.Delay(500, cancellationToken); // Wait a bit more for file to be fully written
                            string[] logLines = File.ReadAllLines(logFilePath);
                            logCallback?.Invoke($"");
                            logCallback?.Invoke($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                            logCallback?.Invoke($"📋 AutoCAD Command Line Log:");
                            logCallback?.Invoke($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                            foreach (string line in logLines)
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    logCallback?.Invoke($"  {line}");
                                }
                            }
                            logCallback?.Invoke($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                            logCallback?.Invoke($"");
                        }
                        catch (Exception logEx)
                        {
                            logCallback?.Invoke($"⚠️ Could not read log file: {logEx.Message}");
                        }
                    }

                    // Check if process exited successfully
                    if (_autocadProcess.HasExited)
                    {
                        int exitCode = _autocadProcess.ExitCode;
                        logCallback?.Invoke($"✅ AutoCAD process completed with exit code: {exitCode}");
                        
                        if (exitCode == 0)
                        {
                            results.ProcessedFiles.Add(fileName);
                            logCallback?.Invoke($"✅ Successfully processed: {fileName}");
                        }
                        else
                        {
                            results.FailedFiles.Add(fileName);
                            logCallback?.Invoke($"❌ Failed to process: {fileName} (exit code: {exitCode})");
                        }
                    }
                    else
                    {
                        logCallback?.Invoke("⚠️ AutoCAD process did not exit - forcing termination");
                        KillAutoCADProcess();
                        results.FailedFiles.Add(fileName);
                        logCallback?.Invoke($"❌ Failed to process: {fileName}");
                    }

                    // Ensure AutoCAD is fully closed before processing next file
                    CloseExistingAutoCADInstances();
                    await Task.Delay(2000, cancellationToken); // Wait for AutoCAD to fully close
                }
                catch (OperationCanceledException)
                {
                    logCallback?.Invoke("⚠️ Processing cancelled");
                    KillAutoCADProcess();
                    break;
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"❌ Error processing {fileName}: {ex.Message}");
                    KillAutoCADProcess();
                    results.FailedFiles.Add(fileName);
                }
                finally
                {
                    // Clean up script file
                    try
                    {
                        if (!string.IsNullOrEmpty(tempScriptPath) && File.Exists(tempScriptPath))
                        {
                            await Task.Delay(500); // Wait a bit before deleting
                            File.Delete(tempScriptPath);
                        }
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }

                    // Ensure process is killed
                    KillAutoCADProcess();
                }

                progress?.Report((i + 1, drawingPaths.Length, fileName));
            }

            logCallback?.Invoke($"");
            logCallback?.Invoke($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            logCallback?.Invoke($"📊 Batch processing complete!");
            logCallback?.Invoke($"   ✅ Successful: {results.ProcessedFiles.Count}");
            logCallback?.Invoke($"   ❌ Failed: {results.FailedFiles.Count}");
            logCallback?.Invoke($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            return results;
        }

        /// <summary>
        /// Process a single drawing file through AutoCAD GUI (legacy method - kept for compatibility)
        /// </summary>
        public async Task<bool> ProcessDrawingAsync(
            string drawingPath,
            CancellationToken cancellationToken,
            Action<string>? logCallback = null)
        {
            if (!File.Exists(drawingPath))
            {
                logCallback?.Invoke($"❌ Drawing file not found: {drawingPath}");
                return false;
            }

            if (!File.Exists(_uiPluginDllPath))
            {
                logCallback?.Invoke($"❌ UIPlugin DLL not found: {_uiPluginDllPath}");
                return false;
            }

            string tempScriptPath = "";
            string logFilePath = "";
            try
            {
                // Create script file
                tempScriptPath = CreateProcessingScript(drawingPath, out logFilePath);
                logCallback?.Invoke($"📝 Created script: {Path.GetFileName(tempScriptPath)}");
                if (!string.IsNullOrEmpty(logFilePath))
                {
                    logCallback?.Invoke($"📋 Log file: {Path.GetFileName(logFilePath)}");
                }

                // Launch AutoCAD with drawing and script
                logCallback?.Invoke("🚀 Launching AutoCAD...");
                logCallback?.Invoke($"📂 Drawing: {Path.GetFileName(drawingPath)}");
                _autocadProcess = LaunchAutoCAD(drawingPath, tempScriptPath, logCallback);
                
                if (_autocadProcess == null)
                {
                    logCallback?.Invoke("❌ Failed to launch AutoCAD");
                    return false;
                }

                // Wait for AutoCAD to start
                await Task.Delay(3000, cancellationToken);
                logCallback?.Invoke("⏳ Waiting for AutoCAD to initialize...");

                // Monitor for popups and handle them
                var popupMonitorTask = MonitorAndHandlePopupsAsync(cancellationToken, logCallback);

                // Wait for script to complete or timeout
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(CommandTimeoutSeconds), cancellationToken);
                var processTask = WaitForProcessCompletionAsync(_autocadProcess, cancellationToken);

                var completedTask = await Task.WhenAny(processTask, timeoutTask, popupMonitorTask);

                if (completedTask == timeoutTask)
                {
                    logCallback?.Invoke($"⚠️ Processing timed out after {CommandTimeoutSeconds} seconds");
                    KillAutoCADProcess();
                    return false;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    logCallback?.Invoke("⚠️ Processing was cancelled");
                    KillAutoCADProcess();
                    return false;
                }

                // Read AutoCAD log file if it exists
                if (!string.IsNullOrEmpty(logFilePath) && File.Exists(logFilePath))
                {
                    try
                    {
                        await Task.Delay(500, cancellationToken); // Wait a bit more for file to be fully written
                        string[] logLines = File.ReadAllLines(logFilePath);
                        logCallback?.Invoke($"");
                        logCallback?.Invoke($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                        logCallback?.Invoke($"📋 AutoCAD Command Line Log:");
                        logCallback?.Invoke($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                        foreach (string line in logLines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                logCallback?.Invoke($"  {line}");
                            }
                        }
                        logCallback?.Invoke($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                        logCallback?.Invoke($"");
                    }
                    catch (Exception logEx)
                    {
                        logCallback?.Invoke($"⚠️ Could not read log file: {logEx.Message}");
                    }
                }

                // Read AutoCAD log file if it exists
                if (!string.IsNullOrEmpty(logFilePath) && File.Exists(logFilePath))
                {
                    try
                    {
                        await Task.Delay(500, cancellationToken); // Wait a bit more for file to be fully written
                        string[] logLines = File.ReadAllLines(logFilePath);
                        logCallback?.Invoke($"");
                        logCallback?.Invoke($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                        logCallback?.Invoke($"📋 AutoCAD Command Line Log (WriteMessage output):");
                        logCallback?.Invoke($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                        foreach (string line in logLines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                logCallback?.Invoke($"  {line}");
                            }
                        }
                        logCallback?.Invoke($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                        logCallback?.Invoke($"");
                    }
                    catch (Exception logEx)
                    {
                        logCallback?.Invoke($"⚠️ Could not read log file: {logEx.Message}");
                    }
                }

                // Check if process exited successfully
                if (_autocadProcess.HasExited)
                {
                    int exitCode = _autocadProcess.ExitCode;
                    logCallback?.Invoke($"✅ AutoCAD process completed with exit code: {exitCode}");
                    return exitCode == 0;
                }

                // If process is still running, wait a bit more for it to finish
                await Task.Delay(2000, cancellationToken);
                
                if (_autocadProcess.HasExited)
                {
                    return _autocadProcess.ExitCode == 0;
                }

                // Process still running - kill it and return false
                logCallback?.Invoke("⚠️ AutoCAD process did not exit - forcing termination");
                KillAutoCADProcess();
                return false;
            }
            catch (OperationCanceledException)
            {
                logCallback?.Invoke("⚠️ Processing cancelled");
                KillAutoCADProcess();
                return false;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"❌ Error during processing: {ex.Message}");
                KillAutoCADProcess();
                return false;
            }
            finally
            {
                // Clean up script file
                try
                {
                    if (!string.IsNullOrEmpty(tempScriptPath) && File.Exists(tempScriptPath))
                    {
                        await Task.Delay(1000); // Wait a bit before deleting
                        File.Delete(tempScriptPath);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }

                // Ensure process is killed
                KillAutoCADProcess();
            }
        }

        /// <summary>
        /// Create a batch script file that processes all drawings in a single AutoCAD session
        /// This function processes multiple files sequentially in one AutoCAD instance
        /// </summary>
 private string CreateBatchProcessingScript(string[] drawingPaths)
{
    // Validate input
    if (drawingPaths == null || drawingPaths.Length == 0)
        throw new ArgumentException("Drawing paths array cannot be null or empty", nameof(drawingPaths));

    if (string.IsNullOrWhiteSpace(_uiPluginDllPath) || !File.Exists(_uiPluginDllPath))
        throw new FileNotFoundException($"UIPlugin DLL not found: {_uiPluginDllPath}");

    string tempDir = Path.GetTempPath();
    string scriptName = $"create_relations_batch_{Guid.NewGuid():N}.scr";
    string scriptPath = Path.Combine(tempDir, scriptName);

    // Create log file path for AutoCAD command line output
    string batchLogFilePath = Path.Combine(tempDir, $"autocad_cmdlog_batch_{Guid.NewGuid():N}.log");
    string escapedBatchLogPath = batchLogFilePath.Replace("\\", "/");

    string escapedDllPath = _uiPluginDllPath.Replace("\\", "/");
    var sb = new System.Text.StringBuilder();

    sb.AppendLine($"; Batch Processing Script - {drawingPaths.Length} file(s)");
    sb.AppendLine($"; Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine();
    
    // Enable AutoCAD logging to capture WriteMessage output
    sb.AppendLine("(setvar \"CMDECHO\" 1)");
    sb.AppendLine($"(setvar \"LOGFILEMODE\" 1)"); // Enable log file
    sb.AppendLine($"(setvar \"LOGFILENAME\" \"{escapedBatchLogPath}\")"); // Set log file path
    sb.AppendLine($"(princ (strcat \"\\n[AutoCAD] Starting batch processing - {drawingPaths.Length} file(s)\\n\"))");
    sb.AppendLine($"(princ (strcat \"[AutoCAD] Log file: {escapedBatchLogPath}\\n\"))");
    sb.AppendLine();

    for (int i = 0; i < drawingPaths.Length; i++)
    {
        string path = drawingPaths[i];

        if (string.IsNullOrWhiteSpace(path))
        {
            sb.AppendLine($"; WARNING: Skipping empty path at index {i}");
            continue;
        }
        if (!File.Exists(path))
        {
            sb.AppendLine($"; ERROR: File not found: {path}");
            continue;
        }

        string escapedDwgPath = path.Replace("\\", "/");
        string fileName = Path.GetFileName(path);

        sb.AppendLine($"; ========================================");
        sb.AppendLine($"; Processing file {i + 1}/{drawingPaths.Length}: {fileName}");
        sb.AppendLine($"; ========================================");
        sb.AppendLine();

        // Close previous drawing but ONLY after first file
        if (i > 0)
        {
            sb.AppendLine($"; Closing previous drawing");
            sb.AppendLine("_.CLOSE");
            sb.AppendLine("DELAY 1000");   // Wait 1 second for close to finish
        }

        // Open drawing
        sb.AppendLine($"; Opening drawing");
        sb.AppendLine($"(princ (strcat \"\\n[AutoCAD] Opening drawing: {Path.GetFileName(path)}\\n\"))");
        sb.AppendLine($"_.OPEN \"{escapedDwgPath}\"");
        sb.AppendLine("(princ \"[AutoCAD] Drawing opened successfully\\n\")");
        sb.AppendLine("DELAY 2000");   // Wait 2 seconds for drawing to fully load

        // Load plugin
        sb.AppendLine($"; Loading UIPlugin DLL");
        sb.AppendLine($"(princ (strcat \"[AutoCAD] Loading UIPlugin DLL: {Path.GetFileName(_uiPluginDllPath)}\\n\"))");
        sb.AppendLine($"_.NETLOAD \"{escapedDllPath}\"");
        sb.AppendLine("(princ \"[AutoCAD] UIPlugin DLL loaded\\n\")");
        sb.AppendLine("DELAY 1000");   // Wait 1 second for DLL to load

        // Ready check
        sb.AppendLine("_.REDRAW");
        sb.AppendLine("(princ \"[AutoCAD] Ready to execute command\\n\")");

        // Execute your command
        sb.AppendLine($"; Running CREATE_RELATIONS_FOR_ENTITIES");
        sb.AppendLine("(princ \"[AutoCAD] Executing CREATE_RELATIONS_FOR_ENTITIES command...\\n\")");
        sb.AppendLine("CREATE_RELATIONS_FOR_ENTITIES");
        sb.AppendLine("(princ \"[AutoCAD] CREATE_RELATIONS_FOR_ENTITIES command completed\\n\")");
        sb.AppendLine("DELAY 2000");   // Wait 2 seconds for command to complete

        // Save drawing
        sb.AppendLine($"; Saving drawing");
        sb.AppendLine("(princ \"[AutoCAD] Saving drawing...\\n\")");
        sb.AppendLine("_.QSAVE");
        sb.AppendLine("(princ \"[AutoCAD] Drawing saved\\n\")");
        sb.AppendLine();
    }

    // Quit AutoCAD
    sb.AppendLine($"; ========================================");
    sb.AppendLine($"; All drawings processed - closing AutoCAD");
    sb.AppendLine($"; ========================================");
    sb.AppendLine("(princ \"[AutoCAD] Batch processing complete!\\n\")");
    sb.AppendLine($"(setvar \"LOGFILEMODE\" 0)"); // Disable log file before quit
    sb.AppendLine("_.QUIT");   // No Y → SAFE
    sb.AppendLine();

    // Write to file
    string scriptContent = sb.ToString();
    File.WriteAllText(scriptPath, scriptContent);

    // Save debug copy (optional)
    try
    {
        string debugScriptPath = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? Path.GetTempPath(),
            $"debug_batch_script_{DateTime.Now:yyyyMMdd_HHmmss}.scr"
        );
        File.WriteAllText(debugScriptPath, scriptContent);
    }
    catch { }

    return scriptPath;
}


        /// <summary>
        /// Create a script file for processing a single drawing (legacy method)
        /// </summary>
        private string CreateProcessingScript(string drawingPath, out string logFilePath)
        {
            string tempDir = Path.GetTempPath();
            string scriptName = $"create_relations_{Guid.NewGuid():N}.scr";
            string scriptPath = Path.Combine(tempDir, scriptName);

            // Create log file path for AutoCAD command line output
            logFilePath = Path.Combine(tempDir, $"autocad_cmdlog_{Guid.NewGuid():N}.log");
            string escapedLogPath = logFilePath.Replace("\\", "/");

            // Escape paths - use forward slashes for AutoCAD
            string escapedDllPath = _uiPluginDllPath.Replace("\\", "/");
            string escapedDwgPath = drawingPath.Replace("\\", "/");
            string fileName = Path.GetFileName(drawingPath);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"; Processing Script - {fileName}");
            sb.AppendLine($"; Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            
            // Enable command echo and AutoCAD logging
            sb.AppendLine("(setvar \"CMDECHO\" 1)");
            sb.AppendLine($"(setvar \"LOGFILEMODE\" 1)"); // Enable log file
            sb.AppendLine($"(setvar \"LOGFILENAME\" \"{escapedLogPath}\")"); // Set log file path
            sb.AppendLine($"(princ (strcat \"\\n[AutoCAD] Starting processing: {fileName}\\n\"))");
            sb.AppendLine($"(princ (strcat \"[AutoCAD] Log file: {logFilePath}\\n\"))");

            // Open drawing
            sb.AppendLine($"; Opening drawing: {fileName}");
            sb.AppendLine($"(princ (strcat \"\\n[AutoCAD] Opening drawing: {fileName}\\n\"))");
            sb.AppendLine($"_.OPEN \"{escapedDwgPath}\"");
            sb.AppendLine("(princ \"[AutoCAD] Drawing opened successfully\\n\")");

            // Load plugin
            sb.AppendLine($"; Loading UIPlugin DLL");
            sb.AppendLine($"(princ (strcat \"[AutoCAD] Loading UIPlugin DLL: {Path.GetFileName(_uiPluginDllPath)}\\n\"))");
            sb.AppendLine($"_.NETLOAD \"{escapedDllPath}\"");
            sb.AppendLine("(princ \"[AutoCAD] UIPlugin DLL loaded\\n\")");

            // Ready check
            sb.AppendLine("_.REDRAW");
            sb.AppendLine("(princ \"[AutoCAD] Ready to execute command\\n\")");

            // Execute command
            sb.AppendLine($"; Running CREATE_RELATIONS_FOR_ENTITIES");
            sb.AppendLine("(princ \"[AutoCAD] Executing CREATE_RELATIONS_FOR_ENTITIES command...\\n\")");
            sb.AppendLine("CREATE_RELATIONS_FOR_ENTITIES");
            sb.AppendLine("(princ \"[AutoCAD] CREATE_RELATIONS_FOR_ENTITIES command completed\\n\")");

            // Save drawing
            sb.AppendLine($"; Saving drawing");
            sb.AppendLine("(princ \"[AutoCAD] Saving drawing...\\n\")");
            sb.AppendLine("_.QSAVE");
            sb.AppendLine("(princ \"[AutoCAD] Drawing saved\\n\")");

            // Quit AutoCAD
            sb.AppendLine($"; Closing AutoCAD");
            sb.AppendLine("(princ \"[AutoCAD] Closing AutoCAD...\\n\")");
            sb.AppendLine("(princ \"[AutoCAD] Processing complete!\\n\")");
            sb.AppendLine($"(setvar \"LOGFILEMODE\" 0)"); // Disable log file before quit
            sb.AppendLine("_.QUIT");
            sb.AppendLine();

            string scriptContent = sb.ToString();
            File.WriteAllText(scriptPath, scriptContent);
            
            // Store log file path - we'll read it after processing
            // Note: We'll need to modify the method signature to return both script path and log path
            // For now, we'll construct the log path in the calling method
            return scriptPath;
        }

        /// <summary>
        /// Launch AutoCAD with the script (overloaded for batch processing)
        /// </summary>
        private Process? LaunchAutoCAD(string scriptPath, Action<string>? logCallback = null)
        {
            try
            {
                // First, try to close any existing AutoCAD instances
                CloseExistingAutoCADInstances();

                var startInfo = new ProcessStartInfo
                {
                    FileName = _autocadExePath,
                    // Run script file: acad.exe /b script.scr
                    // Use /nologo to suppress logo, /s to run script, and redirect output
                    Arguments = $"/nologo /b \"{scriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true, // Must be true to capture output
                    WorkingDirectory = Path.GetDirectoryName(_autocadExePath) ?? ""
                };

                var process = Process.Start(startInfo);
                
                if (process != null && logCallback != null)
                {
                    // Capture stdout - AutoCAD GUI doesn't output much, but LISP (princ) commands will show here
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            string data = e.Data.Trim();
                            // Show all output from LISP (princ) commands and important messages
                            if (!string.IsNullOrWhiteSpace(data))
                            {
                                // Check if it's our log message (starts with [AutoCAD])
                                if (data.Contains("[AutoCAD]"))
                                {
                                    logCallback(data);
                                }
                                // Also show errors and important messages
                                else if (data.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                         data.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                                         data.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                                         data.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                                         data.Contains("NETLOAD", StringComparison.OrdinalIgnoreCase))
                                {
                                    logCallback($"[AutoCAD] {data}");
                                }
                            }
                        }
                    };
                    
                    // Capture stderr
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            logCallback($"[AutoCAD Error] {e.Data}");
                        }
                    };
                    
                    // Start async reading
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                
                return process;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error launching AutoCAD: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Launch AutoCAD with the script (legacy method - kept for single file processing)
        /// </summary>
        private Process? LaunchAutoCAD(string drawingPath, string scriptPath, Action<string>? logCallback = null)
        {
            try
            {
                // First, try to close any existing AutoCAD instances
                CloseExistingAutoCADInstances();

                var startInfo = new ProcessStartInfo
                {
                    FileName = _autocadExePath,
                    // Run script file: acad.exe /b script.scr
                    // Script file will open the drawing and process it
                    Arguments = $"/nologo /b \"{scriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true, // Must be true to capture output
                    WorkingDirectory = Path.GetDirectoryName(_autocadExePath) ?? ""
                };

                var process = Process.Start(startInfo);
                
                if (process != null && logCallback != null)
                {
                    // Capture stdout - AutoCAD GUI doesn't output much, but LISP (princ) commands will show here
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            string data = e.Data.Trim();
                            // Show all output from LISP (princ) commands and important messages
                            if (!string.IsNullOrWhiteSpace(data))
                            {
                                // Check if it's our log message (starts with [AutoCAD])
                                if (data.Contains("[AutoCAD]"))
                                {
                                    logCallback(data);
                                }
                                // Also show errors and important messages
                                else if (data.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                         data.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                                         data.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                                         data.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                                         data.Contains("NETLOAD", StringComparison.OrdinalIgnoreCase))
                                {
                                    logCallback($"[AutoCAD] {data}");
                                }
                            }
                        }
                    };
                    
                    // Capture stderr
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            logCallback($"[AutoCAD Error] {e.Data}");
                        }
                    };
                    
                    // Start async reading
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                
                return process;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error launching AutoCAD: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Monitor for popups and automatically dismiss them
        /// </summary>
        private async Task MonitorAndHandlePopupsAsync(CancellationToken cancellationToken, Action<string>? logCallback)
        {
            int consecutivePopupCount = 0;
            while (!cancellationToken.IsCancellationRequested && (_autocadProcess == null || !_autocadProcess.HasExited))
            {
                try
                {
                    await Task.Delay(PopupCheckIntervalMs, cancellationToken);

                    // Find AutoCAD windows
                    var autocadWindows = FindAutoCADWindows();
                    bool foundPopup = false;
                    
                    foreach (var window in autocadWindows)
                    {
                        // Check if this is a dialog/popup
                        if (IsPopupWindow(window))
                        {
                            foundPopup = true;
                            consecutivePopupCount++;
                            string windowName = window.Current.Name ?? "Unknown";
                            logCallback?.Invoke($"⚠️ Popup detected ({consecutivePopupCount}x): {windowName} - attempting to dismiss...");
                            
                            if (DismissPopup(window))
                            {
                                logCallback?.Invoke("✅ Popup dismissed");
                                consecutivePopupCount = 0; // Reset counter on success
                            }
                            else
                            {
                                logCallback?.Invoke("⚠️ Failed to dismiss popup - trying Enter key");
                                // Try pressing Enter as fallback
                                SendKeys.SendWait("{ENTER}");
                                await Task.Delay(500, cancellationToken);
                                
                                // Also try Escape key in case it's a cancelable dialog
                                SendKeys.SendWait("{ESC}");
                                await Task.Delay(300, cancellationToken);
                            }
                        }
                    }
                    
                    // If no popup found, reset counter
                    if (!foundPopup)
                    {
                        consecutivePopupCount = 0;
                    }
                    // If we've seen the same popup many times, it might be stuck
                    else if (consecutivePopupCount > 10)
                    {
                        logCallback?.Invoke("⚠️ Popup appears stuck - trying aggressive dismissal");
                        // Try multiple dismissal methods
                        SendKeys.SendWait("{ESC}");
                        await Task.Delay(200, cancellationToken);
                        SendKeys.SendWait("{ENTER}");
                        await Task.Delay(200, cancellationToken);
                        SendKeys.SendWait("{TAB}{ENTER}"); // Tab to OK button and press Enter
                        await Task.Delay(200, cancellationToken);
                        consecutivePopupCount = 0; // Reset to prevent infinite loop
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Log but continue monitoring
                    System.Diagnostics.Debug.WriteLine($"Error monitoring popups: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Find AutoCAD windows using UI Automation
        /// </summary>
        private AutomationElement[] FindAutoCADWindows()
        {
            try
            {
                var root = AutomationElement.RootElement;
                var condition = new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                    new OrCondition(
                        new PropertyCondition(AutomationElement.NameProperty, "AutoCAD"),
                        new PropertyCondition(AutomationElement.ClassNameProperty, "Afx:00400000:8:00010003:00000006:00000000")
                    )
                );

                var windows = root.FindAll(TreeScope.Children, condition);
                var result = new AutomationElement[windows.Count];
                for (int i = 0; i < windows.Count; i++)
                {
                    result[i] = windows[i];
                }
                return result;
            }
            catch
            {
                return Array.Empty<AutomationElement>();
            }
        }

        /// <summary>
        /// Check if a window is a popup/dialog
        /// </summary>
        private bool IsPopupWindow(AutomationElement window)
        {
            try
            {
                // Check if window is a dialog by looking for dialog characteristics
                // (DialogPattern is not available in all .NET versions, so we check other patterns)

                // Check for window pattern
                var windowPattern = window.GetCurrentPattern(WindowPattern.Pattern) as WindowPattern;
                if (windowPattern != null && windowPattern.Current.WindowInteractionState == WindowInteractionState.ReadyForUserInteraction)
                {
                    string name = window.Current.Name ?? "";
                    // Common AutoCAD dialog titles/keywords
                    if (name.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Alert", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Dialog", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Message", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                // Check for button patterns (dialogs usually have buttons)
                var buttons = window.FindAll(TreeScope.Descendants, 
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                
                if (buttons.Count > 0 && buttons.Count <= 5) // Dialogs typically have 1-5 buttons
                {
                    // Check if it's a modal dialog by looking at its parent
                    AutomationElement parent = TreeWalker.RawViewWalker.GetParent(window);
                    if (parent != null && parent.Current.ProcessId == window.Current.ProcessId)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempt to dismiss a popup window
        /// </summary>
        private bool DismissPopup(AutomationElement popup)
        {
            try
            {
                // Try to find OK, Yes, Close, or Skip button
                var buttonConditions = new[]
                {
                    new PropertyCondition(AutomationElement.NameProperty, "OK"),
                    new PropertyCondition(AutomationElement.NameProperty, "Yes"),
                    new PropertyCondition(AutomationElement.NameProperty, "Close"),
                    new PropertyCondition(AutomationElement.NameProperty, "Skip"),
                    new PropertyCondition(AutomationElement.NameProperty, "Cancel"),
                    new PropertyCondition(AutomationElement.NameProperty, "Continue")
                };

                foreach (var condition in buttonConditions)
                {
                    var buttons = popup.FindAll(TreeScope.Descendants, 
                        new AndCondition(
                            condition,
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)
                        ));

                    if (buttons.Count > 0)
                    {
                        var button = buttons[0];
                        var invokePattern = button.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
                        if (invokePattern != null)
                        {
                            invokePattern.Invoke();
                            return true;
                        }
                    }
                }

                // Fallback: try to find first button and click it
                var allButtons = popup.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

                if (allButtons.Count > 0)
                {
                    var firstButton = allButtons[0];
                    var invokePattern = firstButton.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
                    if (invokePattern != null)
                    {
                        invokePattern.Invoke();
                        return true;
                    }
                }

                // Last resort: send Enter key
                SendKeys.SendWait("{ENTER}");
                return false;
            }
            catch
            {
                // Try Enter key as fallback
                try
                {
                    SendKeys.SendWait("{ENTER}");
                }
                catch { }
                return false;
            }
        }

        /// <summary>
        /// Wait for process to complete
        /// </summary>
        private async Task WaitForProcessCompletionAsync(Process process, CancellationToken cancellationToken)
        {
            try
            {
                while (!process.HasExited && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(500, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
        }

        /// <summary>
        /// Close any existing AutoCAD instances
        /// </summary>
        private void CloseExistingAutoCADInstances()
        {
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => p.ProcessName.Equals("acad", StringComparison.OrdinalIgnoreCase) ||
                               p.ProcessName.Equals("acadlt", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var proc in processes)
                {
                    try
                    {
                        if (!proc.HasExited)
                        {
                            proc.CloseMainWindow();
                            if (!proc.WaitForExit(3000))
                            {
                                proc.Kill();
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors closing processes
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
        }

        /// <summary>
        /// Kill the AutoCAD process if it's still running
        /// </summary>
        private void KillAutoCADProcess()
        {
            try
            {
                if (_autocadProcess != null && !_autocadProcess.HasExited)
                {
                    _autocadProcess.Kill();
                    _autocadProcess.WaitForExit(5000);
                }
            }
            catch
            {
                // Ignore errors
            }
            finally
            {
                _autocadProcess?.Dispose();
                _autocadProcess = null;
            }
        }
    }
}

