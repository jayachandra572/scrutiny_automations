using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchProcessor.PreScrutiny
{
    /// <summary>
    /// Processes multiple AutoCAD drawing files in parallel using accoreconsole.exe
    /// without opening AutoCAD GUI. Fully standalone - no project dependencies!
    /// </summary>
    public class DrawingBatchProcessor
    {
        private readonly string _accoreconsoleExePath;
        private readonly List<string> _dllsToLoad;
        private readonly string _mainCommand;
        private readonly int _maxParallelism;
        private readonly string _tempScriptFolder;
        private readonly bool _enableVerboseLogging;
        private CsvParameterMapper _csvMapper;
        private bool _useCsvMapping;

        public DrawingBatchProcessor(
            string accoreconsoleExePath,
            List<string> dllsToLoad,
            string mainCommand = "ProcessWithJsonBatch",
            int maxParallelism = 4,
            string tempScriptFolder = "",
            bool enableVerboseLogging = false)
        {
            _accoreconsoleExePath = accoreconsoleExePath;
            _dllsToLoad = dllsToLoad ?? new List<string>();
            _mainCommand = string.IsNullOrWhiteSpace(mainCommand) ? "RunPreScrutinyValidationsBatch" : mainCommand;
            _maxParallelism = maxParallelism;
            _tempScriptFolder = string.IsNullOrWhiteSpace(tempScriptFolder) ? Path.GetTempPath() : tempScriptFolder;
            _enableVerboseLogging = enableVerboseLogging;
            _useCsvMapping = false;
        }

        /// <summary>
        /// Enable CSV-based parameter mapping
        /// </summary>
        public bool EnableCsvMapping(string csvFilePath)
        {
            _csvMapper = new CsvParameterMapper(csvFilePath);
            _useCsvMapping = _csvMapper.LoadCsv();
            
            if (_useCsvMapping)
            {
                Console.WriteLine($"\n✅ CSV Parameter Mapping Enabled");
                Console.WriteLine(_csvMapper.GetStatistics());
            }
            
            return _useCsvMapping;
        }

        /// <summary>
        /// Validate that all drawing files have parameters in CSV
        /// Returns list of drawing files that don't have parameters
        /// </summary>
        public List<string> ValidateDrawingsHaveParameters(string inputFolder)
        {
            var missingParameterFiles = new List<string>();
            
            if (!_useCsvMapping || _csvMapper == null)
            {
                return missingParameterFiles; // No CSV mapping, so no validation needed
            }

            var dwgFiles = Directory.GetFiles(inputFolder, "*.dwg", SearchOption.TopDirectoryOnly);
            
            foreach (var dwgFile in dwgFiles)
            {
                if (!_csvMapper.HasDrawing(dwgFile))
                {
                    missingParameterFiles.Add(Path.GetFileName(dwgFile));
                }
            }
            
            return missingParameterFiles;
        }

        /// <summary>
        /// Process all DWG files in a folder
        /// </summary>
        /// <returns>List of failed drawing file names</returns>
        public async Task<ProcessingSummary> ProcessFolderAsync(
            string inputFolder,
            string outputFolder,
            string inputJsonPath,
            CancellationToken cancellationToken = default,
            IProgress<(int completed, int total)>? progress = null)
        {
            if (!Directory.Exists(inputFolder))
            {
                Console.WriteLine($"ERROR: Input folder not found: {inputFolder}");
                return new ProcessingSummary { FailedFiles = new List<string>(), NonProcessedFiles = new List<string>() };
            }

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
                Console.WriteLine($"Created output folder: {outputFolder}");
            }

            // Create timestamped subfolder for this batch run
            var batchStartTime = DateTime.Now;
            string timestamp = batchStartTime.ToString("yyyyMMdd_HHmmss_fff");
            string timestampedOutputFolder = Path.Combine(outputFolder, timestamp);
            Directory.CreateDirectory(timestampedOutputFolder);
            Console.WriteLine($"Created timestamped output folder: {timestampedOutputFolder}");

            // Validate DLLs exist
            ValidateDlls();

            // Get all DWG files
            var dwgFiles = Directory.GetFiles(inputFolder, "*.dwg", SearchOption.TopDirectoryOnly);

            if (dwgFiles.Length == 0)
            {
                Console.WriteLine($"No DWG files found in {inputFolder}");
                return new ProcessingSummary { FailedFiles = new List<string>(), NonProcessedFiles = new List<string>() };
            }

            // Report initial progress
            progress?.Report((0, dwgFiles.Length));

            Console.WriteLine($"\n╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║  AutoCAD Batch Processor (Standalone Mode)                  ║");
            Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"\n📁 Input Folder:  {inputFolder}");
            Console.WriteLine($"📁 Output Folder: {outputFolder}");
            Console.WriteLine($"📁 Batch Folder:  {timestampedOutputFolder}");
            Console.WriteLine($"📋 Input JSON:    {inputJsonPath}");
            Console.WriteLine($"📄 Total Files:   {dwgFiles.Length}");
            Console.WriteLine($"⚙️  Max Parallel:  {_maxParallelism}");
            Console.WriteLine($"🔧 AutoCAD:       {_accoreconsoleExePath}");
            Console.WriteLine($"📦 DLLs to Load:  {_dllsToLoad.Count}");
            
            if (_enableVerboseLogging)
            {
                foreach (var dll in _dllsToLoad)
                {
                    Console.WriteLine($"   - {Path.GetFileName(dll)}");
                }
            }
            
            Console.WriteLine($"🎯 Command:       {_mainCommand}");
            Console.WriteLine($"\n{new string('─', 64)}\n");

            var startTime = DateTime.Now;
            var results = new List<ProcessingResult>();
            int completedCount = 0;

            // Process in parallel
            var options = new ParallelOptions 
            { 
                MaxDegreeOfParallelism = _maxParallelism,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(dwgFiles, options, async (dwgFile, ct) =>
            {
                // Check for cancellation before processing
                cancellationToken.ThrowIfCancellationRequested();

                ProcessingResult result = null;
                string drawingName = Path.GetFileNameWithoutExtension(dwgFile);
                
                try
                {
                    // Log start of processing for this file
                    Console.WriteLine($"\n🔄 Starting processing: {drawingName} ({completedCount + 1}/{dwgFiles.Length})");
                    
                    result = await ProcessSingleDrawingAsync(
                        dwgFile,
                        inputJsonPath,
                        timestampedOutputFolder);

                    // Check for cancellation after processing
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // Log completion
                    Console.WriteLine($"✅ Completed processing: {drawingName} (Success: {result.Success})");
                }
                catch (OperationCanceledException)
                {
                    // Re-throw cancellation to stop processing
                    Console.WriteLine($"\n⚠️ Processing cancelled for: {drawingName}");
                    throw;
                }
                catch (Exception ex)
                {
                    // Catch any unhandled exceptions to prevent stopping the entire batch
                    // Create a failed result for this file
                    Console.WriteLine($"\n❌ UNHANDLED EXCEPTION processing {drawingName}: {ex.Message}");
                    Console.WriteLine($"  📋 REASON: Exception occurred before AutoCAD processing could start");
                    Console.WriteLine($"  📋 REASON: Exception type: {ex.GetType().Name}");
                    if (_enableVerboseLogging)
                    {
                        Console.WriteLine($"  📋 REASON: Stack trace: {ex.StackTrace}");
                    }
                    Console.WriteLine($"  ❌ RESULT: File marked as NON-PROCESSED (exception prevented processing)");
                    
                    result = new ProcessingResult
                    {
                        DrawingPath = dwgFile,
                        DrawingName = drawingName,
                        StartTime = DateTime.Now,
                        EndTime = DateTime.Now,
                        Success = false,
                        WasProcessed = false, // Not processed due to exception
                        ErrorMessage = $"Unhandled exception: {ex.Message}",
                        ExitCode = -1
                    };
                }
                finally
                {
                    // Always add result and report progress, even if there was an error
                    if (result != null)
                    {
                        lock (results)
                        {
                            results.Add(result);
                            completedCount++;
                            // Report progress
                            progress?.Report((completedCount, dwgFiles.Length));
                            Console.WriteLine($"📊 Progress: {completedCount}/{dwgFiles.Length} files completed");
                        }
                    }
                }
            });

            var endTime = DateTime.Now;
            var duration = endTime - startTime;

            // Separate failed files (processed but validation failed - has JSON) 
            // from non-processed files (error before processing - no JSON)
            var failedFiles = results
                .Where(r => !r.Success && r.WasProcessed)
                .Select(r => r.DrawingPath) // Store full path instead of just name
                .ToList();
            
            var nonProcessedFiles = results
                .Where(r => !r.Success && !r.WasProcessed)
                .Select(r => new { r.DrawingName, r.ErrorMessage })
                .ToList();

            // Print summary
            PrintSummary(results, duration, timestampedOutputFolder);

            // Check if UIPlugin.dll failed to load in any drawing
            bool uiPluginFailed = results.Any(r => r.UIPluginLoadFailed);

            return new ProcessingSummary 
            { 
                FailedFiles = failedFiles,
                NonProcessedFiles = nonProcessedFiles.Select(x => x.DrawingName).ToList(),
                NonProcessedFilesWithErrors = nonProcessedFiles.ToDictionary(x => x.DrawingName, x => x.ErrorMessage ?? "Unknown error"),
                UIPluginLoadFailed = uiPluginFailed
            };
        }

        /// <summary>
        /// Validate that all DLLs exist
        /// </summary>
        private void ValidateDlls()
        {
            if (_dllsToLoad.Count == 0)
            {
                Console.WriteLine("⚠️  Warning: No DLLs configured to load!");
                return;
            }

            Console.WriteLine($"\n📦 Verifying {_dllsToLoad.Count} DLL(s):");
            bool allValid = true;
            
            for (int i = 0; i < _dllsToLoad.Count; i++)
            {
                string dllPath = _dllsToLoad[i];
                string dllName = Path.GetFileName(dllPath);
                
                if (!File.Exists(dllPath))
                {
                    Console.WriteLine($"    ❌ {i + 1}. {dllName} - FILE NOT FOUND: {dllPath}");
                    allValid = false;
                }
                else
                {
                    Console.WriteLine($"    ✅ {i + 1}. {dllName}");
                }
            }
            
            Console.WriteLine(); // Empty line after DLL list

            if (!allValid)
            {
                throw new FileNotFoundException("One or more DLLs not found. Please check appsettings.json");
            }
        }

        /// <summary>
        /// Process a single drawing file
        /// </summary>
        private async Task<ProcessingResult> ProcessSingleDrawingAsync(
            string dwgPath,
            string inputJsonPath,
            string outputFolder)
        {
            var result = new ProcessingResult
            {
                DrawingPath = dwgPath,
                DrawingName = Path.GetFileNameWithoutExtension(dwgPath),
                StartTime = DateTime.Now
            };

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            bool processStarted = false; // Track if process was successfully started (declared at method scope)

            try
            {
                // Add clear separator before each drawing's logs
                Console.WriteLine($"\n");
                Console.WriteLine($"{new string('═', 80)}");
                Console.WriteLine($"  📄 Processing: {result.DrawingName}");
                Console.WriteLine($"{new string('═', 80)}");
                Console.WriteLine();

                // Generate drawing-specific config if CSV mapping is enabled
                string drawingConfigPath = inputJsonPath;
                string drawingConfigJson = null; // JSON content (if we want to pass directly)
                
                if (_useCsvMapping && _csvMapper != null)
                {
                    // Use template config if provided, otherwise null for CSV-only mode
                    string templatePath = !string.IsNullOrWhiteSpace(inputJsonPath) && File.Exists(inputJsonPath) ? inputJsonPath : null;
                    
                    var csvConfig = _csvMapper.GenerateConfigJson(dwgPath, templatePath);
                        if (csvConfig != null)
                    {
                        // Store JSON content directly (no temp file needed)
                        drawingConfigJson = csvConfig;
                        drawingConfigPath = ""; // No file path - we'll pass JSON directly
                        
                        if (templatePath == null)
                        {
                            Console.WriteLine($"  📋 Generated config from CSV (no template)");
                        }
                        else
                        {
                            Console.WriteLine($"  📋 Generated config from CSV + template");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  ⚠️  No CSV parameters found");
                        if (string.IsNullOrWhiteSpace(drawingConfigPath) || !File.Exists(drawingConfigPath))
                        {
                            Console.WriteLine($"  ❌ ERROR: No config available for this drawing");
                            Console.WriteLine($"  📋 REASON: Drawing file '{result.DrawingName}' is not found in CSV file");
                            Console.WriteLine($"  📋 REASON: No base config file provided as fallback");
                            Console.WriteLine($"  ❌ RESULT: File marked as NON-PROCESSED (AutoCAD never started)");
                            result.Success = false;
                            result.WasProcessed = false; // Not processed - error before AutoCAD
                            result.ErrorMessage = "No configuration available (not in CSV and no base config)";
                            return result;
                        }
                    }
                }
                else
                {
                    // Not using CSV - read JSON from file if provided
                    if (!string.IsNullOrWhiteSpace(drawingConfigPath) && File.Exists(drawingConfigPath))
                    {
                        drawingConfigJson = File.ReadAllText(drawingConfigPath);
                    }
                }
                
                // Validate that we have config content before proceeding
                if (string.IsNullOrWhiteSpace(drawingConfigJson))
                {
                    Console.WriteLine($"  ❌ ERROR: No config content available");
                    Console.WriteLine($"  📋 REASON: Configuration JSON is empty or null");
                    Console.WriteLine($"  📋 REASON: Cannot proceed without valid configuration parameters");
                    Console.WriteLine($"  ❌ RESULT: File marked as NON-PROCESSED (AutoCAD never started)");
                    result.Success = false;
                    result.WasProcessed = false; // Not processed - error before AutoCAD
                    result.ErrorMessage = "No configuration content available";
                    return result;
                }

                // Create temporary script file for this drawing
                // Pass drawingConfigPath (may be empty if using JSON content directly)
                string tempScriptPath = CreateTemporaryScript(dwgPath, timestamp, drawingConfigPath ?? "", outputFolder, result.DrawingName);

                if (_enableVerboseLogging)
                {
                    Console.WriteLine($"  Script: {tempScriptPath}");
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _accoreconsoleExePath,
                        Arguments = $"/i \"{dwgPath}\" /s \"{tempScriptPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                // Define the expected output filename (moved earlier for JSON monitoring)
                string outputFileName = $"{result.DrawingName}.json";
                string expectedJsonPath = Path.Combine(outputFolder, outputFileName);
                
                // Set environment variables for this specific process
                // Pass JSON content directly via environment variable (no temp file needed!)
                process.StartInfo.EnvironmentVariables["INPUT_JSON_PATH"] = ""; // Empty = use content
                process.StartInfo.EnvironmentVariables["INPUT_JSON_CONTENT"] = drawingConfigJson;
                process.StartInfo.EnvironmentVariables["OUTPUT_FOLDER"] = outputFolder;
                process.StartInfo.EnvironmentVariables["OUTPUT_FILENAME"] = outputFileName;
                process.StartInfo.EnvironmentVariables["TIMESTAMP"] = timestamp;
                process.StartInfo.EnvironmentVariables["DRAWING_NAME"] = result.DrawingName;

                // Disable verbose AutoCAD logs - suppress environment variable details
                // (user requested to disable AutoCAD logs)
                if (_enableVerboseLogging)
                {
                    Console.WriteLine($"  ✅ Passing config JSON directly (no temp file)");
                    Console.WriteLine($"  📦 Environment Variables:");
                    Console.WriteLine($"     OUTPUT_FOLDER = {outputFolder}");
                    Console.WriteLine($"     OUTPUT_FILENAME = {outputFileName}");
                    Console.WriteLine($"     INPUT_JSON_CONTENT = {drawingConfigJson.Length} chars");
                    Console.WriteLine($"     DRAWING_NAME = {result.DrawingName}");
                    Console.WriteLine();
                }

                // Capture output
                var outputBuilder = new StringBuilder();
                var dllLoadErrors = new List<string>();
                bool uiPluginLoadFailed = false;
                bool commandNotFound = false;
                string commandNotFoundMessage = null;
                
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        
                        string data = e.Data.Trim();
                        
                        // Check for command not found errors
                        if (data.Contains("Unknown command", StringComparison.OrdinalIgnoreCase) ||
                            data.Contains("Command not found", StringComparison.OrdinalIgnoreCase) ||
                            (data.Contains("not recognized", StringComparison.OrdinalIgnoreCase) && 
                             (data.Contains("command", StringComparison.OrdinalIgnoreCase) || 
                              data.Contains(_mainCommand, StringComparison.OrdinalIgnoreCase))) ||
                            (data.Contains("not found", StringComparison.OrdinalIgnoreCase) && 
                             data.Contains(_mainCommand, StringComparison.OrdinalIgnoreCase)))
                        {
                            commandNotFound = true;
                            commandNotFoundMessage = data;
                            Console.WriteLine($"  ❌ Command Not Found: {data}");
                        }
                        
                        // Disable AutoCAD logs - suppress DLL loading messages
                        // (user requested to disable AutoCAD logs)
                        
                        // Check for NETLOAD errors
                        if (data.Contains("NETLOAD", StringComparison.OrdinalIgnoreCase) || 
                            data.Contains("netload", StringComparison.OrdinalIgnoreCase) ||
                            data.Contains("Assembly", StringComparison.OrdinalIgnoreCase))
                        {
                            // Check for error indicators
                            if (data.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                data.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                                data.Contains("cannot", StringComparison.OrdinalIgnoreCase) ||
                                data.Contains("unable", StringComparison.OrdinalIgnoreCase) ||
                                data.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                                data.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
                                data.Contains("could not", StringComparison.OrdinalIgnoreCase))
                            {
                                dllLoadErrors.Add(data);
                                Console.WriteLine($"  ❌ DLL Load Error: {data}");
                                
                                // Check if this is UIPlugin.dll error
                                if (data.Contains("UIPlugin", StringComparison.OrdinalIgnoreCase))
                                {
                                    uiPluginLoadFailed = true;
                                }
                            }
                        }
                        
                        // Filter out verbose AutoCAD noise
                        bool isVerboseNoise = data == "Command:" || 
                                             data == "Regenerating model." ||
                                             data.StartsWith("Substituting [") ||
                                             data == "Loading Modeler DLLs." ||
                                             data == "AutoCAD menu utilities loaded." ||
                                             data.Contains("System Variable Changed") ||
                                             data.Contains("monitored system variables") ||
                                             data == "CoreHeartBeat" ||
                                             data.StartsWith("AcCoreConsole:") ||
                                             data.StartsWith("AutoCAD Core Engine Console") ||
                                             data.StartsWith("Version Number:") ||
                                             data.StartsWith("LogFilePath has been set") ||
                                             data.StartsWith("Execution Path:") ||
                                             data.StartsWith("Current Directory:") ||
                                             data.StartsWith("Redirect stdout") ||
                                             string.IsNullOrWhiteSpace(data);
                        
                        // Disable AutoCAD logs - only show critical errors
                        // Skip all AutoCAD output (user requested to disable AutoCAD logs)
                        // Only errors are shown above (DLL load errors, command not found, etc.)
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine($"ERROR: {e.Data}");
                        // Always show errors (separator already shows which drawing)
                        Console.WriteLine($"  ❌ ERROR: {e.Data}");
                    }
                };

                process.Start();
                processStarted = true; // Mark that process was successfully started
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                // Disable AutoCAD logs - suppress process start message
                // (user requested to disable AutoCAD logs)

                // Add timeout to prevent hanging (6 minutes max per drawing)
                // IMPORTANT: Without proper timeout and cleanup, processes can hang indefinitely,
                // blocking parallel processing slots and preventing next files from being processed
                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(6)))
                {
                    try
                    {
                        await process.WaitForExitAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout - kill the process
                        try
                        {
                            if (!process.HasExited)
                            {
                                Console.WriteLine($"  ⚠️  WARNING: Process timed out after 6 minutes - killing process tree...");
                                try
                                {
                                    process.Kill(entireProcessTree: true); // Kill entire process tree including child processes
                                }
                                catch
                                {
                                    // Fallback to regular kill if tree kill fails
                                    process.Kill();
                                }
                                // Wait for process to actually exit (with timeout)
                                if (!process.WaitForExit(10000))
                                {
                                    Console.WriteLine($"  ⚠️  WARNING: Process did not exit after kill - forcing termination");
                                }
                                Console.WriteLine($"  ⏱️  REASON: AutoCAD process exceeded 6-minute timeout limit");
                                Console.WriteLine($"  ⏱️  REASON: Process may be hung or taking too long to initialize");
                                Console.WriteLine($"  ❌ RESULT: File marked as NON-PROCESSED (process killed before completion)");
                                result.ErrorMessage = "Process timed out after 6 minutes";
                                result.WasProcessed = false; // Mark as not processed due to timeout
                            }
                        }
                        catch (Exception killEx)
                        {
                            Console.WriteLine($"  ⚠️  WARNING: Failed to kill timed-out process: {killEx.Message}");
                        }
                    }
                }
                
                // CRITICAL FIX: Ensure process has actually exited before proceeding
                // Problem: WaitForExitAsync() can complete even if process is still running (race conditions)
                // Impact: If we proceed without verifying exit, the process continues running and blocks next files
                // Solution: Explicitly check HasExited and force kill if still running
                if (!process.HasExited)
                {
                    try
                    {
                        Console.WriteLine($"  ⚠️  WARNING: Process still running after WaitForExitAsync - killing process tree...");
                        try
                        {
                            // CRITICAL: Kill entire process tree (parent + all child processes)
                            // Problem: AutoCAD spawns child processes that weren't being killed
                            // Impact: Child processes would continue running, consuming resources and blocking file handles
                            process.Kill(entireProcessTree: true); // Kill entire process tree including child processes
                        }
                        catch
                        {
                            // Fallback to regular kill if tree kill fails
                            process.Kill();
                        }
                        if (!process.WaitForExit(10000))
                        {
                            Console.WriteLine($"  ⚠️  WARNING: Process did not exit after kill - may be stuck");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ⚠️  WARNING: Error killing process: {ex.Message}");
                    }
                }

                // CRITICAL FIX: Properly close output streams to prevent process from staying alive
                // Problem: BeginOutputReadLine() and BeginErrorReadLine() keep async handlers active
                // Impact: If streams aren't cancelled, they can keep process references alive, preventing cleanup
                // Solution: Explicitly cancel async reads and wait for output to be captured
                if (process.HasExited)
                {
                    try
                    {
                        // Give a small delay to ensure all async output is captured
                        // The output handlers will continue to receive data even after process exits
                        await Task.Delay(500); // Reduced from 1000ms to 500ms
                    }
                    catch { /* Ignore delay errors */ }
                    
                    // Cancel async reads if still active (process has exited, so no more data)
                    // This releases references to the process, allowing proper cleanup
                    try
                    {
                        process.CancelOutputRead();
                    }
                    catch { /* Ignore if already closed or not started */ }
                    
                    try
                    {
                        process.CancelErrorRead();
                    }
                    catch { /* Ignore if already closed or not started */ }
                }
                else
                {
                    // Process didn't exit - cancel reads before killing
                    // This prevents race conditions where output handlers interfere with process termination
                    try
                    {
                        process.CancelOutputRead();
                    }
                    catch { /* Ignore if already closed */ }
                    
                    try
                    {
                        process.CancelErrorRead();
                    }
                    catch { /* Ignore if already closed */ }
                }

                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                
                // Get exit code and output before disposing
                // Handle case where process object may be disposed/invalid
                int exitCode = -1;
                bool processExited = false;
                try
                {
                    processExited = process.HasExited;
                    if (processExited)
                    {
                        exitCode = process.ExitCode;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    // "No process is associated with this object" - process was disposed
                    // But process WAS started, so mark as processed
                    Console.WriteLine($"  ⚠️  WARNING: Cannot access process properties: {ex.Message}");
                    Console.WriteLine($"  📋 REASON: Process object is no longer associated (may have been disposed)");
                    Console.WriteLine($"  📋 REASON: Process was started but status cannot be determined");
                    processExited = true; // Assume exited since we can't check
                    exitCode = -1; // Unknown exit code
                }
                
                result.ExitCode = exitCode;
                result.Output = outputBuilder.ToString();
                
                // Log process completion status for monitoring
                if (processExited)
                {
                    Console.WriteLine($"  ✓ Process exited (Exit Code: {exitCode}, Duration: {result.Duration.TotalSeconds:F1}s)");
                }
                else
                {
                    Console.WriteLine($"  ⚠️  WARNING: Process did not exit properly - was force killed");
                }
                
                // CRITICAL FIX: Aggressive process cleanup to ensure resources are fully released
                // Problem: Processes weren't being fully terminated, leaving handles open
                // Impact: Open handles prevent new processes from starting, blocking parallel processing
                // Solution: Multiple verification points, force kill, explicit disposal, and resource release delay
                try
                {
                    if (process != null)
                    {
                        // Force kill if still running (including child processes)
                        // This is a final safety check - process should already be killed above
                        if (!process.HasExited)
                        {
                            try
                            {
                                try
                                {
                                    // Kill entire process tree to ensure no child processes remain
                                    process.Kill(entireProcessTree: true); // Kill entire process tree including child processes
                                }
                                catch
                                {
                                    // Fallback to regular kill if tree kill fails (older .NET versions)
                                    process.Kill();
                                }
                                process.WaitForExit(5000);
                            }
                            catch { /* Ignore kill errors */ }
                        }
                        
                        // Dispose process and all its resources (closes handles, releases memory)
                        process.Dispose();
                        
                        // Give a small delay to ensure process resources are fully released by OS
                        // This prevents race conditions where next file tries to start before resources are free
                        await Task.Delay(100);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️  WARNING: Error disposing process: {ex.Message}");
                }
                
                // Check for UIPlugin.dll loading failure
                // Only mark as failed if we actually detected an error in the output
                // (We can't verify success without LISP messages, but AutoCAD will show errors if NETLOAD fails)
                result.UIPluginLoadFailed = uiPluginLoadFailed;
                
                // Check for command not found - mark as not processed
                if (commandNotFound)
                {
                    Console.WriteLine($"  ❌ Command '{_mainCommand}' not found - marking file as not processed");
                    Console.WriteLine($"  📋 REASON: AutoCAD command '{_mainCommand}' is not recognized");
                    Console.WriteLine($"  📋 REASON: Command may not be loaded or registered in AutoCAD");
                    Console.WriteLine($"  📋 REASON: Check if DLLs are loaded correctly (CommonUtils.dll, etc.)");
                    Console.WriteLine($"  📋 REASON: Check if command name is correct in appsettings.json");
                    Console.WriteLine($"  ❌ RESULT: File marked as NON-PROCESSED (command execution never started)");
                    result.WasProcessed = false; // Not processed - command doesn't exist
                    result.Success = false;
                    result.ErrorMessage = $"Command not found: {commandNotFoundMessage ?? _mainCommand}";
                    result.EndTime = DateTime.Now;
                    result.Duration = result.EndTime - result.StartTime;
                    Console.WriteLine();
                    Console.WriteLine($"{new string('─', 80)}");
                    Console.WriteLine($"  ❌ COMMAND NOT FOUND: {result.DrawingName}");
                    Console.WriteLine($"     Error: {result.ErrorMessage}");
                    Console.WriteLine($"{new string('═', 80)}");
                    Console.WriteLine();
                    return result; // Return early - don't check for output files
                }
                
                // Check for DLL loading errors
                if (dllLoadErrors.Count > 0)
                {
                    Console.WriteLine($"  ⚠️  WARNING: {dllLoadErrors.Count} DLL loading error(s) detected!");
                    result.ErrorMessage = $"DLL loading errors: {string.Join("; ", dllLoadErrors)}";
                }
                
                // Mark as processed (AutoCAD process was started)
                result.WasProcessed = true;
                
                // Determine success based on command type
                bool isReportGenerationCommand = (_mainCommand.Contains("GenerateScrutinyReportBatch", StringComparison.OrdinalIgnoreCase) ||
                                                  (_mainCommand.Contains("Generate", StringComparison.OrdinalIgnoreCase) && 
                                                   _mainCommand.Contains("Report", StringComparison.OrdinalIgnoreCase)));
                
                if (isReportGenerationCommand)
                {
                    // For report generation commands: File MUST exist for success
                    // Check for any output file starting with drawing name (could be PDF, DOCX, etc.)
                    string[] possibleExtensions = { ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".json", ".txt" };
                    bool outputFileExists = false;
                    string foundOutputFile = null;
                    
                    foreach (string ext in possibleExtensions)
                    {
                        string expectedFilePath = Path.Combine(outputFolder, $"{result.DrawingName}{ext}");
                        if (File.Exists(expectedFilePath))
                        {
                            outputFileExists = true;
                            foundOutputFile = expectedFilePath;
                            break;
                        }
                    }
                    
                    // Also check for any file starting with drawing name (in case of different naming)
                    if (!outputFileExists)
                    {
                        try
                        {
                            var files = Directory.GetFiles(outputFolder, $"{result.DrawingName}*");
                            if (files.Length > 0)
                            {
                                outputFileExists = true;
                                foundOutputFile = files[0];
                            }
                        }
                        catch { /* Ignore directory access errors */ }
                    }
                    
                    result.Success = outputFileExists;
                    
                    if (outputFileExists && foundOutputFile != null)
                    {
                        Console.WriteLine($"  ✅ Output file created: {Path.GetFileName(foundOutputFile)}");
                    }
                    else
                    {
                        Console.WriteLine($"  ❌ No output file created for {result.DrawingName}");
                        result.ErrorMessage = "Output file was not created - report generation may have failed";
                    }
                }
                else
                {
                    // For validation commands: JSON file exists = validation failed
                    // Success = no JSON file (all validations passed)
                    // expectedJsonPath is already defined above
                    bool jsonCreated = File.Exists(expectedJsonPath);
                    
                    // Success logic: No JSON file = all validations passed (success)
                    // Failure logic: JSON file exists = validations failed
                    result.Success = !jsonCreated;
                }

                // Clean up temp script and parameter files
                try { File.Delete(tempScriptPath); } catch { }
                try 
                { 
                    string paramFile = Path.Combine(_tempScriptFolder, $"params_{timestamp}_{result.DrawingName}.json");
                    File.Delete(paramFile); 
                } 
                catch { }
                
                // Clean up CSV-generated config file if it was created
                if (_useCsvMapping && drawingConfigPath != inputJsonPath)
                {
                    try { File.Delete(drawingConfigPath); } catch { }
                }

                // Add completion status with clear formatting
                Console.WriteLine();
                Console.WriteLine($"{new string('─', 80)}");
                if (result.Success)
                {
                    if (isReportGenerationCommand)
                    {
                        // Success: Output file created (report generated)
                        Console.WriteLine($"  ✅ PROCESSING COMPLETE: {result.DrawingName}");
                        Console.WriteLine($"     Duration: {result.Duration.TotalSeconds:F1}s | Report file created successfully");
                    }
                    else
                    {
                        // Success: No JSON file created (all validations passed)
                        Console.WriteLine($"  ✅ SUCCESS: {result.DrawingName}");
                        Console.WriteLine($"     Duration: {result.Duration.TotalSeconds:F1}s | All validations passed");
                    }
                }
                else
                {
                    if (isReportGenerationCommand)
                    {
                        // Failed: Output file not created (report generation failed)
                        Console.WriteLine($"  ❌ FAILED: {result.DrawingName}");
                        Console.WriteLine($"     Duration: {result.Duration.TotalSeconds:F1}s | Output file was not created");
                        
                        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                        {
                            Console.WriteLine($"     Error: {result.ErrorMessage}");
                        }
                    }
                    else
                    {
                        // Failed: JSON file exists (validations failed)
                        Console.WriteLine($"  ❌ FAILED: {result.DrawingName}");
                        Console.WriteLine($"     Duration: {result.Duration.TotalSeconds:F1}s | Failures detected (JSON created)");
                    }
                    
                    // Log exit code for debugging if non-zero
                    try
                    {
                        if (process.HasExited && process.ExitCode != 0)
                        {
                            Console.WriteLine($"     Exit Code: {process.ExitCode}");
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Process object no longer valid - ignore
                    }
                }
                Console.WriteLine($"{new string('═', 80)}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                // Check if process was started - if so, mark as processed (even if it failed)
                // Use the processStarted flag to determine if process was actually started
                bool wasProcessStarted = processStarted;
                
                result.Success = false;
                result.WasProcessed = wasProcessStarted; // Mark as processed if process was started
                result.ErrorMessage = ex.Message;
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                
                Console.WriteLine();
                Console.WriteLine($"{new string('─', 80)}");
                Console.WriteLine($"  ❌ EXCEPTION: {result.DrawingName}");
                Console.WriteLine($"     Error: {ex.Message}");
                if (wasProcessStarted)
                {
                    Console.WriteLine($"     📋 REASON: Process was started but exception occurred during/after execution");
                    Console.WriteLine($"     📋 RESULT: File marked as FAILED (processed but failed)");
                }
                else
                {
                    Console.WriteLine($"     📋 REASON: Exception occurred before process could start");
                    Console.WriteLine($"     📋 RESULT: File marked as NON-PROCESSED (AutoCAD never started)");
                }
                Console.WriteLine($"{new string('═', 80)}");
                Console.WriteLine();
            }

            return result;
        }

        /// <summary>
        /// Create temporary script file with multiple NETLOAD commands and parameter files
        /// </summary>
        private string CreateTemporaryScript(string dwgPath, string timestamp, string inputJsonPath, string outputFolder, string drawingName)
        {
            string tempScriptPath = Path.Combine(
                _tempScriptFolder,
                $"autocad_script_{timestamp}_{Path.GetFileNameWithoutExtension(dwgPath)}.scr"
            );

            // Create a parameter file for this drawing
            string paramFilePath = Path.Combine(
                _tempScriptFolder,
                $"params_{timestamp}_{drawingName}.json"
            );

            // Define the expected output filename (without timestamp for batch consistency)
            string outputFileName = $"{drawingName}.json";
            
            var parameters = new
            {
                InputJsonPath = inputJsonPath,
                OutputFolder = outputFolder,
                DrawingName = drawingName,
                OutputFileName = outputFileName,
                OutputFilePath = Path.Combine(outputFolder, outputFileName),
                Timestamp = timestamp
            };

            string paramsJson = System.Text.Json.JsonSerializer.Serialize(parameters, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(paramFilePath, paramsJson);

            var scriptBuilder = new StringBuilder();

            // Load all DLLs in order (verification already done at start)
            foreach (var dllPath in _dllsToLoad)
            {
                string dllName = Path.GetFileName(dllPath);
                // Escape backslashes for LISP string (double backslashes)
                string escapedDllPath = dllPath.Replace("\\", "\\\\");
                // Print DLL name and full path for verification
                scriptBuilder.AppendLine($"(princ (strcat \"\\n[Loading] \" \"{dllName}\" \"\\n\"))");
                scriptBuilder.AppendLine($"(princ (strcat \"[Loading] Path: \" \"{escapedDllPath}\" \"\\n\"))");
                // Use escaped path in NETLOAD command
                scriptBuilder.AppendLine($"NETLOAD \"{escapedDllPath}\"");
            }
            
            // Small delay to ensure all DLLs are fully loaded and commands are registered
            scriptBuilder.AppendLine("(princ \"\\n[Loading] Waiting for DLLs to fully initialize...\\n\")");
            scriptBuilder.AppendLine("(command \"_.DELAY\" \"500\" \"\")"); // 500ms delay with empty string to avoid waiting for input

            // Set LISP variables that the command can read (traditional AutoCAD method)
            scriptBuilder.AppendLine($"(setq BATCH_INPUT_JSON \"{inputJsonPath.Replace("\\", "\\\\")}\")");
            scriptBuilder.AppendLine($"(setq BATCH_OUTPUT_FOLDER \"{outputFolder.Replace("\\", "\\\\")}\")");
            scriptBuilder.AppendLine($"(setq BATCH_DRAWING_NAME \"{drawingName}\")");
            scriptBuilder.AppendLine($"(setq BATCH_OUTPUT_FILENAME \"{outputFileName}\")");
            scriptBuilder.AppendLine($"(setq BATCH_PARAM_FILE \"{paramFilePath.Replace("\\", "\\\\")}\")");

            // Execute the selected command
            scriptBuilder.AppendLine(_mainCommand);

            // CRITICAL: Add explicit delay and force QUIT to ensure process exits
            // Even if command hangs, this ensures script continues
            scriptBuilder.AppendLine("(princ \"\\n[Command execution completed - preparing to exit...]\\n\")");
            scriptBuilder.AppendLine("(command \"_.DELAY\" \"100\" \"\")"); // Small delay to ensure command fully completes
            
            // Force quit - try both QUIT and _EXIT (some accoreconsole versions prefer _EXIT)
            scriptBuilder.AppendLine("(princ \"\\n[Exiting AutoCAD...]\\n\")");
            scriptBuilder.AppendLine("_EXIT"); // Use _EXIT instead of QUIT (more reliable in accoreconsole)
            scriptBuilder.AppendLine("QUIT"); // Fallback to QUIT if _EXIT doesn't work

            string scriptContent = scriptBuilder.ToString();
            
            // Only log parameter file in verbose mode
            if (_enableVerboseLogging)
            {
                Console.WriteLine($"  📝 Parameter file: {paramFilePath}");
                Console.WriteLine($"  📝 Generated Script: {tempScriptPath}");
                Console.WriteLine($"  📋 Parameter JSON:");
                // Format JSON with indentation for readability
                var lines = paramsJson.Split('\n');
                foreach (var line in lines)
                {
                    Console.WriteLine($"     {line}");
                }
                Console.WriteLine();
            }

            File.WriteAllText(tempScriptPath, scriptContent);
            return tempScriptPath;
        }

        private void PrintSummary(List<ProcessingResult> results, TimeSpan duration, string outputFolder)
        {
            Console.WriteLine($"\n{new string('═', 64)}");
            Console.WriteLine($"  PROCESSING SUMMARY");
            Console.WriteLine($"{new string('═', 64)}");

            int successful = results.Count(r => r.Success);
            int failedValidation = results.Count(r => !r.Success && r.WasProcessed);
            int nonProcessed = results.Count(r => !r.Success && !r.WasProcessed);
            int totalFailed = failedValidation + nonProcessed;

            Console.WriteLine($"\n  ✅ Successful:        {successful}");
            Console.WriteLine($"  ❌ Validation Failed: {failedValidation} (JSON created)");
            Console.WriteLine($"  ⚠️  Non-Processed:     {nonProcessed} (errors before processing)");
            Console.WriteLine($"  📊 Total:             {results.Count}");
            Console.WriteLine($"  ⏱️  Duration:          {duration.TotalMinutes:F2} minutes");
            Console.WriteLine($"  📁 Output:            {outputFolder}");
            
            if (results.Count > 0)
            {
                Console.WriteLine($"  ⚡ Avg Speed:         {duration.TotalSeconds / results.Count:F2} sec/file");
            }
            
            // Show per-file processing times
            Console.WriteLine($"\n  📋 Per-File Processing Times:");
            foreach (var result in results.OrderBy(r => r.StartTime))
            {
                string statusIcon = result.Success ? "✅" : "❌";
                string timeDisplay = result.Duration.TotalSeconds < 60 
                    ? $"{result.Duration.TotalSeconds:F1}s" 
                    : $"{result.Duration.TotalMinutes:F2}m";
                Console.WriteLine($"     {statusIcon} {result.DrawingName}: {timeDisplay}");
            }

            if (failedValidation > 0)
            {
                Console.WriteLine($"\n  Validation Failed Files (JSON created):");
                foreach (var result in results.Where(r => !r.Success && r.WasProcessed))
                {
                    Console.WriteLine($"    - {result.DrawingName}");
                }
            }

            if (nonProcessed > 0)
            {
                Console.WriteLine($"\n  ⚠️  NON-PROCESSED FILES (errors before AutoCAD processing started):");
                Console.WriteLine($"  {new string('─', 64)}");
                foreach (var result in results.Where(r => !r.Success && !r.WasProcessed))
                {
                    Console.WriteLine($"\n    📄 File: {result.DrawingName}");
                    if (!string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        Console.WriteLine($"    ❌ Error: {result.ErrorMessage}");
                    }
                    Console.WriteLine($"    ⏱️  Duration: {result.Duration.TotalSeconds:F1}s (before failure)");
                    Console.WriteLine($"    📋 Status: NON-PROCESSED (AutoCAD never executed command)");
                }
                Console.WriteLine($"\n  {new string('─', 64)}");
            }

            Console.WriteLine($"\n{new string('═', 64)}\n");
        }
    }

    public class ProcessingResult
    {
        public string DrawingPath { get; set; }
        public string DrawingName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string ErrorMessage { get; set; }
        public bool WasProcessed { get; set; } // True if AutoCAD process was started, false if error before processing
        public bool UIPluginLoadFailed { get; set; } // True if UIPlugin.dll failed to load
    }

    public class ProcessingSummary
    {
        public List<string> FailedFiles { get; set; } = new List<string>(); // Processed but validation failed (has JSON) - stores full file paths
        public List<string> NonProcessedFiles { get; set; } = new List<string>(); // Not processed (errors before AutoCAD)
        public Dictionary<string, string> NonProcessedFilesWithErrors { get; set; } = new Dictionary<string, string>(); // File name -> error message
        public bool UIPluginLoadFailed { get; set; } // True if UIPlugin.dll failed to load in any drawing
    }
}

