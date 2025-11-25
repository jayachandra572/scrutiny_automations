using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WinFormsFolderBrowser = System.Windows.Forms.FolderBrowserDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace BatchProcessor
{
    public partial class MainWindow : Window
    {
        private const string UserSettingsFile = "user_settings.json";

        public MainWindow()
        {
            InitializeComponent();
            LoadCommandsFromAppSettings();
            LoadUserSettings();
        }
        
        private void LoadCommandsFromAppSettings()
        {
            try
            {
                var appSettings = LoadAppSettings();
                if (appSettings != null && appSettings.AvailableCommands != null && appSettings.AvailableCommands.Count > 0)
                {
                    // Clear existing items
                    CmbCommand.Items.Clear();
                    
                    // Add commands from appsettings.json
                    foreach (var cmd in appSettings.AvailableCommands)
                    {
                        var item = new ComboBoxItem { Content = cmd };
                        CmbCommand.Items.Add(item);
                    }
                    
                    // Select the main command or first one
                    if (CmbCommand.Items.Count > 0)
                    {
                        bool found = false;
                        if (!string.IsNullOrEmpty(appSettings.MainCommand))
                        {
                            foreach (ComboBoxItem item in CmbCommand.Items)
                            {
                                if (item.Content.ToString() == appSettings.MainCommand)
                                {
                                    item.IsSelected = true;
                                    found = true;
                                    break;
                                }
                            }
                        }
                        
                        // If main command not found, select first item
                        if (!found)
                        {
                            CmbCommand.SelectedIndex = 0;
                        }
                    }
                }
                else
                {
                    // Fallback: add default commands if appsettings.json is missing or empty
                    CmbCommand.Items.Clear();
                    CmbCommand.Items.Add(new ComboBoxItem { Content = "RunPreScrutinyValidationsBatch", IsSelected = true });
                    CmbCommand.Items.Add(new ComboBoxItem { Content = "ExtractScrutinyMetricsBatch" });
                }
            }
            catch (Exception ex)
            {
                // Fallback: add default commands on error
                CmbCommand.Items.Clear();
                CmbCommand.Items.Add(new ComboBoxItem { Content = "RunPreScrutinyValidationsBatch", IsSelected = true });
                CmbCommand.Items.Add(new ComboBoxItem { Content = "ExtractScrutinyMetricsBatch" });
                LogMessage($"Could not load commands from appsettings.json: {ex.Message}");
            }
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

        private void BtnBrowseCommonUtils_Click(object sender, RoutedEventArgs e)
        {
            BrowseForDll(TxtCommonUtilsDll, "Select CommonUtils.dll");
        }

        private void BtnBrowseNewtonsoft_Click(object sender, RoutedEventArgs e)
        {
            BrowseForDll(TxtNewtonsoftDll, "Select Newtonsoft.Json.dll");
        }

        private void BtnBrowseCrxApp_Click(object sender, RoutedEventArgs e)
        {
            BrowseForDll(TxtCrxAppDll, "Select CrxApp.dll");
        }

        private void BtnBrowseUIPlugin_Click(object sender, RoutedEventArgs e)
        {
            BrowseForDll(TxtUIPluginDll, "Select UIPlugin.dll");
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

        private void BtnLoadDefaults_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Load from appsettings.json
                var appSettings = LoadAppSettings();
                if (appSettings != null)
                {
                    TxtAutoCADPath.Text = appSettings.AutoCADPath ?? "";
                    
                    if (appSettings.DllsToLoad != null && appSettings.DllsToLoad.Count >= 3)
                    {
                        TxtCommonUtilsDll.Text = appSettings.DllsToLoad[0];
                        TxtNewtonsoftDll.Text = appSettings.DllsToLoad[1];
                        TxtCrxAppDll.Text = appSettings.DllsToLoad[2];
                    }

                    TxtMaxParallel.Text = appSettings.MaxParallelProcesses.ToString();
                    ChkVerbose.IsChecked = appSettings.EnableVerboseLogging;

                    WpfMessageBox.Show("Default settings loaded from appsettings.json", "Success", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error loading defaults: {ex.Message}", "Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
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
                NewtonsoftDll = TxtNewtonsoftDll.Text,
                CrxAppDll = TxtCrxAppDll.Text,
                UIPluginDll = TxtUIPluginDll.Text,
                AutoCADPath = TxtAutoCADPath.Text,
                SelectedCommand = (CmbCommand.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "RunPreScrutinyValidationsBatch",
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
                        TxtNewtonsoftDll.Text = settings.NewtonsoftDll ?? "";
                        TxtCrxAppDll.Text = settings.CrxAppDll ?? "";
                        TxtUIPluginDll.Text = settings.UIPluginDll ?? "";
                        TxtAutoCADPath.Text = settings.AutoCADPath ?? "";
                        TxtMaxParallel.Text = settings.MaxParallel.ToString();
                        ChkVerbose.IsChecked = settings.VerboseLogging;

                        // Set command selection
                        foreach (ComboBoxItem item in CmbCommand.Items)
                        {
                            if (item.Content.ToString() == settings.SelectedCommand)
                            {
                                CmbCommand.SelectedItem = item;
                                break;
                            }
                        }

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
                    
                    if (appSettings.DllsToLoad != null && appSettings.DllsToLoad.Count >= 3)
                    {
                        TxtCommonUtilsDll.Text = appSettings.DllsToLoad[0];
                        TxtNewtonsoftDll.Text = appSettings.DllsToLoad[1];
                        TxtCrxAppDll.Text = appSettings.DllsToLoad[2];
                        
                        // Check if UIPlugin DLL is in the list (4th position or find by name)
                        if (appSettings.DllsToLoad.Count >= 4)
                        {
                            // Check if 4th DLL is UIPlugin
                            string dll4 = appSettings.DllsToLoad[3];
                            if (dll4.Contains("UIPlugin", StringComparison.OrdinalIgnoreCase))
                            {
                                TxtUIPluginDll.Text = dll4;
                            }
                        }
                        else
                        {
                            // Try to find UIPlugin in any position
                            foreach (var dll in appSettings.DllsToLoad)
                            {
                                if (dll.Contains("UIPlugin", StringComparison.OrdinalIgnoreCase))
                                {
                                    TxtUIPluginDll.Text = dll;
                                    break;
                                }
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
            // Validate inputs
            if (!ValidateInputs())
            {
                return;
            }

            // Save settings for next time
            SaveUserSettings();

            // Disable run button
            BtnRun.IsEnabled = false;
            TxtStatus.Text = "Processing...";
            TxtLog.Clear();

            try
            {
                // Get settings
                string inputFolder = TxtInputFolder.Text;
                string outputFolder = TxtOutputFolder.Text;
                string csvFile = TxtCsvFile.Text;
                string configFile = ""; // No config file - CSV provides all parameters
                string command = (CmbCommand.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "RunPreScrutinyValidationsBatch";
                int maxParallel = int.Parse(TxtMaxParallel.Text);
                bool verbose = ChkVerbose.IsChecked ?? false;

                var dllsToLoad = new List<string>();
                
                // Load dependencies first (CommonUtils and Newtonsoft.Json)
                dllsToLoad.Add(TxtCommonUtilsDll.Text);
                dllsToLoad.Add(TxtNewtonsoftDll.Text);
                
                // Load UIPlugin DLL (if provided) - no automatic dependency loading
                if (!string.IsNullOrWhiteSpace(TxtUIPluginDll.Text))
                {
                    dllsToLoad.Add(TxtUIPluginDll.Text);
                    LogMessage($"✅ UIPlugin.dll will be loaded: {Path.GetFileName(TxtUIPluginDll.Text)}");
                }
                
                // Load CrxApp DLL last (depends on CommonUtils and Newtonsoft.Json)
                dllsToLoad.Add(TxtCrxAppDll.Text);

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

                // Enable CSV parameter mapping if CSV file is provided
                if (!string.IsNullOrWhiteSpace(TxtCsvFile.Text) && File.Exists(TxtCsvFile.Text))
                {
                    LogMessage($"\n📊 Enabling CSV parameter mapping...");
                    bool csvEnabled = processor.EnableCsvMapping(TxtCsvFile.Text);
                    if (csvEnabled)
                    {
                        LogMessage($"✅ CSV mapping enabled - each drawing will use its specific parameters");
                    }
                    else
                    {
                        LogMessage($"⚠️  CSV mapping failed - will use default config for all drawings");
                    }
                }

                // Redirect console output to our log
                var originalOut = Console.Out;
                var textBoxWriter = new TextBoxWriter(this);
                Console.SetOut(textBoxWriter);

                try
                {
                    // Run processing
                    var summary = await processor.ProcessFolderAsync(inputFolder, outputFolder, configFile);
                    
                    // Check if UIPlugin.dll failed to load and show alert
                    if (summary.UIPluginLoadFailed && !string.IsNullOrWhiteSpace(TxtUIPluginDll.Text))
                    {
                        WpfMessageBox.Show(
                            "UIPlugin.dll failed to load!\n\n" +
                            "This may cause UIPlugin commands to be unavailable.\n\n" +
                            "Please check:\n" +
                            "1. UIPlugin.dll path is correct\n" +
                            "2. All required dependencies are available\n" +
                            "3. Check the log output for detailed error messages",
                            "UIPlugin.dll Load Failed",
                            WpfMessageBoxButton.OK,
                            WpfMessageBoxImage.Warning);
                    }

                    // Re-enable the button immediately after processing completes
                    BtnRun.IsEnabled = true;

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
                finally
                {
                    // Always restore console output
                    Console.SetOut(originalOut);
                }

            }
            catch (Exception ex)
            {
                // Re-enable button immediately on error
                BtnRun.IsEnabled = true;
                TxtStatus.Text = "Error occurred";
                LogMessage($"\n❌ Error: {ex.Message}");
                WpfMessageBox.Show($"Error during processing:\n{ex.Message}", "Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            }
            finally
            {
                // Ensure button is re-enabled (safety net in case of any edge cases)
                if (!BtnRun.IsEnabled)
                {
                    Dispatcher.Invoke(() =>
                    {
                        BtnRun.IsEnabled = true;
                    });
                }
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
            if (string.IsNullOrWhiteSpace(TxtCommonUtilsDll.Text) || !File.Exists(TxtCommonUtilsDll.Text))
            {
                WpfMessageBox.Show("Please select a valid CommonUtils.dll file", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(TxtNewtonsoftDll.Text) || !File.Exists(TxtNewtonsoftDll.Text))
            {
                WpfMessageBox.Show("Please select a valid Newtonsoft.Json.dll file", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(TxtCrxAppDll.Text) || !File.Exists(TxtCrxAppDll.Text))
            {
                WpfMessageBox.Show("Please select a valid CrxApp.dll file", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            // Validate UIPlugin DLL (optional - only if provided)
            if (!string.IsNullOrWhiteSpace(TxtUIPluginDll.Text) && !File.Exists(TxtUIPluginDll.Text))
            {
                WpfMessageBox.Show("UIPlugin.dll file does not exist. Please select a valid file or leave it empty.", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
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

            // Validate max parallel
            if (!int.TryParse(TxtMaxParallel.Text, out int maxParallel) || maxParallel < 1 || maxParallel > 32)
            {
                WpfMessageBox.Show("Max parallel processes must be between 1 and 32", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            return true;
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

            // Add header
            var header = new TextBlock
            {
                Text = $"Total Validation Failures: {failedFiles.Count}",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red),
                Margin = new Thickness(0, 0, 0, 10)
            };
            StkFailedFiles.Children.Add(header);

            // Add each failed file
            foreach (var fileName in failedFiles)
            {
                var fileBlock = new TextBlock
                {
                    Text = $"  ❌ {fileName}",
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 11,
                    Margin = new Thickness(5, 2, 0, 2),
                    TextWrapping = TextWrapping.Wrap
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
                Dispatcher.Invoke(() => LogMessage(message));
                return;
            }

            try
            {
                TxtLog.AppendText(message + Environment.NewLine);
                TxtLog.ScrollToEnd();
                TxtLog.UpdateLayout();
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
    }

    #region Settings Classes

    public class UserSettings
    {
        public string? InputFolder { get; set; }
        public string? OutputFolder { get; set; }
        public string? CsvFile { get; set; }
        public string? CommonUtilsDll { get; set; }
        public string? NewtonsoftDll { get; set; }
        public string? CrxAppDll { get; set; }
        public string? UIPluginDll { get; set; }
        public string? AutoCADPath { get; set; }
        public string? SelectedCommand { get; set; }
        public int MaxParallel { get; set; } = 4;
        public bool VerboseLogging { get; set; }
    }

    #endregion
}

