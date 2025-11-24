using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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

            return new ProcessingSummary 
            { 
                FailedFiles = failedFiles,
                NonProcessedFiles = nonProcessedFiles.Select(x => x.DrawingName).ToList(),
                NonProcessedFilesWithErrors = nonProcessedFiles.ToDictionary(x => x.DrawingName, x => x.ErrorMessage ?? "Unknown error")
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

            bool allValid = true;
            foreach (var dll in _dllsToLoad)
            {
                if (!File.Exists(dll))
                {
                    Console.WriteLine($"❌ Error: DLL not found: {dll}");
                    allValid = false;
                }
            }

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
                Console.WriteLine($"[{timestamp}] ⏳ Processing: {result.DrawingName}");

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
                            Console.WriteLine($"  [{result.DrawingName}] 📋 Generated config from CSV (no template)");
                        }
                        else
                        {
                            Console.WriteLine($"  [{result.DrawingName}] 📋 Generated config from CSV + template");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  [{result.DrawingName}] ⚠️  No CSV parameters found");
                        if (string.IsNullOrWhiteSpace(drawingConfigPath) || !File.Exists(drawingConfigPath))
                        {
                            Console.WriteLine($"  [{result.DrawingName}] ❌ ERROR: No config available for this drawing");
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
                    Console.WriteLine($"  [{result.DrawingName}] ❌ ERROR: No config content available");
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
                    Console.WriteLine($"  [{result.DrawingName}] Script: {tempScriptPath}");
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
                Console.WriteLine($"  [{result.DrawingName}] ✅ Passing config JSON directly (no temp file)");
                Console.WriteLine($"  [{result.DrawingName}] ENV: OUTPUT_FOLDER = {outputFolder}");
                Console.WriteLine($"  [{result.DrawingName}] ENV: OUTPUT_FILENAME = {outputFileName}");
                Console.WriteLine($"  [{result.DrawingName}] ENV: INPUT_JSON_CONTENT = {drawingConfigJson.Length} chars");
                Console.WriteLine($"  [{result.DrawingName}] ENV: DRAWING_NAME = {result.DrawingName}");

                // Capture output
                var outputBuilder = new StringBuilder();
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        // Always show output - this is critical for debugging
                        Console.WriteLine($"  [{result.DrawingName}] {e.Data}");
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine($"ERROR: {e.Data}");
                        Console.WriteLine($"  [{result.DrawingName}] ❌ ERROR: {e.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                result.ExitCode = process.ExitCode;
                result.Output = outputBuilder.ToString();
                
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

                if (result.Success)
                {
                    // Success: No JSON file created (all validations passed)
                    Console.WriteLine($"[{timestamp}] ✅ Completed: {result.DrawingName} ({result.Duration.TotalSeconds:F1}s) - All validations passed");
                }
                else
                {
                    // Failed: JSON file exists (validations failed)
                    Console.WriteLine($"[{timestamp}] ❌ Failed: {result.DrawingName} ({result.Duration.TotalSeconds:F1}s) - Failures detected (JSON created)");
                    
                    // Log exit code for debugging if non-zero
                    if (process.ExitCode != 0)
                    {
                        Console.WriteLine($"[{timestamp}] ⚠️  Note: Process exit code was {process.ExitCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.WasProcessed = false; // Not processed - exception before/during AutoCAD
                result.ErrorMessage = ex.Message;
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                Console.WriteLine($"❌ Exception processing {result.DrawingName}: {ex.Message}");
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

            // Load all DLLs in order
            foreach (var dllPath in _dllsToLoad)
            {
                scriptBuilder.AppendLine($"NETLOAD \"{dllPath}\"");
            }

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
            
            Console.WriteLine($"  [{drawingName}] 📝 Parameter file: {paramFilePath}");
            
            if (_enableVerboseLogging)
            {
                Console.WriteLine($"\nGenerated Script:\n{scriptContent}\n");
                Console.WriteLine($"Parameter JSON:\n{paramsJson}\n");
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
    }

    public class ProcessingSummary
    {
        public List<string> FailedFiles { get; set; } = new List<string>(); // Processed but validation failed (has JSON)
        public List<string> NonProcessedFiles { get; set; } = new List<string>(); // Not processed (errors before AutoCAD)
        public Dictionary<string, string> NonProcessedFilesWithErrors { get; set; } = new Dictionary<string, string>(); // File name -> error message
    }
}

