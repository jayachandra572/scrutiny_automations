using System.Collections.Generic;

namespace BatchProcessor.Configuration
{
    /// <summary>
    /// Configuration settings for the batch processor
    /// All settings can be modified in appsettings.json without recompiling
    /// </summary>
    public class BatchProcessorSettings
    {
        /// <summary>
        /// Path to accoreconsole.exe
        /// Default: C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe
        /// </summary>
        public string AutoCADPath { get; set; } = @"C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe";

        /// <summary>
        /// List of DLLs to load in AutoCAD (in order)
        /// Full paths to each DLL
        /// Example: ["C:\\Path\\CommonUtils.dll", "C:\\Path\\CrxApp.dll"]
        /// </summary>
        public List<string> DllsToLoad { get; set; } = new List<string>();

        /// <summary>
        /// AutoCAD command to execute on each drawing (legacy, kept for backward compatibility)
        /// If Commands list is empty, this single command will be used
        /// Default: ProcessWithJsonBatch
        /// </summary>
        public List<string> AvailableCommands { get; set; } = new List<string>();

        /// <summary>
        /// Default AutoCAD command (used if AvailableCommands is empty or no selection made)
        /// Default: ProcessWithJsonBatch
        /// </summary>
        public string MainCommand { get; set; } = "ProcessWithJsonBatch";

        /// <summary>
        /// Maximum number of drawings to process in parallel
        /// Default: 4
        /// </summary>
        public int MaxParallelProcesses { get; set; } = 4;

        /// <summary>
        /// Temporary folder for script files (leave empty for system temp)
        /// </summary>
        public string TempScriptFolder { get; set; } = string.Empty;

        /// <summary>
        /// Enable verbose logging
        /// </summary>
        public bool EnableVerboseLogging { get; set; } = false;
    }

    public class AppSettings
    {
        public BatchProcessorSettings BatchProcessorSettings { get; set; } = new BatchProcessorSettings();
    }
}

