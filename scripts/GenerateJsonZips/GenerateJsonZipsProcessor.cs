using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatchProcessor.PreScrutiny;
using BatchProcessor.Utils;

namespace BatchProcessor.Scripts.GenerateJsonZips
{
    public class GenerateJsonZipsProcessor
    {
        private const string DrawingFileValidationsCommand = "RunDrawingFileValidationsBatch";
        private const string PreScrutinyCommand = "RunPreScrutinyOnlyValidationsBatch";
        private const string ScrutinyReportsCommand = "GenerateScrutinyReportBatch";

        private readonly string _drawingsFolder;
        private readonly string _appParamsCsvPath;
        private readonly string _workloadMapCsvPath;
        private readonly string _outputFolder;
        private readonly GenerateJsonZipsSettings _settings;
        private readonly Action<string> _log;

        private CsvParameterMapper? _appParamsMapper;
        private WorkloadMapCsvReader? _workloadReader;

        public GenerateJsonZipsProcessor(
            string drawingsFolder,
            string appParamsCsvPath,
            string workloadMapCsvPath,
            string outputFolder,
            GenerateJsonZipsSettings settings,
            Action<string> log)
        {
            _drawingsFolder = drawingsFolder;
            _appParamsCsvPath = appParamsCsvPath;
            _workloadMapCsvPath = workloadMapCsvPath;
            _outputFolder = outputFolder;
            _settings = settings;
            _log = log;
        }

        /// <summary>
        /// Pre-flight check: load CSVs and report which drawings are missing mappings.
        /// Does not run any AutoCAD processing.
        /// </summary>
        public MappingValidationResult ValidateMappings()
        {
            var result = new MappingValidationResult();

            var appMapper = new CsvParameterMapper(_appParamsCsvPath);
            if (!appMapper.LoadCsv())
            {
                result.ErrorMessage = "Failed to load Application Parameters CSV.";
                return result;
            }

            // Workload Map CSV is optional. When omitted, a random GUID is generated
            // per drawing as its WorkloadID, so there are no "missing workload ID" failures.
            bool hasWorkloadMap = !string.IsNullOrWhiteSpace(_workloadMapCsvPath);
            WorkloadMapCsvReader? workloadReader = null;
            if (hasWorkloadMap)
            {
                workloadReader = new WorkloadMapCsvReader(_workloadMapCsvPath);
                if (!workloadReader.LoadCsv())
                {
                    result.ErrorMessage = "Failed to load Workload Map CSV.";
                    return result;
                }
            }

            var drawings = DiscoverDrawings();
            if (drawings.Count == 0)
            {
                result.ErrorMessage = $"No .dwg files found in: {_drawingsFolder}";
                return result;
            }

            result.TotalDrawings = drawings.Count;

            foreach (var dwg in drawings)
            {
                string name = Path.GetFileName(dwg);
                if (!appMapper.HasDrawing(dwg))
                    result.MissingAppParams.Add(name);
                if (hasWorkloadMap && !workloadReader!.HasWorkloadId(dwg))
                    result.MissingWorkloadIds.Add(name);
            }

            result.IsValid = result.MissingAppParams.Count == 0 && result.MissingWorkloadIds.Count == 0;
            return result;
        }

