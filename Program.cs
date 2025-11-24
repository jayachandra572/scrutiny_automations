using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using BatchProcessor.Configuration;

namespace BatchProcessor
{
    internal class ProgramConsole
    {
        private static AppSettings _settings;

        public static async Task RunAsync(string[] args)
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║          AutoCAD Batch Processor v1.0                       ║");
            Console.WriteLine("║          Standalone - Process Multiple DWG Files            ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝\n");

            // Load configuration from appsettings.json
            LoadConfiguration();

            // Parse command line arguments
            string inputFolder, outputFolder, inputJsonPath;
            string accoreconsoleExePath = _settings.BatchProcessorSettings.AutoCADPath;
            List<string> dllsToLoad = _settings.BatchProcessorSettings.DllsToLoad;
            List<string> availableCommands = _settings.BatchProcessorSettings.AvailableCommands;
            string mainCommand = _settings.BatchProcessorSettings.MainCommand;
            int maxParallelism = _settings.BatchProcessorSettings.MaxParallelProcesses;
            string selectedCommand = null;

            if (args.Length >= 3)
            {
                // Command line mode
                inputFolder = args[0];
                outputFolder = args[1];
                inputJsonPath = args[2];

                // Optional: custom AutoCAD path
                if (args.Length >= 4 && !string.IsNullOrWhiteSpace(args[3]))
                {
                    accoreconsoleExePath = args[3];
                }

                // Optional: max parallelism
                if (args.Length >= 5 && int.TryParse(args[4], out int parallel))
                {
                    maxParallelism = parallel;
                }

                // Optional: command selection
                if (args.Length >= 6 && !string.IsNullOrWhiteSpace(args[5]))
                {
                    selectedCommand = args[5];
                }
            }
            else if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h" || args[0] == "/?"))
            {
                ShowHelp();
                return;
            }
            else
            {
                // Interactive mode
                Console.WriteLine("📋 Enter the required information:\n");

                Console.Write("Input folder (containing .dwg files): ");
                inputFolder = Console.ReadLine()?.Trim('"');

                Console.Write("Output folder (for JSON results): ");
                outputFolder = Console.ReadLine()?.Trim('"');

                Console.Write("Input JSON configuration file: ");
                inputJsonPath = Console.ReadLine()?.Trim('"');

                // Command selection (if multiple commands available)
                if (availableCommands != null && availableCommands.Count > 0)
                {
                    Console.WriteLine($"\n🎯 Select command to execute:");
                    for (int i = 0; i < availableCommands.Count; i++)
                    {
                        Console.WriteLine($"  {i + 1}. {availableCommands[i]}");
                    }
                    Console.Write($"Enter number (1-{availableCommands.Count}) or press Enter for default ({mainCommand}): ");
                    
                    string commandChoice = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(commandChoice) && int.TryParse(commandChoice, out int cmdIndex))
                    {
                        if (cmdIndex >= 1 && cmdIndex <= availableCommands.Count)
                        {
                            selectedCommand = availableCommands[cmdIndex - 1];
                        }
                    }
                }

                Console.Write($"\nAutoCAD path (press Enter for default):\nDefault: {accoreconsoleExePath}\n> ");
                string customPath = Console.ReadLine()?.Trim('"');
                if (!string.IsNullOrWhiteSpace(customPath))
                {
                    accoreconsoleExePath = customPath;
                }

