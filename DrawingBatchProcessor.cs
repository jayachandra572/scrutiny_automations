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
            _mainCommand = string.IsNullOrWhiteSpace(mainCommand) ? "ProcessWithJsonBatch" : mainCommand;
            _maxParallelism = maxParallelism;
            _tempScriptFolder = string.IsNullOrWhiteSpace(tempScriptFolder) ? Path.GetTempPath() : tempScriptFolder;
            _enableVerboseLogging = enableVerboseLogging;
        }

        /// <summary>
        /// Process all DWG files in a folder
        /// </summary>
        public async Task ProcessFolderAsync(
            string inputFolder,
            string outputFolder,
            string inputJsonPath)
        {
            if (!Directory.Exists(inputFolder))
            {
                Console.WriteLine($"ERROR: Input folder not found: {inputFolder}");
                return;
            }

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
                Console.WriteLine($"Created output folder: {outputFolder}");
            }

            // Validate DLLs exist
            ValidateDlls();

            // Get all DWG files
            var dwgFiles = Directory.GetFiles(inputFolder, "*.dwg", SearchOption.TopDirectoryOnly);

            if (dwgFiles.Length == 0)
            {
                Console.WriteLine($"No DWG files found in {inputFolder}");
                return;
            }

            Console.WriteLine($"\n╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║  AutoCAD Batch Processor (Standalone Mode)                  ║");
            Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"\n📁 Input Folder:  {inputFolder}");
            Console.WriteLine($"📁 Output Folder: {outputFolder}");
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
                    outputFolder);

                lock (results)
                {
                    results.Add(result);
                }
            });

            var endTime = DateTime.Now;
            var duration = endTime - startTime;

            // Print summary
            PrintSummary(results, duration);
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

                // Create temporary script file for this drawing
                string tempScriptPath = CreateTemporaryScript(dwgPath, timestamp, inputJsonPath, outputFolder, result.DrawingName);

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
                process.StartInfo.EnvironmentVariables["INPUT_JSON_PATH"] = inputJsonPath;
                process.StartInfo.EnvironmentVariables["OUTPUT_FOLDER"] = outputFolder;
                process.StartInfo.EnvironmentVariables["OUTPUT_FILENAME"] = outputFileName;
                process.StartInfo.EnvironmentVariables["TIMESTAMP"] = timestamp;
                process.StartInfo.EnvironmentVariables["DRAWING_NAME"] = result.DrawingName;

                // Always log environment variables for debugging
                Console.WriteLine($"  [{result.DrawingName}] ENV: OUTPUT_FOLDER = {outputFolder}");
                Console.WriteLine($"  [{result.DrawingName}] ENV: OUTPUT_FILENAME = {outputFileName}");
                Console.WriteLine($"  [{result.DrawingName}] ENV: INPUT_JSON_PATH = {inputJsonPath}");
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
                result.Success = process.ExitCode == 0;

                // Clean up temp script and parameter file
                try { File.Delete(tempScriptPath); } catch { }
                try 
                { 
                    string paramFile = Path.Combine(_tempScriptFolder, $"params_{timestamp}_{result.DrawingName}.json");
                    File.Delete(paramFile); 
                } 
                catch { }

                if (result.Success)
                {
                    // Verify output JSON was created
                    string expectedJsonPath = Path.Combine(outputFolder, $"{result.DrawingName}.json");
                    bool jsonCreated = File.Exists(expectedJsonPath);
                    
                    if (jsonCreated)
                    {
                        Console.WriteLine($"[{timestamp}] ✅ Completed: {result.DrawingName} ({result.Duration.TotalSeconds:F1}s) - JSON created");
                    }
                    else
                    {
                        Console.WriteLine($"[{timestamp}] ⚠️  Completed: {result.DrawingName} ({result.Duration.TotalSeconds:F1}s) - BUT JSON NOT FOUND at: {expectedJsonPath}");
                        Console.WriteLine($"  [{result.DrawingName}] 🔍 Check if CrxApp command is reading environment variables!");
                        
                        // List what files ARE in the output folder for debugging
                        if (Directory.Exists(outputFolder))
                        {
                            var filesInOutput = Directory.GetFiles(outputFolder, "*.json");
                            Console.WriteLine($"  [{result.DrawingName}] 📂 Files in output folder: {filesInOutput.Length}");
                            if (filesInOutput.Length > 0 && _enableVerboseLogging)
                            {
                                foreach (var f in filesInOutput.Take(5))
                                {
                                    Console.WriteLine($"      - {Path.GetFileName(f)}");
                                }
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[{timestamp}] ❌ Failed: {result.DrawingName} (Exit code: {result.ExitCode})");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
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

        private void PrintSummary(List<ProcessingResult> results, TimeSpan duration)
        {
            Console.WriteLine($"\n{new string('═', 64)}");
            Console.WriteLine($"  PROCESSING SUMMARY");
            Console.WriteLine($"{new string('═', 64)}");

            int successful = results.Count(r => r.Success);
            int failed = results.Count(r => !r.Success);

            Console.WriteLine($"\n  ✅ Successful: {successful}");
            Console.WriteLine($"  ❌ Failed:     {failed}");
            Console.WriteLine($"  📊 Total:      {results.Count}");
            Console.WriteLine($"  ⏱️  Duration:   {duration.TotalMinutes:F2} minutes");
            
            if (results.Count > 0)
            {
                Console.WriteLine($"  ⚡ Avg Speed:  {duration.TotalSeconds / results.Count:F2} sec/file");
            }

            if (failed > 0)
            {
                Console.WriteLine($"\n  Failed Files:");
                foreach (var result in results.Where(r => !r.Success))
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
    }
}