        /// <summary>
        /// Main processing pipeline: run up to 3 AutoCAD passes then zip all outputs.
        /// </summary>
        public async Task<GenerateJsonZipsResult> ProcessAsync(CancellationToken cancellationToken = default)
        {
            var totalTimer = System.Diagnostics.Stopwatch.StartNew();
            var result = new GenerateJsonZipsResult();

            // Phase 0 — pre-flight
            _log("🔍 Running pre-flight mapping check...");
            var validation = ValidateMappings();
            if (!validation.IsValid)
            {
                result.ErrorMessage = validation.ErrorMessage
                    ?? $"Mapping check failed. Missing app params: {validation.MissingAppParams.Count}, missing workload IDs: {validation.MissingWorkloadIds.Count}.";
                return result;
            }

            // Phase 1 — load CSVs
            _appParamsMapper = new CsvParameterMapper(_appParamsCsvPath);
            _appParamsMapper.LoadCsv();

            if (!string.IsNullOrWhiteSpace(_workloadMapCsvPath))
            {
                _workloadReader = new WorkloadMapCsvReader(_workloadMapCsvPath);
                _workloadReader.LoadCsv();
            }

            // Phase 2 — discover drawings
            var drawings = DiscoverDrawings();
            result.TotalDrawings = drawings.Count;
            _log($"📂 Found {drawings.Count} drawing(s) to process.");

            // Phase 3 — create temp working folder for this batch
            var batchTime = DateTime.Now;
            string batchFolder = Path.Combine(_outputFolder, "_tmp_batch_" + batchTime.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(batchFolder);

            // Temp sub-folders for each pass
            string tempDrawingValidations = Path.Combine(batchFolder, "_tmp_drawing_validations");
            string tempPreScrutiny = Path.Combine(batchFolder, "_tmp_pre_scrutiny");
            string tempScrutiny = Path.Combine(batchFolder, "_tmp_scrutiny");

            // Phase 4 — Drawing File Validations pass
            _log($"⚙️  Pass 1/3: Drawing File Validations ({DrawingFileValidationsCommand})...");
            var sw1 = System.Diagnostics.Stopwatch.StartNew();
            await RunBatchPassAsync(
                DrawingFileValidationsCommand,
                tempDrawingValidations,
                BuildCommonUtilsDllList(),
                cancellationToken,
                totalDrawings: drawings.Count,
                generateJsonAlways: true);
            sw1.Stop();
            _log($"   ⏱️  Pass 1 time: {FormatElapsed(sw1.Elapsed)}");

            cancellationToken.ThrowIfCancellationRequested();

            // Phase 5 — Pre-Scrutiny Validations pass
            _log($"⚙️  Pass 2/3: Pre-Scrutiny Validations ({PreScrutinyCommand})...");
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            await RunBatchPassAsync(
                PreScrutinyCommand,
                tempPreScrutiny,
                BuildCommonUtilsDllList(),
                cancellationToken,
                totalDrawings: drawings.Count,
                generateJsonAlways: true);
            sw2.Stop();
            _log($"   ⏱️  Pass 2 time: {FormatElapsed(sw2.Elapsed)}");

            cancellationToken.ThrowIfCancellationRequested();

            // Phase 6 — Scrutiny Reports pass
            _log($"⚙️  Pass 3/3: Scrutiny Reports Generation ({ScrutinyReportsCommand})...");
            var sw3 = System.Diagnostics.Stopwatch.StartNew();
            await RunBatchPassAsync(
                ScrutinyReportsCommand,
                tempScrutiny,
                BuildCrxDllList(),
                cancellationToken,
                totalDrawings: drawings.Count);
            sw3.Stop();
            _log($"   ⏱️  Pass 3 time: {FormatElapsed(sw3.Elapsed)}");

            cancellationToken.ThrowIfCancellationRequested();

            // Phase 7 — Build the 3 inner zips
            _log("📦 Building zip files...");
            var swZip = System.Diagnostics.Stopwatch.StartNew();
            var (succeeded, skipped) = BuildZips(
                batchFolder,
                drawings,
                tempDrawingValidations,
                tempPreScrutiny,
                tempScrutiny);
            swZip.Stop();
            _log($"   ⏱️  Zip build time: {FormatElapsed(swZip.Elapsed)}");

            // Phase 8 — Wrap the 3 zips into one outer zip with a readable name
            string outerZipName = $"JsonZips_{batchTime:dd-MMM-yyyy_HH-mm}.zip";
            string outerZipPath = Path.Combine(_outputFolder, outerZipName);
            _log($"📦 Creating outer zip: {outerZipName}...");
            WrapInOuterZip(batchFolder, outerZipPath);

            // Cleanup temp batch folder
            DeleteTempFolder(batchFolder);

            totalTimer.Stop();
            result.SuccessfulDrawings = succeeded;
            result.SkippedDrawings = skipped.Count;
            result.SkippedDrawingNames = skipped;
            result.OutputBatchFolder = _outputFolder;
            result.Success = true;
            result.Duration = totalTimer.Elapsed;

            _log($"");
            _log($"✅ Done. {succeeded} drawing(s) packaged.");
            _log($"⏱️  Pass 1 (Drawing Validations) : {FormatElapsed(sw1.Elapsed)}");
            _log($"⏱️  Pass 2 (Pre-Scrutiny)        : {FormatElapsed(sw2.Elapsed)}");
            _log($"⏱️  Pass 3 (Scrutiny Reports)    : {FormatElapsed(sw3.Elapsed)}");
            _log($"⏱️  Zip packaging                : {FormatElapsed(swZip.Elapsed)}");
            _log($"⏱️  Total                        : {FormatElapsed(totalTimer.Elapsed)}");
            _log($"📁 Output: {outerZipPath}");

            return result;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private List<string> DiscoverDrawings()
        {
            if (!Directory.Exists(_drawingsFolder))
                return new List<string>();

            return Directory.GetFiles(_drawingsFolder, "*.dwg", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f)
                .ToList();
        }

        private async Task RunBatchPassAsync(string command, string tempOutputFolder, List<string> dlls, CancellationToken ct, int totalDrawings = 0, bool generateJsonAlways = false)
        {
            Directory.CreateDirectory(tempOutputFolder);

            var processor = new DrawingBatchProcessor(
                accoreconsoleExePath: _settings.AutoCADPath ?? string.Empty,
                dllsToLoad: dlls,
                mainCommand: command,
                maxParallelism: _settings.MaxParallelProcesses,
                enableVerboseLogging: _settings.VerboseLogging);

            if (generateJsonAlways)
                processor.AdditionalEnvironmentVariables["GENERATE_JSON_ALWAYS"] = "true";

            processor.EnableCsvMapping(_appParamsCsvPath);

            var summary = await processor.ProcessFolderAsync(
                inputFolder: _drawingsFolder,
                outputFolder: tempOutputFolder,
                inputJsonPath: string.Empty,
                cancellationToken: ct);

            // FailedFiles = AutoCAD ran but validation failed — JSON was still generated
            // NonProcessedFiles = AutoCAD never ran — no JSON generated
            int noJson = summary.NonProcessedFiles?.Count ?? 0;
            int jsonGenerated = totalDrawings - noJson;
            _log($"   → JSON generated: {jsonGenerated}/{totalDrawings}, Not generated: {noJson}");
        }

        private List<string> BuildCommonUtilsDllList()
        {
            var dlls = new List<string>();
            if (!string.IsNullOrWhiteSpace(_settings.CommonUtilsDll))
                dlls.Add(_settings.CommonUtilsDll);
            return dlls;
        }

        private List<string> BuildCrxDllList()
        {
            var dlls = new List<string>();
            if (!string.IsNullOrWhiteSpace(_settings.CrxDll))
                dlls.Add(_settings.CrxDll);
            return dlls;
        }

        private (int succeeded, List<string> skipped) BuildZips(
            string batchFolder,
            List<string> drawings,
            string tempDrawingValidations,
            string tempPreScrutiny,
            string tempScrutiny)
        {
            string zipDrawingValidations = Path.Combine(batchFolder, "drawing-file-validations.zip");
            string zipPreScrutiny = Path.Combine(batchFolder, "pre-scrutiny-json.zip");
            string zipScrutiny = Path.Combine(batchFolder, "scrutinyjson.zip");

            using var archiveDv = ZipFile.Open(zipDrawingValidations, ZipArchiveMode.Create);
            using var archivePs = ZipFile.Open(zipPreScrutiny, ZipArchiveMode.Create);
            using var archiveSr = ZipFile.Open(zipScrutiny, ZipArchiveMode.Create);

            int succeeded = 0;
            var skipped = new List<string>();

            foreach (var dwgPath in drawings)
            {
                string drawingName = Path.GetFileNameWithoutExtension(dwgPath);
                string? workloadId = _workloadReader?.GetWorkloadId(dwgPath);

                if (string.IsNullOrWhiteSpace(workloadId))
                {
                    // No mapping (either no Workload Map CSV was provided, or this drawing
                    // isn't in it) — generate a random 36-char GUID as the WorkloadID.
                    workloadId = Guid.NewGuid().ToString();
                    _log($"   🆔 Generated WorkloadID for {drawingName}: {workloadId}");
                }

                // Each workload is stored as {workloadId}.zip inside the outer zip
                AddJsonAsNestedZip(archiveDv, tempDrawingValidations, drawingName, workloadId, "drawingFileValidations.json");
                AddJsonAsNestedZip(archivePs, tempPreScrutiny, drawingName, workloadId, "prescrutinyValidations.json");
                AddJsonAsNestedZip(archiveSr, tempScrutiny, drawingName, workloadId, "reportMetrics.json");

                succeeded++;
                _log($"   ✅ Packaged: {drawingName} → {workloadId}.zip");
            }

            return (succeeded, skipped);
        }

        private static void AddJsonAsNestedZip(
            ZipArchive outerArchive,
            string tempFolder,
            string drawingName,
            string workloadId,
            string entryFileName)
        {
            string? jsonFile = FindOutputJson(tempFolder, drawingName);
            string jsonContent = (jsonFile != null && File.Exists(jsonFile))
                ? File.ReadAllText(jsonFile)
                : $"{{\"drawingName\":\"{drawingName}\",\"status\":\"no_output_generated\"}}";

            // Create a {workloadId}.zip entry inside the outer zip
            var nestedEntry = outerArchive.CreateEntry($"{workloadId}.zip", CompressionLevel.NoCompression);
            using var nestedStream = nestedEntry.Open();
            using var nestedArchive = new ZipArchive(nestedStream, ZipArchiveMode.Create, leaveOpen: true);
            var innerEntry = nestedArchive.CreateEntry(entryFileName, CompressionLevel.Optimal);
            using var writer = new StreamWriter(innerEntry.Open());
            writer.Write(jsonContent);
        }

        private static string? FindOutputJson(string tempFolder, string drawingName)
        {
            if (!Directory.Exists(tempFolder))
                return null;

            // DrawingBatchProcessor creates {tempFolder}/{yyyyMMdd_HHmmss_fff}/{drawingName}.json
            return Directory.GetDirectories(tempFolder)
                .OrderByDescending(d => d)
                .Select(d => Path.Combine(d, drawingName + ".json"))
                .FirstOrDefault(f => File.Exists(f));
        }

        private static string FormatElapsed(TimeSpan t)
            => t.TotalHours >= 1
                ? $"{(int)t.TotalHours}h {t.Minutes:D2}m {t.Seconds:D2}s"
                : t.TotalMinutes >= 1
                    ? $"{t.Minutes}m {t.Seconds:D2}s"
                    : $"{t.Seconds}.{t.Milliseconds / 100}s";

        private static void WrapInOuterZip(string sourceFolder, string outerZipPath)
        {
            using var outer = ZipFile.Open(outerZipPath, ZipArchiveMode.Create);
            foreach (var file in Directory.GetFiles(sourceFolder, "*.zip"))
                outer.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.NoCompression);
        }

        private void DeleteTempFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex)
            {
                _log($"   ⚠️  Could not delete temp folder {Path.GetFileName(folder)}: {ex.Message}");
            }
        }
    }

    // ── Result / validation models ────────────────────────────────────────────

    public class MappingValidationResult
    {
        public bool IsValid { get; set; }
        public int TotalDrawings { get; set; }
        public List<string> MissingAppParams { get; set; } = new();
        public List<string> MissingWorkloadIds { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    public class GenerateJsonZipsResult
    {
        public bool Success { get; set; }
        public int TotalDrawings { get; set; }
        public int SuccessfulDrawings { get; set; }
        public int SkippedDrawings { get; set; }
        public List<string> SkippedDrawingNames { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public string? OutputBatchFolder { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
