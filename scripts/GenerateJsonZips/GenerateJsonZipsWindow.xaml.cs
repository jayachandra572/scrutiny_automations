using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BatchProcessor.JsonDiff;
using BatchProcessor.PreScrutiny;
using BatchProcessor.Relations;
using BatchProcessor.Utils;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WinFormsFolderBrowser = System.Windows.Forms.FolderBrowserDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace BatchProcessor.Scripts.GenerateJsonZips
{
    public partial class GenerateJsonZipsWindow : Window
    {
        private const string SettingsFile = "Settings/generate_json_zips_settings.json";
        private const string WorkloadMapsFolder = "WorkloadMaps";
        private const string ReportLinkBaseUrl = "https://citizen-portal-epsilon.flowwlabs.tech/report/";

        private bool _isNavigatingBack = false;
        private CancellationTokenSource? _cts;
        private bool _mappingsValidated = false;

        public GenerateJsonZipsWindow()
        {
            InitializeComponent();
            LoadSettings();

            this.Closing += (s, args) =>
            {
                if (!_isNavigatingBack && System.Windows.Application.Current.MainWindow == this)
                    System.Windows.Application.Current.Shutdown();
            };
        }

        // ── Settings ──────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            try
            {
                // First try to load from appsettings.json as defaults
                TryLoadFromAppSettings();

                // Then overlay with saved user settings (takes priority)
                if (!File.Exists(SettingsFile)) return;
                var json = File.ReadAllText(SettingsFile);
                var s = JsonSerializer.Deserialize<GenerateJsonZipsSettings>(json);
                if (s == null) return;

                if (!string.IsNullOrWhiteSpace(s.DrawingsFolder)) TxtDrawingsFolder.Text = s.DrawingsFolder;
                if (!string.IsNullOrWhiteSpace(s.OutputFolder)) TxtOutputFolder.Text = s.OutputFolder;
                if (!string.IsNullOrWhiteSpace(s.AppParamsCsvFile)) TxtAppParamsCsv.Text = s.AppParamsCsvFile;
                if (!string.IsNullOrWhiteSpace(s.WorkloadMapCsvFile)) TxtWorkloadMapCsv.Text = s.WorkloadMapCsvFile;
                if (!string.IsNullOrWhiteSpace(s.AutoCADPath)) TxtAutoCADPath.Text = s.AutoCADPath;
                if (!string.IsNullOrWhiteSpace(s.CommonUtilsDll)) TxtCommonUtilsDll.Text = s.CommonUtilsDll;
                if (!string.IsNullOrWhiteSpace(s.CrxDll)) TxtCrxDll.Text = s.CrxDll;
                TxtMaxParallel.Text = s.MaxParallelProcesses.ToString();
                ChkVerboseLogging.IsChecked = s.VerboseLogging;
            }
            catch (Exception ex)
            {
                Log($"⚠️  Could not load settings: {ex.Message}");
            }
        }

        private void TryLoadFromAppSettings()
        {
            try
            {
                const string appSettingsPath = "appsettings.json";
                if (!File.Exists(appSettingsPath)) return;

                var json = File.ReadAllText(appSettingsPath);
                var root = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(json);
                var batch = root?["BatchProcessorSettings"];
                if (batch == null) return;

                TxtAutoCADPath.Text = batch["AutoCADPath"]?.GetValue<string>() ?? string.Empty;

                var dlls = batch["DllsToLoad"]?.AsArray();
                if (dlls != null)
                {
                    foreach (var dll in dlls)
                    {
                        string? path = dll?.GetValue<string>();
                        if (path == null) continue;
                        if (path.Contains("CommonUtils", StringComparison.OrdinalIgnoreCase))
                            TxtCommonUtilsDll.Text = path;
                        else if (path.Contains("Crx", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("UIPlugin", StringComparison.OrdinalIgnoreCase))
                            TxtCrxDll.Text = path;
                    }
                }
            }
            catch { /* appsettings is optional — silently ignore */ }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory("Settings");
                var s = BuildSettings();
                var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
                Log("💾 Settings saved.");
            }
            catch (Exception ex)
            {
                Log($"⚠️  Could not save settings: {ex.Message}");
            }
        }

        private GenerateJsonZipsSettings BuildSettings()
        {
            int.TryParse(TxtMaxParallel.Text, out int maxParallel);
            return new GenerateJsonZipsSettings
            {
                DrawingsFolder = TxtDrawingsFolder.Text.Trim(),
                OutputFolder = TxtOutputFolder.Text.Trim(),
                AppParamsCsvFile = TxtAppParamsCsv.Text.Trim(),
                WorkloadMapCsvFile = TxtWorkloadMapCsv.Text.Trim(),
                AutoCADPath = TxtAutoCADPath.Text.Trim(),
                CommonUtilsDll = TxtCommonUtilsDll.Text.Trim(),
                CrxDll = TxtCrxDll.Text.Trim(),
                MaxParallelProcesses = maxParallel > 0 ? maxParallel : 4,
                VerboseLogging = ChkVerboseLogging.IsChecked == true
            };
        }

        // ── Validation ────────────────────────────────────────────────────────

        private bool ValidateInputs(out string error)
        {
            if (string.IsNullOrWhiteSpace(TxtDrawingsFolder.Text))
                { error = "Drawings Folder is required."; return false; }
            if (!Directory.Exists(TxtDrawingsFolder.Text.Trim()))
                { error = "Drawings Folder does not exist."; return false; }
            if (string.IsNullOrWhiteSpace(TxtOutputFolder.Text))
                { error = "Output Folder is required."; return false; }
            if (string.IsNullOrWhiteSpace(TxtAppParamsCsv.Text) || !File.Exists(TxtAppParamsCsv.Text.Trim()))
                { error = "App Params CSV is required and must exist."; return false; }
            // Workload Map CSV is optional. If a path is supplied, it must exist.
            // When omitted, a random GUID is generated as the WorkloadID for each drawing.
            if (!string.IsNullOrWhiteSpace(TxtWorkloadMapCsv.Text) && !File.Exists(TxtWorkloadMapCsv.Text.Trim()))
                { error = "Workload Map CSV was provided but does not exist."; return false; }
            if (string.IsNullOrWhiteSpace(TxtAutoCADPath.Text) || !File.Exists(TxtAutoCADPath.Text.Trim()))
                { error = "AutoCAD path is required and must exist."; return false; }
            if (string.IsNullOrWhiteSpace(TxtCommonUtilsDll.Text) || !File.Exists(TxtCommonUtilsDll.Text.Trim()))
                { error = "CommonUtils DLL is required and must exist."; return false; }
            if (string.IsNullOrWhiteSpace(TxtCrxDll.Text) || !File.Exists(TxtCrxDll.Text.Trim()))
                { error = "CRX DLL is required and must exist."; return false; }

            error = string.Empty;
            return true;
        }

        // ── Button handlers ───────────────────────────────────────────────────

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private async void BtnValidateMappings_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs(out string error))
            {
                SetStatus($"❌ {error}", isError: true);
                return;
            }

            BtnValidateMappings.IsEnabled = false;
            BtnRun.IsEnabled = false;
            _mappingsValidated = false;
            SetStatus("🔍 Validating mappings...");
            TxtLog.Clear();

            // If no Workload Map CSV was supplied, generate one now: copy the App Params
            // CSV, stamp a fresh WorkloadID on every row (adding the column when absent)
            // and save it under WorkloadMaps/ with a datetime-stamped name, then use it
            // for validation and the run.
            if (string.IsNullOrWhiteSpace(TxtWorkloadMapCsv.Text))
            {
                string? generated = GenerateWorkloadMapCsv(TxtAppParamsCsv.Text.Trim());
                if (generated != null)
                    TxtWorkloadMapCsv.Text = generated;
            }

            var settings = BuildSettings();
            var processor = new GenerateJsonZipsProcessor(
                settings.DrawingsFolder!,
                settings.AppParamsCsvFile!,
                settings.WorkloadMapCsvFile!,
                settings.OutputFolder!,
                settings,
                Log);

            MappingValidationResult result = await Task.Run(() => processor.ValidateMappings());

            BtnValidateMappings.IsEnabled = true;

            if (result.ErrorMessage != null)
            {
                SetStatus($"❌ {result.ErrorMessage}", isError: true);
                return;
            }

            Log($"\n📊 Validation summary — {result.TotalDrawings} drawing(s) found");

            if (result.MissingAppParams.Count > 0)
            {
                Log($"❌ Missing App Params ({result.MissingAppParams.Count}):");
                foreach (var d in result.MissingAppParams) Log($"   • {d}");
            }

            if (result.MissingWorkloadIds.Count > 0)
            {
                Log($"❌ Missing WorkloadIDs ({result.MissingWorkloadIds.Count}):");
                foreach (var d in result.MissingWorkloadIds) Log($"   • {d}");
            }

            if (result.IsValid)
            {
                Log($"✅ All {result.TotalDrawings} drawing(s) are fully mapped. Ready to run.");

                // Emit the filename → report link CSV from whichever workload map is in
                // effect, so the links always match the IDs the zips will be built with.
                GenerateReportLinksCsv(TxtWorkloadMapCsv.Text.Trim());

                SetStatus($"✅ Validation passed — {result.TotalDrawings} drawing(s) ready.");
                _mappingsValidated = true;
                BtnRun.IsEnabled = true;
            }
            else
            {
                SetStatus($"❌ Validation failed — fix CSVs and re-validate.", isError: true);
            }
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (!_mappingsValidated)
            {
                SetStatus("⚠️  Please validate mappings first.", isError: true);
                return;
            }

            if (!ValidateInputs(out string error))
            {
                SetStatus($"❌ {error}", isError: true);
                return;
            }

            SaveSettings();
            TxtLog.Clear();
            GrpSkippedDrawings.Visibility = Visibility.Collapsed;
            StkSkippedDrawings.Children.Clear();

            BtnRun.IsEnabled = false;
            BtnValidateMappings.IsEnabled = false;
            BtnCancel.IsEnabled = true;

            _cts = new CancellationTokenSource();
            var settings = BuildSettings();
            var processor = new GenerateJsonZipsProcessor(
                settings.DrawingsFolder!,
                settings.AppParamsCsvFile!,
                settings.WorkloadMapCsvFile!,
                settings.OutputFolder!,
                settings,
                Log);

            SetStatus("⚙️  Processing...");
            TxtExecutionTime.Visibility = Visibility.Collapsed;

            var startTime = DateTime.Now;

            try
            {
                var result = await Task.Run(() => processor.ProcessAsync(_cts.Token), _cts.Token);

                var elapsed = DateTime.Now - startTime;

                if (result.Success)
                {
                    SetStatus($"✅ Done — {result.SuccessfulDrawings}/{result.TotalDrawings} drawing(s) packaged in {elapsed:mm\\:ss}");
                    TxtExecutionTime.Text = $"Total time: {elapsed:mm\\:ss}";
                    TxtExecutionTime.Visibility = Visibility.Visible;

                    if (result.SkippedDrawings > 0)
                    {
                        GrpSkippedDrawings.Visibility = Visibility.Visible;
                        foreach (var name in result.SkippedDrawingNames)
                        {
                            StkSkippedDrawings.Children.Add(new TextBlock
                            {
                                Text = $"• {name}",
                                Margin = new Thickness(4, 2, 0, 2)
                            });
                        }
                    }
                }
                else
                {
                    SetStatus($"❌ {result.ErrorMessage}", isError: true);
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("⏹ Cancelled by user.", isError: true);
                Log("⏹ Operation cancelled.");
            }
            catch (Exception ex)
            {
                SetStatus($"❌ Unexpected error: {ex.Message}", isError: true);
                Log($"❌ {ex}");
            }
            finally
            {
                BtnRun.IsEnabled = _mappingsValidated;
                BtnValidateMappings.IsEnabled = true;
                BtnCancel.IsEnabled = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            BtnCancel.IsEnabled = false;
            SetStatus("⏹ Cancelling...");
        }

        // ── Browse buttons ────────────────────────────────────────────────────

        private void BtnBrowseDrawingsFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = BrowseFolder("Select Drawings Folder");
            if (folder != null) { TxtDrawingsFolder.Text = folder; InvalidateMappings(); }
        }

        private void BtnBrowseOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = BrowseFolder("Select Output Folder");
            if (folder != null) TxtOutputFolder.Text = folder;
        }

        private void BtnBrowseAppParamsCsv_Click(object sender, RoutedEventArgs e)
        {
            var file = BrowseFile("CSV Files|*.csv|All Files|*.*", "Select Application Parameters CSV");
            if (file != null) { TxtAppParamsCsv.Text = file; InvalidateMappings(); }
        }

        private void BtnBrowseWorkloadMapCsv_Click(object sender, RoutedEventArgs e)
        {
            var file = BrowseFile("CSV Files|*.csv|All Files|*.*", "Select Workload Map CSV");
            if (file == null) return;

            // Store a copy inside the project so the CSV travels with the app.
            string stored = CopyIntoProjectFolder(file, WorkloadMapsFolder);
            TxtWorkloadMapCsv.Text = stored;
            InvalidateMappings();
        }

        /// <summary>
        /// Copies the given file into the project sub-folder and returns the copy's path.
        /// If the file is already inside that folder, it's returned unchanged.
        /// On any failure, falls back to the original path.
        /// </summary>
        private string CopyIntoProjectFolder(string sourcePath, string projectFolder)
        {
            try
            {
                Directory.CreateDirectory(projectFolder);
                string destPath = Path.Combine(projectFolder, Path.GetFileName(sourcePath));

                string fullSource = Path.GetFullPath(sourcePath);
                string fullDest = Path.GetFullPath(destPath);
                if (string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
                    return destPath;

                File.Copy(sourcePath, destPath, overwrite: true);
                Log($"📁 Stored Workload Map CSV in project: {destPath}");
                return destPath;
            }
            catch (Exception ex)
            {
                Log($"⚠️  Could not copy CSV into project folder: {ex.Message}. Using original path.");
                return sourcePath;
            }
        }

        /// <summary>
        /// Build a workload map by copying the App Params CSV and stamping a freshly
        /// generated WorkloadID onto every data row. The WorkloadID column is appended
        /// if the source CSV doesn't have one; if it does, its values are regenerated.
        /// The original App Params CSV is never modified — the copy is written to
        /// WorkloadMaps/ with a datetime-stamped name and used as the Workload Map CSV.
        /// Returns the new file's path, or null if it could not be generated.
        /// </summary>
        private string? GenerateWorkloadMapCsv(string appParamsCsvPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(appParamsCsvPath) || !File.Exists(appParamsCsvPath))
                    return null;

                var rows = CsvParser.ParseCsvRows(File.ReadAllText(appParamsCsvPath));
                if (rows.Count < 2)
                {
                    Log("⚠️  App Params CSV has no data rows — skipping workload map generation.");
                    return null;
                }

                var headers = rows[0];
                int workloadColumnIndex = FindWorkloadColumnIndex(headers);

                if (workloadColumnIndex == -1)
                {
                    workloadColumnIndex = headers.Count;
                    headers.Add("WorkloadID");
                    Log($"🆕 Added 'WorkloadID' column (index {workloadColumnIndex}).");
                }
                else
                {
                    Log($"♻️  Regenerating existing '{headers[workloadColumnIndex]}' column (index {workloadColumnIndex}).");
                }

                int stamped = 0;
                for (int i = 1; i < rows.Count; i++)
                {
                    var values = rows[i];

                    // Skip fully blank rows so we don't mint IDs for CSV padding.
                    if (values.All(string.IsNullOrWhiteSpace))
                        continue;

                    // Short rows (trailing commas omitted) are padded out to the column.
                    while (values.Count <= workloadColumnIndex)
                        values.Add(string.Empty);

                    values[workloadColumnIndex] = Guid.NewGuid().ToString();
                    stamped++;
                }

                Directory.CreateDirectory(WorkloadMapsFolder);
                string fileName = $"app_params_with_workload_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                string destPath = Path.Combine(WorkloadMapsFolder, fileName);

                var sb = new System.Text.StringBuilder();
                foreach (var row in rows)
                    sb.AppendLine(string.Join(",", row.Select(EscapeCsvValue)));

                File.WriteAllText(destPath, sb.ToString());
                Log($"🆔 Generated WorkloadIDs for {stamped} row(s): {destPath}");
                return destPath;
            }
            catch (Exception ex)
            {
                Log($"⚠️  Could not generate workload map: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Write a two-column CSV mapping each drawing filename to its citizen-portal
        /// report link, built from the WorkloadIDs in the effective Workload Map CSV
        /// (whether auto-generated or user-supplied). Saved to WorkloadMaps/ with a
        /// datetime-stamped name. Returns the new file's path, or null on failure.
        /// </summary>
        private string? GenerateReportLinksCsv(string workloadMapCsvPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workloadMapCsvPath) || !File.Exists(workloadMapCsvPath))
                    return null;

                var rows = CsvParser.ParseCsvRows(File.ReadAllText(workloadMapCsvPath));
                if (rows.Count < 2)
                {
                    Log("⚠️  Workload map has no data rows — skipping report links CSV.");
                    return null;
                }

                var headers = rows[0];
                int filenameColumnIndex = CsvParser.FindFilenameColumnIndex(headers);
                int workloadColumnIndex = FindWorkloadColumnIndex(headers);

                if (workloadColumnIndex == -1)
                {
                    Log("⚠️  No 'WorkloadID' column found — skipping report links CSV.");
                    return null;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("File Name,Report Link");

                int linked = 0;
                for (int i = 1; i < rows.Count; i++)
                {
                    var values = rows[i];

                    // Need both a filename and an ID to form a usable link.
                    if (filenameColumnIndex >= values.Count
                        || workloadColumnIndex >= values.Count
                        || string.IsNullOrWhiteSpace(values[filenameColumnIndex])
                        || string.IsNullOrWhiteSpace(values[workloadColumnIndex]))
                        continue;

                    string fileName = Path.GetFileName(values[filenameColumnIndex].Trim());
                    if (!fileName.Contains('.'))
                        fileName += ".dwg";

                    string reportLink = ReportLinkBaseUrl + values[workloadColumnIndex].Trim();

                    sb.AppendLine($"{EscapeCsvValue(fileName)},{EscapeCsvValue(reportLink)}");
                    linked++;
                }

                if (linked == 0)
                {
                    Log("⚠️  No rows had both a filename and a WorkloadID — skipping report links CSV.");
                    return null;
                }

                Directory.CreateDirectory(WorkloadMapsFolder);
                string destPath = Path.Combine(
                    WorkloadMapsFolder, $"report_links_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                File.WriteAllText(destPath, sb.ToString());
                Log($"🔗 Generated report links for {linked} drawing(s): {destPath}");
                return destPath;
            }
            catch (Exception ex)
            {
                Log($"⚠️  Could not generate report links CSV: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Locate an existing WorkloadID column, accepting the same header spellings
        /// as WorkloadMapCsvReader. Returns -1 when the CSV has no such column.
        /// </summary>
        private static int FindWorkloadColumnIndex(System.Collections.Generic.List<string> headers)
        {
            return headers.FindIndex(h =>
                h.Equals("WorkloadID", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("Workload ID", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("WorkLoad_Id", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("work_load_id", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("Work load ID", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("Work Load ID", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Re-quote a parsed cell so the written CSV round-trips through CsvParser.
        /// </summary>
        private static string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
        }

        private void BtnBrowseAutoCAD_Click(object sender, RoutedEventArgs e)
        {
            var file = BrowseFile("Executable Files|*.exe|All Files|*.*", "Select accoreconsole.exe");
            if (file != null) TxtAutoCADPath.Text = file;
        }

        private void BtnBrowseCommonUtilsDll_Click(object sender, RoutedEventArgs e)
        {
            var file = BrowseFile("DLL Files|*.dll|All Files|*.*", "Select CommonUtils.dll");
            if (file != null) TxtCommonUtilsDll.Text = file;
        }

        private void BtnBrowseCrxDll_Click(object sender, RoutedEventArgs e)
        {
            var file = BrowseFile("DLL Files|*.dll|All Files|*.*", "Select CRX DLL");
            if (file != null) TxtCrxDll.Text = file;
        }

        // ── Back navigation (same pattern as all other windows) ──────────────

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            _isNavigatingBack = true;

            var modeSelectionWindow = new ModeSelectionWindow();
            System.Windows.Application.Current.MainWindow = modeSelectionWindow;
            this.Close();

            if (modeSelectionWindow.ShowDialog() == true && modeSelectionWindow.IsModeSelected)
            {
                Window newWindow;

                switch (modeSelectionWindow.Mode)
                {
                    case ModeSelectionWindow.SelectedMode.PreScrutinyValidations:
                        newWindow = new Scripts.PreScrutiny.PreScrutinyWindow();
                        break;
                    case ModeSelectionWindow.SelectedMode.ScrutinyReports:
                        newWindow = new Scripts.ScrutinyReports.ScrutinyReportsWindow();
                        break;
                    case ModeSelectionWindow.SelectedMode.AnalyzeDrawings:
                        newWindow = new Scripts.AnalyzeDrawings.AnalyzeDrawingsWindow();
                        break;
                    case ModeSelectionWindow.SelectedMode.JsonDiffComparison:
                        newWindow = new JsonDiffWindow();
                        break;
                    case ModeSelectionWindow.SelectedMode.RelationsCreation:
                        newWindow = new RelationsWindow();
                        break;
                    case ModeSelectionWindow.SelectedMode.GenerateJsonZips:
                    default:
                        newWindow = new GenerateJsonZipsWindow();
                        break;
                }

                System.Windows.Application.Current.MainWindow = newWindow;
                newWindow.IsEnabled = true;
                newWindow.Visibility = Visibility.Visible;
                newWindow.WindowState = WindowState.Normal;
                newWindow.Show();
                newWindow.Activate();
                newWindow.Focus();
            }
            else
            {
                System.Windows.Application.Current.Shutdown();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void InvalidateMappings()
        {
            _mappingsValidated = false;
            BtnRun.IsEnabled = false;
            SetStatus("Ready. Validate mappings before running.");
        }

        private void SetStatus(string message, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = message;
                TxtStatus.Foreground = isError
                    ? System.Windows.Media.Brushes.Red
                    : System.Windows.Media.Brushes.Black;
            });
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText(message + Environment.NewLine);
                TxtLog.ScrollToEnd();
            });
        }

        private string? BrowseFolder(string description)
        {
            using var dlg = new WinFormsFolderBrowser { Description = description };
            return dlg.ShowDialog() == WinFormsDialogResult.OK ? dlg.SelectedPath : null;
        }

        private string? BrowseFile(string filter, string title)
        {
            var dlg = new WpfOpenFileDialog { Filter = filter, Title = title };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
    }
}
