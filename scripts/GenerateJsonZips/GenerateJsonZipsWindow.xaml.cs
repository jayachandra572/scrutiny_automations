using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BatchProcessor.JsonDiff;
using BatchProcessor.PreScrutiny;
using BatchProcessor.Relations;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WinFormsFolderBrowser = System.Windows.Forms.FolderBrowserDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace BatchProcessor.Scripts.GenerateJsonZips
{
    public partial class GenerateJsonZipsWindow : Window
    {
        private const string SettingsFile = "Settings/generate_json_zips_settings.json";
        private const string WorkloadMapsFolder = "WorkloadMaps";

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

            // If no Workload Map CSV was supplied, generate one now: map every
            // discovered drawing to a fresh GUID and save it under WorkloadMaps/
            // with a datetime-stamped name, then use it for validation and the run.
            if (string.IsNullOrWhiteSpace(TxtWorkloadMapCsv.Text))
            {
                string? generated = GenerateWorkloadMapCsv(TxtDrawingsFolder.Text.Trim());
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
        /// Generates a Workload Map CSV for every .dwg in the drawings folder,
        /// assigning each a fresh GUID as its WorkloadID. The file is saved into
        /// the WorkloadMaps/ project folder with a datetime-stamped name and its
        /// path is returned. Returns null if no drawings are found or on failure.
        /// </summary>
        private string? GenerateWorkloadMapCsv(string drawingsFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(drawingsFolder) || !Directory.Exists(drawingsFolder))
                    return null;

                var drawings = Directory.GetFiles(drawingsFolder, "*.dwg", SearchOption.TopDirectoryOnly);
                if (drawings.Length == 0)
                {
                    Log("⚠️  No .dwg files found — skipping workload map generation.");
                    return null;
                }

                Directory.CreateDirectory(WorkloadMapsFolder);
                string fileName = $"workload_map_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                string destPath = Path.Combine(WorkloadMapsFolder, fileName);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Marking File Link,WorkloadID");
                foreach (var dwg in drawings)
                    sb.AppendLine($"{Path.GetFileName(dwg)},{Guid.NewGuid()}");

                File.WriteAllText(destPath, sb.ToString());
                Log($"🆔 Generated workload map for {drawings.Length} drawing(s): {destPath}");
                return destPath;
            }
            catch (Exception ex)
            {
                Log($"⚠️  Could not generate workload map: {ex.Message}");
                return null;
            }
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
                    case ModeSelectionWindow.SelectedMode.BulkDownloadAndProcess:
                        newWindow = new Scripts.BulkDownload.BulkDownloadAndProcessWindow();
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