                Console.Write($"\nMax parallel processes (press Enter for default: {maxParallelism}): ");
                string parallelInput = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(parallelInput) && int.TryParse(parallelInput, out int parallel))
                {
                    maxParallelism = parallel;
                }
            }

            // Validate inputs
            if (!ValidateInputs(inputFolder, outputFolder, inputJsonPath, accoreconsoleExePath, dllsToLoad))
            {
                Console.WriteLine("\n❌ Validation failed. Press any key to exit...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine("\n" + new string('─', 64));
            Console.WriteLine("Starting batch processing...");
            Console.WriteLine(new string('─', 64) + "\n");

            // Use selected command if specified, otherwise use default mainCommand
            string commandToExecute = !string.IsNullOrWhiteSpace(selectedCommand) ? selectedCommand : mainCommand;

            try
            {
                // Create processor and run
                var processor = new DrawingBatchProcessor(
                    accoreconsoleExePath: accoreconsoleExePath,
                    dllsToLoad: dllsToLoad,
                    mainCommand: commandToExecute,
                    maxParallelism: maxParallelism,
                    tempScriptFolder: _settings.BatchProcessorSettings.TempScriptFolder,
                    enableVerboseLogging: _settings.BatchProcessorSettings.EnableVerboseLogging
                );

                await processor.ProcessFolderAsync(inputFolder, outputFolder, inputJsonPath);

                Console.WriteLine("✅ All processing complete!\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Fatal error: {ex.Message}");
                Console.WriteLine($"\nStack trace:\n{ex.StackTrace}");
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        static void LoadConfiguration()
        {
            try
            {
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

                var config = configBuilder.Build();
                _settings = new AppSettings();
                
                // Load BatchProcessorSettings section manually
                var settingsSection = config.GetSection("BatchProcessorSettings");
                if (settingsSection.Exists())
                {
                    var batchSettings = new BatchProcessorSettings();
                    
                    // Read simple values
                    if (!string.IsNullOrEmpty(settingsSection["AutoCADPath"]))
                        batchSettings.AutoCADPath = settingsSection["AutoCADPath"];
                    
                    if (!string.IsNullOrEmpty(settingsSection["MainCommand"]))
                        batchSettings.MainCommand = settingsSection["MainCommand"];
                    
                    if (int.TryParse(settingsSection["MaxParallelProcesses"], out int maxProc))
                        batchSettings.MaxParallelProcesses = maxProc;
                    
                    if (!string.IsNullOrEmpty(settingsSection["TempScriptFolder"]))
                        batchSettings.TempScriptFolder = settingsSection["TempScriptFolder"];
                    
                    if (bool.TryParse(settingsSection["EnableVerboseLogging"], out bool verbose))
                        batchSettings.EnableVerboseLogging = verbose;
                    
                    // Read DllsToLoad array
                    var dllsSection = settingsSection.GetSection("DllsToLoad");
                    if (dllsSection.Exists())
                    {
                        batchSettings.DllsToLoad = new List<string>();
                        foreach (var child in dllsSection.GetChildren())
                        {
                            if (!string.IsNullOrEmpty(child.Value))
                                batchSettings.DllsToLoad.Add(child.Value);
                        }
                    }
                    
                    // Read AvailableCommands array
                    var commandsSection = settingsSection.GetSection("AvailableCommands");
                    if (commandsSection.Exists())
                    {
                        batchSettings.AvailableCommands = new List<string>();
                        foreach (var child in commandsSection.GetChildren())
                        {
                            if (!string.IsNullOrEmpty(child.Value))
                                batchSettings.AvailableCommands.Add(child.Value);
                        }
                    }
                    
                    _settings.BatchProcessorSettings = batchSettings;
                }

                // If config file doesn't exist or is empty, use defaults
                if (_settings.BatchProcessorSettings == null)
                {
                    _settings.BatchProcessorSettings = new BatchProcessorSettings();
                }

                Console.WriteLine("✅ Configuration loaded from appsettings.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Warning: Could not load appsettings.json ({ex.Message})");
                Console.WriteLine("Using default settings...\n");
                _settings = new AppSettings
                {
                    BatchProcessorSettings = new BatchProcessorSettings()
                };
            }
        }

        static bool ValidateInputs(string inputFolder, string outputFolder, string inputJsonPath, 
            string accoreconsoleExePath, List<string> dllsToLoad)
        {
            bool isValid = true;

            // Validate input folder
            if (string.IsNullOrWhiteSpace(inputFolder))
            {
                Console.WriteLine("❌ Error: Input folder path is required");
                isValid = false;
            }
            else if (!Directory.Exists(inputFolder))
            {
                Console.WriteLine($"❌ Error: Input folder does not exist: {inputFolder}");
                isValid = false;
            }
            else
            {
                // Check for DWG files
                var dwgFiles = Directory.GetFiles(inputFolder, "*.dwg");
                if (dwgFiles.Length == 0)
                {
                    Console.WriteLine($"⚠️  Warning: No .dwg files found in: {inputFolder}");
                }
                else
                {
                    Console.WriteLine($"✅ Found {dwgFiles.Length} .dwg file(s)");
                }
            }

            // Validate output folder
            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                Console.WriteLine($"❌ Error: Output folder path is required");
                isValid = false;
            }
            else
            {
                Console.WriteLine($"✅ Output folder: {outputFolder}");
            }

            // Validate input JSON
            if (string.IsNullOrWhiteSpace(inputJsonPath))
            {
                Console.WriteLine("❌ Error: Input JSON file path is required");
                isValid = false;
            }
            else if (!File.Exists(inputJsonPath))
            {
                Console.WriteLine($"❌ Error: Input JSON file does not exist: {inputJsonPath}");
                isValid = false;
            }
            else
            {
                Console.WriteLine($"✅ Config JSON found");
            }

            // Validate accoreconsole.exe
            if (string.IsNullOrWhiteSpace(accoreconsoleExePath))
            {
                Console.WriteLine("❌ Error: AutoCAD path is required");
                isValid = false;
            }
            else if (!File.Exists(accoreconsoleExePath))
            {
                Console.WriteLine($"❌ Error: accoreconsole.exe not found at: {accoreconsoleExePath}");
                Console.WriteLine("\nPlease update the AutoCADPath in appsettings.json");
                isValid = false;
            }
            else
            {
                Console.WriteLine($"✅ AutoCAD found");
            }

            // Validate DLLs
            if (dllsToLoad == null || dllsToLoad.Count == 0)
            {
                Console.WriteLine("⚠️  Warning: No DLLs configured to load in appsettings.json");
                Console.WriteLine("   Please update DllsToLoad in appsettings.json");
            }
            else
            {
                Console.WriteLine($"✅ {dllsToLoad.Count} DLL(s) configured");
                bool allDllsExist = true;
                foreach (var dll in dllsToLoad)
                {
                    if (!File.Exists(dll))
                    {
                        Console.WriteLine($"❌ Error: DLL not found: {dll}");
                        allDllsExist = false;
                        isValid = false;
                    }
                }
                if (allDllsExist)
                {
                    Console.WriteLine($"✅ All DLLs found");
                }
            }

            return isValid;
        }

        static void ShowHelp()
        {
            Console.WriteLine(@"
AutoCAD Batch Processor - Help
═══════════════════════════════════════════════════════════════

USAGE:
  BatchProcessor.exe [options]

INTERACTIVE MODE:
  BatchProcessor.exe
  (You will be prompted for paths)

COMMAND LINE MODE:
  BatchProcessor.exe ""<input_folder>"" ""<output_folder>"" ""<config.json>""

ADVANCED MODE:
  BatchProcessor.exe ""<input>"" ""<output>"" ""<config>"" ""<autocad_path>"" <parallel> ""<command>""

ARGUMENTS:
  input_folder    - Folder containing .dwg files to process
  output_folder   - Folder where JSON results will be saved
  config.json     - JSON file with processing parameters
  autocad_path    - (Optional) Path to accoreconsole.exe
  parallel        - (Optional) Max number of parallel processes
  command         - (Optional) AutoCAD command to execute

CONFIGURATION FILE:
  Edit appsettings.json to configure:
  - AutoCADPath: Path to accoreconsole.exe
  - DllsToLoad: List of DLLs to load (full paths, in order)
  - AvailableCommands: List of commands to choose from at runtime
  - MainCommand: Default AutoCAD command to execute
  - MaxParallelProcesses: Number of parallel processes
  - And more...

  This allows you to change settings without recompiling!

COMMAND SELECTION:
  If AvailableCommands are configured in appsettings.json, you can:
  - Choose from a menu in interactive mode
  - Specify command as 6th argument in command line mode
  - Leave blank to use MainCommand as default

EXAMPLES:
  1. Interactive mode (with command selection menu):
     BatchProcessor.exe

  2. Process all drawings in a folder:
     BatchProcessor.exe ""C:\Drawings"" ""C:\Results"" ""config.json""

  3. Use AutoCAD 2024 with 6 parallel processes:
     BatchProcessor.exe ""C:\Drawings"" ""C:\Results"" ""config.json"" ^
       ""C:\Program Files\Autodesk\AutoCAD 2024\accoreconsole.exe"" 6

  4. Run with specific command (ExtractScrutinyMetricsBatch):
     BatchProcessor.exe ""C:\Drawings"" ""C:\Results"" ""config.json"" ^
       ""C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe"" 4 ""ExtractScrutinyMetricsBatch""

REQUIRED DLLs:
  Configure in appsettings.json under DllsToLoad:
  - CommonUtils.dll
  - Newtonsoft.Json.dll
  - CrxApp.dll
  (Full paths required, loaded in order)

OUTPUT:
  Each drawing produces a timestamped JSON file:
    <DrawingName>_<Timestamp>.json

NOTES:
  - No AutoCAD GUI will open (uses accoreconsole.exe)
  - Files are processed in parallel for speed
  - Failed drawings don't stop the batch
  - Progress is shown in real-time
  - Fully standalone - no project dependencies!

For more information, see README.md

═══════════════════════════════════════════════════════════════
");
        }
    }
}

