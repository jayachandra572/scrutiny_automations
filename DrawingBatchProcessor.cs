using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchProcessor
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
        /// Process all DWG files in a folder
        /// </summary>
        /// <returns>List of failed drawing file names</returns>
        public async Task<ProcessingSummary> ProcessFolderAsync(
            string inputFolder,
            string outputFolder,
            string inputJsonPath)
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

            // Process in parallel
            var options = new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism };

            await Parallel.ForEachAsync(dwgFiles, options, async (dwgFile, ct) =>
            {
                var result = await ProcessSingleDrawingAsync(
                    dwgFile,
                    inputJsonPath,
                    timestampedOutputFolder);

                lock (results)
                {
                    results.Add(result);
                }
            });

            var endTime = DateTime.Now;
            var duration = endTime - startTime;

            // Separate failed files (processed but validation failed - has JSON) 
            // from non-processed files (error before processing - no JSON)
            var failedFiles = results
                .Where(r => !r.Success && r.WasProcessed)
                .Select(r => r.DrawingName)
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

                // Define the expected output filename
                string outputFileName = $"{result.DrawingName}.json";
                
                // Set environment variables for this specific process
                // Pass JSON content directly via environment variable (no temp file needed!)
                process.StartInfo.EnvironmentVariables["INPUT_JSON_PATH"] = ""; // Empty = use content
                process.StartInfo.EnvironmentVariables["INPUT_JSON_CONTENT"] = drawingConfigJson;
                process.StartInfo.EnvironmentVariables["OUTPUT_FOLDER"] = outputFolder;
                process.StartInfo.EnvironmentVariables["OUTPUT_FILENAME"] = outputFileName;
                process.StartInfo.EnvironmentVariables["TIMESTAMP"] = timestamp;
                process.StartInfo.EnvironmentVariables["DRAWING_NAME"] = result.DrawingName;

                // Always log environment variables for debugging
                Console.WriteLine($"  ✅ Passing config JSON directly (no temp file)");
                Console.WriteLine($"  📦 Environment Variables:");
                Console.WriteLine($"     OUTPUT_FOLDER = {outputFolder}");
                Console.WriteLine($"     OUTPUT_FILENAME = {outputFileName}");
                Console.WriteLine($"     INPUT_JSON_CONTENT = {drawingConfigJson.Length} chars");
                Console.WriteLine($"     DRAWING_NAME = {result.DrawingName}");
                Console.WriteLine();

                // Capture output
                var outputBuilder = new StringBuilder();
                var dllLoadErrors = new List<string>();
                bool uiPluginLoadFailed = false;
                
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        
                        string data = e.Data.Trim();
                        
                        // Check for DLL loading messages
                        if (data.Contains("[Loading]", StringComparison.OrdinalIgnoreCase))
                        {
                            // Always show loading messages
                            Console.WriteLine($"  {data}");
                        }
                        
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
                        
                        // Only show important output or errors (but skip if already shown above)
                        if (!isVerboseNoise && !data.Contains("NETLOAD", StringComparison.OrdinalIgnoreCase) && !data.Contains("[Loading]", StringComparison.OrdinalIgnoreCase))
                        {
                            // Show all non-noise output without prefix (separator already shows which drawing)
                            Console.WriteLine($"  {e.Data}");
                        }
                        // Skip verbose noise entirely
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
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Add timeout to prevent hanging (30 minutes max per drawing)
                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30)))
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
                                process.Kill();
                                Console.WriteLine($"  ⚠️  WARNING: Process timed out after 30 minutes - killed");
                                result.ErrorMessage = "Process timed out after 30 minutes";
                                result.WasProcessed = false; // Mark as not processed due to timeout
                            }
                        }
                        catch (Exception killEx)
                        {
                            Console.WriteLine($"  ⚠️  WARNING: Failed to kill timed-out process: {killEx.Message}");
                        }
                    }
                }

                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                result.ExitCode = process.ExitCode;
                result.Output = outputBuilder.ToString();
                
                // Check for UIPlugin.dll loading failure
                // Only mark as failed if we actually detected an error in the output
                // (We can't verify success without LISP messages, but AutoCAD will show errors if NETLOAD fails)
                result.UIPluginLoadFailed = uiPluginLoadFailed;
                
                // Check for DLL loading errors
                if (dllLoadErrors.Count > 0)
                {
                    Console.WriteLine($"  ⚠️  WARNING: {dllLoadErrors.Count} DLL loading error(s) detected!");
                    result.ErrorMessage = $"DLL loading errors: {string.Join("; ", dllLoadErrors)}";
                }
                
                // Mark as processed (AutoCAD process was started)
                result.WasProcessed = true;
                
                // Check if output JSON was created (only created for failed files)
                string expectedJsonPath = Path.Combine(outputFolder, $"{result.DrawingName}.json");
                bool jsonCreated = File.Exists(expectedJsonPath);
                
                // Success logic: No JSON file = all validations passed (success)
                // Failure logic: JSON file exists = validations failed
                // Note: We only mark as "failed" if JSON exists, regardless of exit code
                // This ensures failed files list matches the JSON files created
                result.Success = !jsonCreated;

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
                    // Success: No JSON file created (all validations passed)
                    Console.WriteLine($"  ✅ SUCCESS: {result.DrawingName}");
                    Console.WriteLine($"     Duration: {result.Duration.TotalSeconds:F1}s | All validations passed");
                }
                else
                {
                    // Failed: JSON file exists (validations failed)
                    Console.WriteLine($"  ❌ FAILED: {result.DrawingName}");
                    Console.WriteLine($"     Duration: {result.Duration.TotalSeconds:F1}s | Failures detected (JSON created)");
                    
                    // Log exit code for debugging if non-zero
                    if (process.ExitCode != 0)
                    {
                        Console.WriteLine($"     Exit Code: {process.ExitCode}");
                    }
                }
                Console.WriteLine($"{new string('═', 80)}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.WasProcessed = false; // Not processed - exception before/during AutoCAD
                result.ErrorMessage = ex.Message;
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                Console.WriteLine();
                Console.WriteLine($"{new string('─', 80)}");
                Console.WriteLine($"  ❌ EXCEPTION: {result.DrawingName}");
                Console.WriteLine($"     Error: {ex.Message}");
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

            // Quit
            scriptBuilder.AppendLine("QUIT");

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
                Console.WriteLine($"\n  Non-Processed Files (errors before processing):");
                foreach (var result in results.Where(r => !r.Success && !r.WasProcessed))
                {
                    Console.WriteLine($"    - {result.DrawingName}");
                    if (!string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        Console.WriteLine($"      Error: {result.ErrorMessage}");
                    }
                }
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
        public List<string> FailedFiles { get; set; } = new List<string>(); // Processed but validation failed (has JSON)
        public List<string> NonProcessedFiles { get; set; } = new List<string>(); // Not processed (errors before AutoCAD)
        public Dictionary<string, string> NonProcessedFilesWithErrors { get; set; } = new Dictionary<string, string>(); // File name -> error message
        public bool UIPluginLoadFailed { get; set; } // True if UIPlugin.dll failed to load in any drawing
    }
}

