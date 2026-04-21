using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BatchProcessor.PreScrutiny;
using BatchProcessor.JsonDiff;
using BatchProcessor.Relations;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WinFormsFolderBrowser = System.Windows.Forms.FolderBrowserDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace BatchProcessor.Scripts.ScrutinyReports
{
    public partial class ScrutinyReportsWindow : Window
    {
        private const string UserSettingsFile = "Settings/scrutiny_reports_settings.json";
        private bool _isNavigatingBack = false;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task<ProcessingSummary>? _currentProcessingTask;
        private int _totalFiles = 0;
        private int _completedFiles = 0;
        private DateTime? _processingStartTime = null;

        // Timer tracking for active files
        private Dictionary<string, DateTime> _fileStartTimes = new Dictionary<string, DateTime>();
        private System.Windows.Threading.DispatcherTimer? _timerUpdateTimer;
        private Dictionary<string, TextBlock> _timerTextBlocks = new Dictionary<string, TextBlock>();

        public ScrutinyReportsWindow()
        {
            InitializeComponent();
            LoadUserSettings();

            // Initialize timer for updating file timers
            _timerUpdateTimer = new System.Windows.Threading.DispatcherTimer();
            _timerUpdateTimer.Interval = TimeSpan.FromSeconds(1);
            _timerUpdateTimer.Tick += TimerUpdateTimer_Tick;

            // Handle window closing
            this.Closing += (s, args) =>
            {
                if (!_isNavigatingBack && System.Windows.Application.Current.MainWindow == this)
                {
                    _timerUpdateTimer?.Stop();
                    System.Windows.Application.Current.Shutdown();
                }
            };
        }

        private void TimerUpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateActiveFileTimers();
        }

        private void UpdateActiveFileTimers()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateActiveFileTimers());
                return;
            }

            if (_fileStartTimes.Count == 0)
            {
                StkActiveTimers.Children.Clear();
                _timerTextBlocks.Clear();
                GrpActiveTimers.Visibility = Visibility.Collapsed;
                return;
            }

            GrpActiveTimers.Visibility = Visibility.Visible;
            var now = DateTime.Now;
            var sortedFiles = _fileStartTimes.OrderBy(kvp => kvp.Value).ToList();

            var activeFileNames = new HashSet<string>(sortedFiles.Select(kvp => kvp.Key));
            var filesToRemove = _timerTextBlocks.Keys.Where(k => !activeFileNames.Contains(k)).ToList();
            foreach (var fileName in filesToRemove)
            {
                _timerTextBlocks.Remove(fileName);
            }

            for (int i = 0; i < sortedFiles.Count; i++)
            {
                var kvp = sortedFiles[i];
                string fileName = kvp.Key;
                DateTime startTime = kvp.Value;
                TimeSpan elapsed = now - startTime;

                string timeString;
                if (elapsed.TotalHours >= 1)
                {
                    timeString = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                }
                else
                {
                    timeString = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                }

                if (_timerTextBlocks.ContainsKey(fileName))
                {
                    var timerBlock = _timerTextBlocks[fileName];
                    timerBlock.Text = $"⏱️ {timeString}";

                    if (elapsed.TotalMinutes >= 6)
                    {
                        timerBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                        timerBlock.Text = $"⏱️ {timeString} ⚠️ TIMEOUT";
                    }
                    else if (elapsed.TotalMinutes >= 5)
                    {
                        timerBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
                    }
                    else
                    {
                        timerBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkBlue);
                    }
                }
                else
                {
                    var timerGrid = new Grid { Margin = new Thickness(5, 2, 0, 2) };
                    timerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    timerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var fileNameBlock = new TextBlock
                    {
                        Text = $"📄 {Path.GetFileNameWithoutExtension(fileName)}",
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        FontSize = 11,
                        TextWrapping = TextWrapping.NoWrap,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = Path.GetFileNameWithoutExtension(fileName)
                    };
                    Grid.SetColumn(fileNameBlock, 0);

                    var timerBlock = new TextBlock
                    {
                        Text = $"⏱️ {timeString}",
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkBlue),
                        Margin = new Thickness(10, 0, 0, 0),
                        MinWidth = 100,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(timerBlock, 1);

                    if (elapsed.TotalMinutes >= 6)
                    {
                        timerBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                        timerBlock.Text = $"⏱️ {timeString} ⚠️ TIMEOUT";
                    }
                    else if (elapsed.TotalMinutes >= 5)
                    {
                        timerBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
                    }

                    timerGrid.Children.Add(fileNameBlock);
                    timerGrid.Children.Add(timerBlock);

                    if (i < StkActiveTimers.Children.Count)
                    {
                        StkActiveTimers.Children.Insert(i, timerGrid);
                    }
                    else
                    {
                        StkActiveTimers.Children.Add(timerGrid);
                    }

                    _timerTextBlocks[fileName] = timerBlock;
                }
            }

            for (int i = StkActiveTimers.Children.Count - 1; i >= 0; i--)
            {
                var child = StkActiveTimers.Children[i];
                if (child is Grid grid && grid.Children.Count > 0)
                {
                    var fileNameBlock = grid.Children[0] as TextBlock;
                    if (fileNameBlock != null)
                    {
                        string displayName = fileNameBlock.Text.Replace("📄 ", "");
                        string fullFileName = _fileStartTimes.Keys.FirstOrDefault(k => Path.GetFileNameWithoutExtension(k) == displayName);
                        if (fullFileName == null || !_fileStartTimes.ContainsKey(fullFileName))
                        {
                            StkActiveTimers.Children.RemoveAt(i);
                        }
                    }
                }
            }
        }

        private void StartFileTimer(string fileName)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => StartFileTimer(fileName));
                return;
            }

            _fileStartTimes[fileName] = DateTime.Now;

            if (_timerUpdateTimer != null && !_timerUpdateTimer.IsEnabled)
            {
                _timerUpdateTimer.Start();
            }

            UpdateActiveFileTimers();
        }

        private void StopFileTimer(string fileName)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => StopFileTimer(fileName));
                return;
            }

            if (_fileStartTimes.ContainsKey(fileName))
            {
                _fileStartTimes.Remove(fileName);
            }

            _timerTextBlocks.Remove(fileName);

            if (_fileStartTimes.Count == 0 && _timerUpdateTimer != null)
            {
                _timerUpdateTimer.Stop();
            }

            UpdateActiveFileTimers();
        }

        private void ClearAllFileTimers()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ClearAllFileTimers());
                return;
            }

            _fileStartTimes.Clear();
            _timerTextBlocks.Clear();
            _timerUpdateTimer?.Stop();
            UpdateActiveFileTimers();
        }

        #region Browse Buttons

        private void BtnBrowseInput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WinFormsFolderBrowser
            {
                Description = "Select input folder containing .dwg files",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == WinFormsDialogResult.OK)
            {
                TxtInputFolder.Text = dialog.SelectedPath;
            }
        }

        private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WinFormsFolderBrowser
            {
                Description = "Select output folder for reports",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == WinFormsDialogResult.OK)
            {
                TxtOutputFolder.Text = dialog.SelectedPath;
            }
        }

        private void BtnBrowseCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "Select CSV parameter file",
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                TxtCsvFile.Text = dialog.FileName;
                LogMessage($"📊 CSV file selected: {System.IO.Path.GetFileName(dialog.FileName)}");
            }
        }

        private void BtnBrowseCrxDll_Click(object sender, RoutedEventArgs e)
        {
            BrowseForDll(TxtCrxDll, "Select Crx.dll");
        }

        private void BtnBrowseAutoCAD_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "Select accoreconsole.exe",
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true,
                FileName = "accoreconsole.exe"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtAutoCADPath.Text = dialog.FileName;
            }
        }

        private void BtnSetMaxParallel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string inputFolder = TxtInputFolder.Text;
                if (string.IsNullOrWhiteSpace(inputFolder) || !Directory.Exists(inputFolder))
                {
                    WpfMessageBox.Show("Please select an input folder first", "No Input Folder", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                    return;
                }

                var dwgFiles = Directory.GetFiles(inputFolder, "*.dwg", SearchOption.TopDirectoryOnly);
                int fileCount = dwgFiles.Length;

                if (fileCount == 0)
                {
                    WpfMessageBox.Show("No DWG files found in the input folder", "No Files Found", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                    return;
                }

                TxtMaxParallel.Text = fileCount.ToString();
                LogMessage($"✅ Max parallel set to {fileCount} (matching number of DWG files in input folder)");
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error counting files: {ex.Message}", "Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            }
        }

        private void BrowseForDll(System.Windows.Controls.TextBox textBox, string title)
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = title,
                Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                textBox.Text = dialog.FileName;
            }
        }

        #endregion

        #region Settings Management

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveUserSettings();
                WpfMessageBox.Show("Settings saved successfully!", "Success", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error saving settings: {ex.Message}", "Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            }
        }

        private void SaveUserSettings()
        {
            var settings = new UserSettings
            {
                InputFolder = TxtInputFolder.Text,
                OutputFolder = TxtOutputFolder.Text,
                CsvFile = TxtCsvFile.Text,
                CrxDll = TxtCrxDll.Text,
                AutoCADPath = TxtAutoCADPath.Text,
                SelectedCommand = "GenerateScrutinyReportBatch",
                MaxParallel = int.TryParse(TxtMaxParallel.Text, out int mp) ? mp : 4,
                VerboseLogging = ChkVerbose.IsChecked ?? false
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(UserSettingsFile, json);
        }

        private void LoadUserSettings()
        {
            try
            {
                if (File.Exists(UserSettingsFile))
                {
                    var json = File.ReadAllText(UserSettingsFile);
                    var settings = JsonSerializer.Deserialize<UserSettings>(json);

                    if (settings != null)
                    {
                        TxtInputFolder.Text = settings.InputFolder ?? "";
                        TxtOutputFolder.Text = settings.OutputFolder ?? "";
                        TxtCsvFile.Text = settings.CsvFile ?? "";
                        TxtCrxDll.Text = settings.CrxDll ?? "";
                        TxtAutoCADPath.Text = settings.AutoCADPath ?? "";
                        TxtMaxParallel.Text = settings.MaxParallel.ToString();
                        ChkVerbose.IsChecked = settings.VerboseLogging;

                        LogMessage("Previous settings loaded successfully");
                    }
                }
                else
                {
                    LoadDefaultsFromAppSettings();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Could not load previous settings: {ex.Message}");
                LoadDefaultsFromAppSettings();
            }
        }

        private void LoadDefaultsFromAppSettings()
        {
            try
            {
                var appSettings = LoadAppSettings();
                if (appSettings != null)
                {
                    TxtAutoCADPath.Text = appSettings.AutoCADPath ?? "";

                    if (appSettings.DllsToLoad != null)
                    {
                        foreach (var dll in appSettings.DllsToLoad)
                        {
                            if (dll.Contains("Crx", StringComparison.OrdinalIgnoreCase))
                            {
                                TxtCrxDll.Text = dll;
                                break;
                            }
                        }
                    }

                    TxtMaxParallel.Text = appSettings.MaxParallelProcesses.ToString();
                    ChkVerbose.IsChecked = appSettings.EnableVerboseLogging;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Could not load default settings: {ex.Message}");
            }
        }

        private Configuration.BatchProcessorSettings? LoadAppSettings()
        {
            try
            {
                if (File.Exists("appsettings.json"))
                {
                    var json = File.ReadAllText("appsettings.json");
                    var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("BatchProcessorSettings", out JsonElement settingsElement))
                    {
                        return JsonSerializer.Deserialize<Configuration.BatchProcessorSettings>(settingsElement.GetRawText());
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading appsettings.json: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region Run Processing

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProcessingTask != null && !_currentProcessingTask.IsCompleted)
            {
                LogMessage("\n⚠️ Cancelling previous task...");
                TxtStatus.Text = "Cancelling...";

                _cancellationTokenSource?.Cancel();
                KillAllConsoleProcesses();

                try
                {
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
                    var completedTask = await Task.WhenAny(_currentProcessingTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        LogMessage("⚠️ Task cancellation taking longer than expected...");
                    }
                    else
                    {
                        await _currentProcessingTask;
                    }
                }
                catch (OperationCanceledException)
                {
                    LogMessage("✅ Previous task cancelled.");
                }
                catch (Exception ex)
                {
                    LogMessage($"⚠️ Error cancelling previous task: {ex.Message}");
                }
                finally
                {
                    _cancellationTokenSource?.Dispose();
                    _currentProcessingTask = null;
                    BtnRun.Content = "▶ Generate Reports";
                    TxtStatus.Text = "Processing cancelled";
                    TxtExecutionTime.Visibility = Visibility.Collapsed;
                    ClearAllFileTimers();
                    _processingStartTime = null;
                }
                return;
            }

            if (!ValidateInputs())
            {
                return;
            }

            SaveUserSettings();

            BtnRun.Content = "⏹ Stop Processing";
            TxtStatus.Text = "Processing...";
            TxtExecutionTime.Visibility = Visibility.Collapsed;
            TxtLog.Clear();
            _completedFiles = 0;
            _totalFiles = 0;
            _processingStartTime = DateTime.Now;

            try
            {
                string inputFolder = TxtInputFolder.Text;
                string outputFolder = TxtOutputFolder.Text;
                string csvFile = TxtCsvFile.Text;
                string command = "GenerateScrutinyReportBatch";
                int maxParallel = int.Parse(TxtMaxParallel.Text);
                bool verbose = ChkVerbose.IsChecked ?? false;

                var dllsToLoad = new List<string> { TxtCrxDll.Text };
                LogMessage($"✅ Crx.dll will be loaded: {Path.GetFileName(TxtCrxDll.Text)}");

                LogMessage("═══════════════════════════════════════════════════════════════");
                LogMessage($"Starting batch processing with {command}");
                LogMessage($"Input:  {inputFolder}");
                LogMessage($"Output: {outputFolder}");
                LogMessage($"CSV:    {csvFile}");
                LogMessage("═══════════════════════════════════════════════════════════════\n");

                var processor = new DrawingBatchProcessor(
                    accoreconsoleExePath: TxtAutoCADPath.Text,
                    dllsToLoad: dllsToLoad,
                    mainCommand: command,
                    maxParallelism: maxParallel,
                    tempScriptFolder: "",
                    enableVerboseLogging: verbose
                );

                var originalOut = Console.Out;
                var textBoxWriter = new TextBoxWriter(this);
                Console.SetOut(textBoxWriter);

                // Enable CSV parameter mapping if CSV file is provided
                bool csvEnabled = false;
                if (!string.IsNullOrWhiteSpace(TxtCsvFile.Text) && File.Exists(TxtCsvFile.Text))
                {
                    LogMessage($"\n📊 Enabling CSV parameter mapping...");
                    csvEnabled = processor.EnableCsvMapping(TxtCsvFile.Text);
                    if (csvEnabled)
                    {
                        LogMessage($"✅ CSV mapping enabled - each drawing will use its specific parameters");
                    }
                    else
                    {
                        LogMessage($"⚠️  CSV mapping failed - will use default config for all drawings");
                    }
                }

                _cancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = _cancellationTokenSource.Token;

                var progress = new Progress<(int completed, int total)>(update =>
                {
                    _completedFiles = update.completed;
                    _totalFiles = update.total;
                    Dispatcher.Invoke(() =>
                    {
                        TxtStatus.Text = $"Processing... {_completedFiles}/{_totalFiles} files completed";
                    });
                });

                ClearAllFileTimers();

                try
                {
                    _currentProcessingTask = processor.ProcessFolderAsync(
                        inputFolder,
                        outputFolder,
                        "",
                        cancellationToken,
                        progress);

                    var summary = await _currentProcessingTask;

                    if (summary.UIPluginLoadFailed && !string.IsNullOrWhiteSpace(TxtCrxDll.Text))
                    {
                        WpfMessageBox.Show(
                            "Crx.dll failed to load!\n\n" +
                            "This may cause commands to be unavailable.\n\n" +
                            "Please check:\n" +
                            "1. Crx.dll path is correct\n" +
                            "2. All required dependencies are available",
                            "Crx.dll Load Failed",
                            WpfMessageBoxButton.OK,
                            WpfMessageBoxImage.Warning);
                    }

                    if (_processingStartTime.HasValue)
                    {
                        var totalDuration = DateTime.Now - _processingStartTime.Value;
                        string timeString = FormatDuration(totalDuration);
                        TxtExecutionTime.Text = $"⏱️ Total Execution Time: {timeString}";
                        TxtExecutionTime.Visibility = Visibility.Visible;
                    }

                    DisplayFailedFiles(summary.FailedFiles);
                    DisplayNonProcessedFiles(summary.NonProcessedFiles, summary.NonProcessedFilesWithErrors);

                    int totalIssues = summary.FailedFiles.Count + summary.NonProcessedFiles.Count;
                    if (totalIssues == 0)
                    {
                        TxtStatus.Text = "✅ Processing complete! All files processed successfully.";
                        LogMessage("\n✅ All processing complete!");
                        WpfMessageBox.Show("Report generation completed successfully!", "Success", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                    }
                    else
                    {
                        TxtStatus.Text = $"⚠️ Processing complete! {summary.FailedFiles.Count} file(s) failed, {summary.NonProcessedFiles.Count} non-processed file(s).";
                        LogMessage($"\n⚠️ Processing complete with {summary.FailedFiles.Count} failure(s) and {summary.NonProcessedFiles.Count} non-processed file(s).");
                        WpfMessageBox.Show($"Processing completed with issues:\n\n• {summary.FailedFiles.Count} file(s) failed\n• {summary.NonProcessedFiles.Count} non-processed file(s)\n\nCheck the sections below for details.",
                            "Completed with Issues", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                    }
                }
                catch (OperationCanceledException)
                {
                    TxtStatus.Text = "❌ Processing cancelled";
                    LogMessage("\n❌ Processing was cancelled by user.");
                    BtnRun.Content = "▶ Generate Reports";

                    if (_processingStartTime.HasValue)
                    {
                        var totalDuration = DateTime.Now - _processingStartTime.Value;
                        string timeString = FormatDuration(totalDuration);
                        TxtExecutionTime.Text = $"⏱️ Execution Time (Cancelled): {timeString}";
                        TxtExecutionTime.Visibility = Visibility.Visible;
                    }
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = "Error occurred";
                    LogMessage($"\n❌ Error: {ex.Message}");
                    WpfMessageBox.Show($"Error during processing:\n{ex.Message}", "Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                    BtnRun.Content = "▶ Generate Reports";
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Dispatcher.Invoke(() =>
                    {
                        BtnRun.Content = "▶ Generate Reports";
                        if (TxtStatus.Text.Contains("Processing..."))
                        {
                            TxtStatus.Text = "Ready";
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Error occurred";
                LogMessage($"\n❌ Error: {ex.Message}");
                WpfMessageBox.Show($"Error during processing:\n{ex.Message}", "Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                BtnRun.Content = "▶ Generate Reports";
            }
            finally
            {
                _currentProcessingTask = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                BtnRun.Content = "▶ Generate Reports";
                ClearAllFileTimers();
                _processingStartTime = null;
            }
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(TxtInputFolder.Text) || !Directory.Exists(TxtInputFolder.Text))
            {
                WpfMessageBox.Show("Please select a valid input folder", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtOutputFolder.Text))
            {
                WpfMessageBox.Show("Please select an output folder", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtCsvFile.Text))
            {
                WpfMessageBox.Show("Please select a CSV parameter file", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (!File.Exists(TxtCsvFile.Text))
            {
                WpfMessageBox.Show("CSV file does not exist", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtCrxDll.Text) || !File.Exists(TxtCrxDll.Text))
            {
                WpfMessageBox.Show("Please select a valid Crx.dll file", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtAutoCADPath.Text) || !File.Exists(TxtAutoCADPath.Text))
            {
                WpfMessageBox.Show("Please select AutoCAD accoreconsole.exe", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (!int.TryParse(TxtMaxParallel.Text, out int maxParallel) || maxParallel < 1)
            {
                WpfMessageBox.Show("Max parallel processes must be at least 1", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (maxParallel > 50)
            {
                var result = WpfMessageBox.Show(
                    $"You've set {maxParallel} parallel processes. This may consume significant system resources.\n\nDo you want to continue?",
                    "High Parallelism Warning",
                    WpfMessageBoxButton.YesNo,
                    WpfMessageBoxImage.Warning);
                if (result == WpfMessageBoxResult.No)
                {
                    return false;
                }
            }

            return true;
        }

        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
            {
                return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
            }
            else if (duration.TotalMinutes >= 1)
            {
                return $"{duration.Minutes}m {duration.Seconds}s";
            }
            else
            {
                return $"{duration.Seconds}s";
            }
        }

        #endregion

        #region Failed Files Display

        private void DisplayFailedFiles(List<string> failedFiles)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => DisplayFailedFiles(failedFiles));
                return;
            }

            StkFailedFiles.Children.Clear();

            if (failedFiles == null || failedFiles.Count == 0)
            {
                GrpFailedFiles.Visibility = Visibility.Collapsed;
                return;
            }

            GrpFailedFiles.Visibility = Visibility.Visible;

            var header = new TextBlock
            {
                Text = $"❌ Total Failed Files: {failedFiles.Count}",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(5)
            };
            StkFailedFiles.Children.Add(header);

            foreach (var filePath in failedFiles)
            {
                var fileBlock = new TextBlock
                {
                    Text = $"  ❌ {Path.GetFileName(filePath)}",
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 11,
                    Margin = new Thickness(5, 2, 0, 2),
                    TextWrapping = TextWrapping.Wrap,
                    ToolTip = filePath
                };
                StkFailedFiles.Children.Add(fileBlock);
            }
        }

        private void DisplayNonProcessedFiles(List<string> nonProcessedFiles, Dictionary<string, string> nonProcessedFilesWithErrors)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => DisplayNonProcessedFiles(nonProcessedFiles, nonProcessedFilesWithErrors));
                return;
            }

            StkNonProcessedFiles.Children.Clear();

            if (nonProcessedFiles == null || nonProcessedFiles.Count == 0)
            {
                GrpNonProcessedFiles.Visibility = Visibility.Collapsed;
                return;
            }

            GrpNonProcessedFiles.Visibility = Visibility.Visible;

            var header = new TextBlock
            {
                Text = $"Total Non-Processed Files: {nonProcessedFiles.Count}",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange),
                Margin = new Thickness(0, 0, 0, 10)
            };
            StkNonProcessedFiles.Children.Add(header);

            foreach (var fileName in nonProcessedFiles)
            {
                var fileBlock = new TextBlock
                {
                    Text = $"  ⚠️ {fileName}",
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 11,
                    Margin = new Thickness(5, 2, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                };
                StkNonProcessedFiles.Children.Add(fileBlock);

                if (nonProcessedFilesWithErrors != null && nonProcessedFilesWithErrors.TryGetValue(fileName, out var errorMessage))
                {
                    var errorBlock = new TextBlock
                    {
                        Text = $"      Error: {errorMessage}",
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        FontSize = 10,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkGray),
                        Margin = new Thickness(10, 0, 0, 4),
                        TextWrapping = TextWrapping.Wrap,
                        FontStyle = FontStyles.Italic
                    };
                    StkNonProcessedFiles.Children.Add(errorBlock);
                }
            }
        }

        #endregion

        #region Logging

        public void LogMessage(string message)
        {
            if (TxtLog == null) return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => LogMessage(message)), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            try
            {
                TxtLog.AppendText(message + Environment.NewLine);
                TxtLog.ScrollToEnd();

                if (message.Contains("🔄 Starting processing:"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(message, @"Starting processing:\s*([^(]+)");
                    if (match.Success)
                    {
                        string fileName = match.Groups[1].Value.Trim();
                        StartFileTimer(fileName);
                    }
                }
                else if (message.Contains("✅ Completed processing:") || message.Contains("❌ UNHANDLED EXCEPTION processing"))
                {
                    string fileName = "";
                    if (message.Contains("Completed processing:"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(message, @"Completed processing:\s*([^(]+)");
                        if (match.Success)
                        {
                            fileName = match.Groups[1].Value.Trim();
                        }
                    }
                    else if (message.Contains("UNHANDLED EXCEPTION processing"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(message, @"UNHANDLED EXCEPTION processing\s+([^:]+)");
                        if (match.Success)
                        {
                            fileName = match.Groups[1].Value.Trim();
                        }
                    }

                    if (!string.IsNullOrEmpty(fileName))
                    {
                        StopFileTimer(fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LogMessage error: {ex.Message}");
            }
        }

        private class TextBoxWriter : System.IO.TextWriter
        {
            private ScrutinyReportsWindow _window;

            public TextBoxWriter(ScrutinyReportsWindow window)
            {
                _window = window;
            }

            public override void WriteLine(string? value)
            {
                if (value != null)
                    _window.LogMessage(value);
                else
                    _window.LogMessage("");
            }

            public override void WriteLine()
            {
                _window.LogMessage("");
            }

            public override void Write(string? value)
            {
                if (value != null)
                    _window.LogMessage(value);
            }

            public override void Write(char value)
            {
                _window.LogMessage(value.ToString());
            }

            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        }

        #endregion

        #region Process Management

        private void KillAllConsoleProcesses()
        {
            try
            {
                LogMessage("🛑 Killing all BatchProcessor and AutoCAD console processes...");

                int killedCount = 0;

                var processesToKill = Process.GetProcesses()
                    .Where(p =>
                        p.ProcessName.Equals("BatchProcessor", StringComparison.OrdinalIgnoreCase) ||
                        p.ProcessName.Equals("acad", StringComparison.OrdinalIgnoreCase) ||
                        p.ProcessName.Equals("acadConsole", StringComparison.OrdinalIgnoreCase) ||
                        p.ProcessName.Equals("accoreconsole", StringComparison.OrdinalIgnoreCase) ||
                        (p.MainWindowTitle != null && p.MainWindowTitle.Contains("AutoCAD", StringComparison.OrdinalIgnoreCase))
                    )
                    .ToList();

                foreach (var process in processesToKill)
                {
                    try
                    {
                        if (process.Id == Process.GetCurrentProcess().Id)
                            continue;

                        process.Kill();
                        killedCount++;
                        LogMessage($"   ✓ Killed process: {process.ProcessName} (PID: {process.Id})");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"   ⚠️ Could not kill process {process.ProcessName} (PID: {process.Id}): {ex.Message}");
                    }
                }

                if (killedCount > 0)
                {
                    LogMessage($"✅ Successfully killed {killedCount} process(es).");
                }
                else
                {
                    LogMessage("ℹ️ No processes found to kill.");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Error killing processes: {ex.Message}");
            }
        }

        #endregion

        #region Mode Switching

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

                    case ModeSelectionWindow.SelectedMode.JsonDiffComparison:
                        newWindow = new JsonDiffWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.RelationsCreation:
                        newWindow = new RelationsWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.ScrutinyReports:
                    default:
                        newWindow = new ScrutinyReportsWindow();
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

        #endregion
    }

    #region Settings Classes

    public class UserSettings
    {
        public string? InputFolder { get; set; }
        public string? OutputFolder { get; set; }
        public string? CsvFile { get; set; }
        public string? CrxDll { get; set; }
        public string? AutoCADPath { get; set; }
        public string? SelectedCommand { get; set; }
        public int MaxParallel { get; set; } = 4;
        public bool VerboseLogging { get; set; }
    }

    #endregion
}
