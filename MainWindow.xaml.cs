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
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WinFormsFolderBrowser = System.Windows.Forms.FolderBrowserDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace BatchProcessor
{
    public partial class MainWindow : Window
    {
        private const string UserSettingsFile = "user_settings.json";
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

        public MainWindow()
        {
            InitializeComponent();
            LoadCommandsFromAppSettings();
            LoadUserSettings();
            
            // Initialize timer for updating file timers
            _timerUpdateTimer = new System.Windows.Threading.DispatcherTimer();
            _timerUpdateTimer.Interval = TimeSpan.FromSeconds(1); // Update every second (lightweight - only updates text)
            _timerUpdateTimer.Tick += TimerUpdateTimer_Tick;
            
            // Handle window closing - shutdown app when main window closes (unless navigating back)
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
                // No active files - clear and hide the group
                StkActiveTimers.Children.Clear();
                _timerTextBlocks.Clear();
                GrpActiveTimers.Visibility = Visibility.Collapsed;
                return;
            }
            
            // Show the group
            GrpActiveTimers.Visibility = Visibility.Visible;
            
            var now = DateTime.Now;
            var sortedFiles = _fileStartTimes.OrderBy(kvp => kvp.Value).ToList();
            
            // Remove timers for files that are no longer active
            var activeFileNames = new HashSet<string>(sortedFiles.Select(kvp => kvp.Key));
            var filesToRemove = _timerTextBlocks.Keys.Where(k => !activeFileNames.Contains(k)).ToList();
            foreach (var fileName in filesToRemove)
            {
                _timerTextBlocks.Remove(fileName);
            }
            
            // Update or create timer displays
            for (int i = 0; i < sortedFiles.Count; i++)
            {
                var kvp = sortedFiles[i];
                string fileName = kvp.Key;
                DateTime startTime = kvp.Value;
                TimeSpan elapsed = now - startTime;
                
                // Format time: MM:SS or HH:MM:SS if over an hour
                string timeString;
                if (elapsed.TotalHours >= 1)
                {
                    timeString = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                }
                else
                {
                    timeString = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                }
                
                // Check if UI element already exists
                if (_timerTextBlocks.ContainsKey(fileName))
                {
                    // OPTIMIZATION: Only update the text and color (much faster than recreating UI)
                    var timerBlock = _timerTextBlocks[fileName];
                    timerBlock.Text = $"⏱️ {timeString}";
                    
                    // Update color based on elapsed time
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
                    // Create new UI element only when file is first added
                    var timerGrid = new Grid
                    {
                        Margin = new Thickness(5, 2, 0, 2)
                    };
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
                    
                    // Set initial color
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
                    
                    // Insert at correct position to maintain sort order
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
            
            // Remove UI elements for files that are no longer active
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
            
            // Start the update timer if not already running
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
            
            _timerTextBlocks.Remove(fileName); // Clean up reference
            
            // Stop the update timer if no more active files
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
            _timerTextBlocks.Clear(); // Clean up references
            _timerUpdateTimer?.Stop();
            UpdateActiveFileTimers();
        }
        
        private void LoadCommandsFromAppSettings()
        {
            // Command is fixed to "RunPreScrutinyValidationsBatch" - no need to load from settings
        }

        #region Browse Buttons

        private void BtnBrowseInput_Click(object sender, System.Windows.RoutedEventArgs e)
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

        private void BtnBrowseOutput_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialog = new WinFormsFolderBrowser
            {
                Description = "Select output folder for results",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == WinFormsDialogResult.OK)
            {
                TxtOutputFolder.Text = dialog.SelectedPath;
            }
        }

        private void BtnBrowseCsv_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "Select CSV parameter file (optional)",
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                TxtCsvFile.Text = dialog.FileName;
                LogMessage($"📊 CSV file selected: {System.IO.Path.GetFileName(dialog.FileName)}");
            }
        }

        // Only CommonUtils DLL is required for Pre Scrutiny Validations

        private void BtnBrowseCommonUtils_Click(object sender, RoutedEventArgs e)
        {
            BrowseForDll(TxtCommonUtilsDll, "Select CommonUtils.dll");
        }

        private void BtnBrowseAutoCAD_Click(object sender, System.Windows.RoutedEventArgs e)
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

        private void BtnSetMaxParallel_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                string inputFolder = TxtInputFolder.Text;
                if (string.IsNullOrWhiteSpace(inputFolder) || !Directory.Exists(inputFolder))
                {
                    WpfMessageBox.Show("Please select an input folder first", "No Input Folder", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                    return;
                }

                // Count DWG files in the input folder
                var dwgFiles = Directory.GetFiles(inputFolder, "*.dwg", SearchOption.TopDirectoryOnly);
                int fileCount = dwgFiles.Length;

                if (fileCount == 0)
                {
                    WpfMessageBox.Show("No DWG files found in the input folder", "No Files Found", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                    return;
                }

                // Set max parallel to file count
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
                CommonUtilsDll = TxtCommonUtilsDll.Text,
                AutoCADPath = TxtAutoCADPath.Text,
                SelectedCommand = "RunPreScrutinyValidationsBatch", // Fixed command
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
                        TxtCommonUtilsDll.Text = settings.CommonUtilsDll ?? "";
                        TxtAutoCADPath.Text = settings.AutoCADPath ?? "";
                        TxtMaxParallel.Text = settings.MaxParallel.ToString();
                        ChkVerbose.IsChecked = settings.VerboseLogging;
                        // Command is fixed to "RunPreScrutinyValidationsBatch" - no selection needed

                        LogMessage("Previous settings loaded successfully");
                    }
                }
                else
                {
                    // Load defaults from appsettings.json
                    LoadDefaultsFromAppSettings();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Could not load previous settings: {ex.Message}");
                LoadDefaultsFromAppSettings();
                LoadCommandsFromAppSettings();
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
                    
                    // Load CommonUtils DLL from appsettings.json
                    if (appSettings.DllsToLoad != null)
                    {
                        // Find CommonUtils DLL in the list
                        foreach (var dll in appSettings.DllsToLoad)
                        {
                            if (dll.Contains("CommonUtils", StringComparison.OrdinalIgnoreCase))
                            {
                                TxtCommonUtilsDll.Text = dll;
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
            // If a task is already running, cancel it
            if (_currentProcessingTask != null && !_currentProcessingTask.IsCompleted)
            {
                LogMessage("\n⚠️ Cancelling previous task...");
                TxtStatus.Text = "Cancelling...";
                
                // Cancel the token
                _cancellationTokenSource?.Cancel();
                
                // Kill all console and AutoCAD processes
                KillAllConsoleProcesses();
                
                try
                {
                    // Wait for task to complete with timeout
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
                    BtnRun.Content = "▶ Run Pre Scrutiny Validations";
                    TxtStatus.Text = "Processing cancelled";
                    TxtExecutionTime.Visibility = Visibility.Collapsed;
                    TxtExecutionTime.Text = "";
                    ClearAllFileTimers();
                    _processingStartTime = null;
                }
                return; // Exit after cancelling
            }

            // Validate inputs
            if (!ValidateInputs())
            {
                return;
            }

            // Save settings for next time
            SaveUserSettings();

            // Keep run button enabled - user can click again to cancel
            // BtnRun.IsEnabled = false; // REMOVED - keep button enabled
            BtnRun.Content = "⏹ Stop Processing";
            TxtStatus.Text = "Processing...";
            TxtExecutionTime.Visibility = Visibility.Collapsed;
            TxtExecutionTime.Text = "";
            TxtLog.Clear();
            _completedFiles = 0;
            _totalFiles = 0;
            _processingStartTime = DateTime.Now;

            try
            {
                // Get settings
                string inputFolder = TxtInputFolder.Text;
                string outputFolder = TxtOutputFolder.Text;
                string csvFile = TxtCsvFile.Text;
                string configFile = ""; // No config file - CSV provides all parameters
                // Fixed command for Pre Scrutiny Validations
                string command = "RunPreScrutinyValidationsBatch";
                int maxParallel = int.Parse(TxtMaxParallel.Text);
                bool verbose = ChkVerbose.IsChecked ?? false;

                var dllsToLoad = new List<string>();
                
                // Only load CommonUtils DLL (required)
                dllsToLoad.Add(TxtCommonUtilsDll.Text);
                LogMessage($"✅ CommonUtils.dll will be loaded: {Path.GetFileName(TxtCommonUtilsDll.Text)}");

                LogMessage("═══════════════════════════════════════════════════════════════");
                LogMessage($"Starting batch processing with {command}");
                LogMessage($"Input:  {inputFolder}");
                LogMessage($"Output: {outputFolder}");
                LogMessage($"CSV:    {csvFile}");
                LogMessage($"Mode:   CSV-based parameter mapping");
                LogMessage("═══════════════════════════════════════════════════════════════\n");

                // Create processor
                var processor = new DrawingBatchProcessor(
                    accoreconsoleExePath: TxtAutoCADPath.Text,
                    dllsToLoad: dllsToLoad,
                    mainCommand: command,
                    maxParallelism: maxParallel,
                    tempScriptFolder: "",
                    enableVerboseLogging: verbose
                );

                // Redirect console output to our log (must be before CSV validation)
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
                        
                        // Validate that all drawings have parameters before processing
                        LogMessage($"\n🔍 Validating drawings have parameters in CSV...");
                        var missingParameterFiles = processor.ValidateDrawingsHaveParameters(inputFolder);
                        
                        if (missingParameterFiles.Count > 0)
                        {
                            LogMessage($"⚠️  Found {missingParameterFiles.Count} drawing file(s) without parameters in CSV:");
                            foreach (var file in missingParameterFiles)
                            {
                                LogMessage($"   - {file}");
                            }
                            
                            // Display missing parameter files
                            DisplayMissingParameterFiles(missingParameterFiles);
                            
                            // Restore console output temporarily for message box
                            Console.SetOut(originalOut);
                            
                            // Ask user if they want to continue
                            var result = WpfMessageBox.Show(
                                $"⚠️ Found {missingParameterFiles.Count} drawing file(s) without parameters in CSV.\n\n" +
                                "These files will be skipped during processing.\n\n" +
                                "Do you want to continue processing the remaining files?",
                                "Missing Parameters",
                                WpfMessageBoxButton.YesNo,
                                WpfMessageBoxImage.Warning);
                            
                            // Restore text box writer
                            Console.SetOut(textBoxWriter);
                            
                if (result == MessageBoxResult.No)
                {
                    LogMessage("\n❌ Processing cancelled by user due to missing parameters.");
                    BtnRun.Content = "▶ Run Pre Scrutiny Validations";
                    TxtStatus.Text = "Processing cancelled - files missing parameters";
                    Console.SetOut(originalOut);
                    _processingStartTime = null;
                    return;
                }
                            
                            LogMessage("\n✅ Continuing with processing (files without parameters will be skipped)...");
                        }
                        else
                        {
                            LogMessage($"✅ All {Directory.GetFiles(inputFolder, "*.dwg").Length} drawing file(s) have parameters in CSV.");
                            // Hide missing parameters section if no missing files
                            Dispatcher.Invoke(() =>
                            {
                                GrpMissingParameters.Visibility = Visibility.Collapsed;
                            });
                        }
                    }
                    else
                    {
                        LogMessage($"⚠️  CSV mapping failed - will use default config for all drawings");
                    }
                }
                else
                {
                    // Hide missing parameters section if CSV is not used
                    Dispatcher.Invoke(() =>
                    {
                        GrpMissingParameters.Visibility = Visibility.Collapsed;
                    });
                }

                // Create cancellation token source
                _cancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = _cancellationTokenSource.Token;

                // Create progress reporter
                var progress = new Progress<(int completed, int total)>(update =>
                {
                    _completedFiles = update.completed;
                    _totalFiles = update.total;
                    Dispatcher.Invoke(() =>
                    {
                        TxtStatus.Text = $"Processing... {_completedFiles}/{_totalFiles} files completed";
                    });
                });
                
                // Clear timers when starting new batch
                ClearAllFileTimers();

                try
                {
                    // Run processing with cancellation and progress
                    _currentProcessingTask = processor.ProcessFolderAsync(
                        inputFolder, 
                        outputFolder, 
                        configFile, 
                        cancellationToken, 
                        progress);
                    
                    var summary = await _currentProcessingTask;
                    
                    // Check if CommonUtils.dll failed to load and show alert
                    if (summary.UIPluginLoadFailed && !string.IsNullOrWhiteSpace(TxtCommonUtilsDll.Text))
                    {
                        WpfMessageBox.Show(
                            "CommonUtils.dll failed to load!\n\n" +
                            "This may cause commands to be unavailable.\n\n" +
                            "Please check:\n" +
                            "1. CommonUtils.dll path is correct\n" +
                            "2. All required dependencies are available\n" +
                            "3. Check the log output for detailed error messages",
                            "CommonUtils.dll Load Failed",
                            WpfMessageBoxButton.OK,
                            WpfMessageBoxImage.Warning);
                    }

                    // Button stays enabled, just update status
                    // BtnRun.IsEnabled = true; // REMOVED - button stays enabled

                    // Calculate and display total execution time
                    if (_processingStartTime.HasValue)
                    {
                        var totalDuration = DateTime.Now - _processingStartTime.Value;
                        string timeString = FormatDuration(totalDuration);
                        TxtExecutionTime.Text = $"⏱️ Total Execution Time: {timeString}";
                        TxtExecutionTime.Visibility = Visibility.Visible;
                    }

                    // Display failed files and non-processed files
                    DisplayFailedFiles(summary.FailedFiles);
                    DisplayNonProcessedFiles(summary.NonProcessedFiles, summary.NonProcessedFilesWithErrors);

                    // Determine if this is a report generation command
                    bool isReportGenerationCommand = (command.Contains("GenerateScrutinyReportBatch", StringComparison.OrdinalIgnoreCase) ||
                                                      (command.Contains("Generate", StringComparison.OrdinalIgnoreCase) && 
                                                       command.Contains("Report", StringComparison.OrdinalIgnoreCase)));
                    
                    int totalIssues = summary.FailedFiles.Count + summary.NonProcessedFiles.Count;
                    if (totalIssues == 0)
                    {
                        TxtStatus.Text = "✅ Processing complete! All files processed successfully.";
                        LogMessage("\n✅ All processing complete!");
                        WpfMessageBox.Show("Batch processing completed successfully!", "Success", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                    }
                    else
                    {
                        if (isReportGenerationCommand)
                        {
                            TxtStatus.Text = $"⚠️ Processing complete! {summary.FailedFiles.Count} file(s) failed (no output created), {summary.NonProcessedFiles.Count} non-processed file(s).";
                            LogMessage($"\n⚠️ Processing complete with {summary.FailedFiles.Count} file(s) that did not generate output and {summary.NonProcessedFiles.Count} non-processed file(s).");
                            WpfMessageBox.Show($"Processing completed with issues:\n\n• {summary.FailedFiles.Count} file(s) failed to generate output\n• {summary.NonProcessedFiles.Count} non-processed file(s) (errors before processing)\n\nCheck the sections below for details.", 
                                "Completed with Issues", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                        }
                        else
                        {
                            TxtStatus.Text = $"⚠️ Processing complete! {summary.FailedFiles.Count} validation failure(s), {summary.NonProcessedFiles.Count} non-processed file(s).";
                            LogMessage($"\n⚠️ Processing complete with {summary.FailedFiles.Count} validation failure(s) and {summary.NonProcessedFiles.Count} non-processed file(s).");
                            WpfMessageBox.Show($"Processing completed with issues:\n\n• {summary.FailedFiles.Count} validation failure(s) (JSON created)\n• {summary.NonProcessedFiles.Count} non-processed file(s) (errors before processing)\n\nCheck the sections below for details.", 
                                "Completed with Issues", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    TxtStatus.Text = "❌ Processing cancelled";
                    LogMessage("\n❌ Processing was cancelled by user.");
                    BtnRun.Content = "▶ Run Pre Scrutiny Validations";
                    
                    // Show execution time even if cancelled
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
                    // Button stays enabled
                    TxtStatus.Text = "Error occurred";
                    LogMessage($"\n❌ Error: {ex.Message}");
                    WpfMessageBox.Show($"Error during processing:\n{ex.Message}", "Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                    BtnRun.Content = "▶ Run Pre Scrutiny Validations";
                }
                finally
                {
                    // Always restore console output
                    Console.SetOut(originalOut);
                    
                    // Reset button state on UI thread
                    Dispatcher.Invoke(() =>
                    {
                        BtnRun.Content = "▶ Run Pre Scrutiny Validations";
                        if (TxtStatus.Text.Contains("Processing..."))
                        {
                            TxtStatus.Text = "Ready";
                        }
                    });
                }

            }
            catch (Exception ex)
            {
                // Button stays enabled
                TxtStatus.Text = "Error occurred";
                LogMessage($"\n❌ Error: {ex.Message}");
                WpfMessageBox.Show($"Error during processing:\n{ex.Message}", "Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                BtnRun.Content = "▶ Run Pre Scrutiny Validations";
            }
            finally
            {
                // Clean up
                _currentProcessingTask = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                BtnRun.Content = "▶ Run Pre Scrutiny Validations";
                ClearAllFileTimers();
                _processingStartTime = null;
            }
        }

        private bool ValidateInputs()
        {
            // Validate input folder
            if (string.IsNullOrWhiteSpace(TxtInputFolder.Text))
            {
                WpfMessageBox.Show("Please select an input folder", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }
            if (!Directory.Exists(TxtInputFolder.Text))
            {
                WpfMessageBox.Show("Input folder does not exist", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            // Validate output folder
            if (string.IsNullOrWhiteSpace(TxtOutputFolder.Text))
            {
                WpfMessageBox.Show("Please select an output folder", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            // Validate CSV file
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

            // Validate DLLs
            // Validate CommonUtils DLL (required)
            if (string.IsNullOrWhiteSpace(TxtCommonUtilsDll.Text) || !File.Exists(TxtCommonUtilsDll.Text))
            {
                WpfMessageBox.Show("Please select a valid CommonUtils.dll file", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            // Validate AutoCAD path
            if (string.IsNullOrWhiteSpace(TxtAutoCADPath.Text))
            {
                WpfMessageBox.Show("Please select AutoCAD accoreconsole.exe", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }
            if (!File.Exists(TxtAutoCADPath.Text))
            {
                WpfMessageBox.Show("AutoCAD accoreconsole.exe not found", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            // Validate max parallel (allow up to 100, or unlimited if user wants)
            if (!int.TryParse(TxtMaxParallel.Text, out int maxParallel) || maxParallel < 1)
            {
                WpfMessageBox.Show("Max parallel processes must be at least 1", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }
            
            // Warn if very high (but allow it)
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

        #region Missing Parameters Display

        private void DisplayMissingParameterFiles(List<string> missingParameterFiles)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => DisplayMissingParameterFiles(missingParameterFiles));
                return;
            }

            // Clear previous missing parameter files
            StkMissingParameters.Children.Clear();

            if (missingParameterFiles == null || missingParameterFiles.Count == 0)
            {
                GrpMissingParameters.Visibility = Visibility.Collapsed;
                return;
            }

            // Show the missing parameters section
            GrpMissingParameters.Visibility = Visibility.Visible;

            // Add header
            var header = new TextBlock
            {
                Text = $"Total Files Without Parameters: {missingParameterFiles.Count}",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange),
                Margin = new Thickness(0, 0, 0, 10)
            };
            StkMissingParameters.Children.Add(header);

            // Add each missing parameter file
            foreach (var fileName in missingParameterFiles)
            {
                var fileBlock = new TextBlock
                {
                    Text = $"  📋 {fileName}",
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 11,
                    Margin = new Thickness(5, 2, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                };
                StkMissingParameters.Children.Add(fileBlock);
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

            // Clear previous failed files
            StkFailedFiles.Children.Clear();

            if (failedFiles == null || failedFiles.Count == 0)
            {
                GrpFailedFiles.Visibility = Visibility.Collapsed;
                return;
            }

            // Show the failed files section
            GrpFailedFiles.Visibility = Visibility.Visible;

            // Add header with count
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

            // Add separator
            var separator = new Border
            {
                Height = 1,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGray),
                Margin = new Thickness(0, 0, 0, 10)
            };
            StkFailedFiles.Children.Add(separator);

            // Add each failed file with better formatting and copy functionality
            foreach (var filePath in failedFiles)
            {
                // Extract just the file name from the full path
                string fileName = Path.GetFileName(filePath);
                
                var fileGrid = new Grid
                {
                    Margin = new Thickness(5, 3, 5, 3)
                };
                fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var iconBlock = new TextBlock
                {
                    Text = "❌",
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(iconBlock, 0);

                // Display just the file name, but store full path for copying
                var fileNameBox = new System.Windows.Controls.TextBox
                {
                    Text = fileName,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkRed),
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    IsReadOnly = true,
                    Cursor = System.Windows.Input.Cursors.IBeam,
                    Padding = new Thickness(2),
                    Margin = new Thickness(0, 0, 5, 0),
                    ToolTip = $"Full path: {filePath}" // Show full path in tooltip
                };
                Grid.SetColumn(fileNameBox, 1);

                // Add copy button - copies full path to clipboard
                var copyButton = new System.Windows.Controls.Button
                {
                    Content = "📋",
                    Width = 30,
                    Height = 25,
                    FontSize = 12,
                    Padding = new Thickness(0),
                    Margin = new Thickness(5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Copy full file path to clipboard",
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                copyButton.Click += (s, e) =>
                {
                    try
                    {
                        System.Windows.Clipboard.SetText(filePath); // Copy full path
                        copyButton.Content = "✓";
                        copyButton.Foreground = System.Windows.Media.Brushes.Green;
                        System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(2)
                        };
                        timer.Tick += (sender, args) =>
                        {
                            copyButton.Content = "📋";
                            copyButton.Foreground = System.Windows.Media.Brushes.Black;
                            timer.Stop();
                        };
                        timer.Start();
                    }
                    catch (Exception ex)
                    {
                        WpfMessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Copy Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                    }
                };
                Grid.SetColumn(copyButton, 2);

                fileGrid.Children.Add(iconBlock);
                fileGrid.Children.Add(fileNameBox);
                fileGrid.Children.Add(copyButton);
                StkFailedFiles.Children.Add(fileGrid);
            }
        }

        private void DisplayNonProcessedFiles(List<string> nonProcessedFiles, Dictionary<string, string> nonProcessedFilesWithErrors)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => DisplayNonProcessedFiles(nonProcessedFiles, nonProcessedFilesWithErrors));
                return;
            }

            // Clear previous non-processed files
            StkNonProcessedFiles.Children.Clear();

            if (nonProcessedFiles == null || nonProcessedFiles.Count == 0)
            {
                GrpNonProcessedFiles.Visibility = Visibility.Collapsed;
                return;
            }

            // Show the non-processed files section
            GrpNonProcessedFiles.Visibility = Visibility.Visible;

            // Add header
            var header = new TextBlock
            {
                Text = $"Total Non-Processed Files: {nonProcessedFiles.Count}",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange),
                Margin = new Thickness(0, 0, 0, 10)
            };
            StkNonProcessedFiles.Children.Add(header);

            // Add each non-processed file with error message
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

                // Add error message if available
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
                // Use BeginInvoke instead of Invoke to avoid blocking - this prevents UI freezing
                Dispatcher.BeginInvoke(new Action(() => LogMessage(message)), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            try
            {
                TxtLog.AppendText(message + Environment.NewLine);
                TxtLog.ScrollToEnd();
                // Removed UpdateLayout() - it's expensive and causes UI blocking
                // ScrollToEnd() is sufficient for scrolling
                
                // Monitor log messages to track file processing start/end for timers
                if (message.Contains("🔄 Starting processing:"))
                {
                    // Extract filename from message like "🔄 Starting processing: filename (1/10)"
                    var match = System.Text.RegularExpressions.Regex.Match(message, @"Starting processing:\s*([^(]+)");
                    if (match.Success)
                    {
                        string fileName = match.Groups[1].Value.Trim();
                        StartFileTimer(fileName);
                    }
                }
                else if (message.Contains("✅ Completed processing:") || message.Contains("❌ UNHANDLED EXCEPTION processing"))
                {
                    // Extract filename from completion message
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
                // Fallback: try to write to console if TextBox fails
                System.Diagnostics.Debug.WriteLine($"LogMessage error: {ex.Message}");
            }
        }

        // Custom TextWriter to redirect Console.WriteLine to our TextBox
        private class TextBoxWriter : System.IO.TextWriter
        {
            private MainWindow _window;

            public TextBoxWriter(MainWindow window)
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
                
                // Get all processes to kill
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
                        // Skip the current process (this application)
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
            // Set flag to prevent shutdown
            _isNavigatingBack = true;
            
            // Go back to mode selection window
            var modeSelectionWindow = new ModeSelectionWindow();
            
            // Set mode selection as MainWindow temporarily to prevent shutdown
            System.Windows.Application.Current.MainWindow = modeSelectionWindow;
            
            // Close current window
            this.Close();
            
            // Show mode selection window
            if (modeSelectionWindow.ShowDialog() == true && modeSelectionWindow.IsModeSelected)
            {
                Window newWindow;

                switch (modeSelectionWindow.Mode)
                {
                    case ModeSelectionWindow.SelectedMode.JsonDiffComparison:
                        newWindow = new JsonDiffWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.RelationsCreation:
                        newWindow = new RelationsWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.CommandsExecution:
                    default:
                        newWindow = new MainWindow();
                        break;
                }

                // Update MainWindow property
                System.Windows.Application.Current.MainWindow = newWindow;
                
                // Ensure window is enabled and visible
                newWindow.IsEnabled = true;
                newWindow.Visibility = Visibility.Visible;
                newWindow.WindowState = WindowState.Normal;
                
                // Show and activate (Closing event handler is set in constructor)
                newWindow.Show();
                newWindow.Activate();
                newWindow.Focus();
            }
            else
            {
                // If no mode selected, shutdown
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
        public string? CommonUtilsDll { get; set; }
        public string? AutoCADPath { get; set; }
        public string? SelectedCommand { get; set; }
        public int MaxParallel { get; set; } = 4;
        public bool VerboseLogging { get; set; }
    }

    #endregion
}

