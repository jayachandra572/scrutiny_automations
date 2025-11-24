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
            LoadUserSettings();
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
                AutoCADPath = TxtAutoCADPath.Text,
                SelectedCommand = (CmbCommand.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "ProcessWithJsonBatch",
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
                string command = (CmbCommand.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "ProcessWithJsonBatch";
                int maxParallel = int.Parse(TxtMaxParallel.Text);
                bool verbose = ChkVerbose.IsChecked ?? false;

                var dllsToLoad = new List<string>
                {
                    TxtCommonUtilsDll.Text,
                    TxtNewtonsoftDll.Text,
                    TxtCrxAppDll.Text
                };

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
                Console.SetOut(new TextBoxWriter(this));

                // Run processing
                await processor.ProcessFolderAsync(inputFolder, outputFolder, configFile);

                // Restore console output
                Console.SetOut(originalOut);

                TxtStatus.Text = "Processing complete!";
                LogMessage("\n✅ All processing complete!");
                
                WpfMessageBox.Show("Batch processing completed successfully!", "Success", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Error occurred";
                LogMessage($"\n❌ Error: {ex.Message}");
                WpfMessageBox.Show($"Error during processing:\n{ex.Message}", "Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            }
            finally
            {
                BtnRun.IsEnabled = true;
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

        #region Logging

        public void LogMessage(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => LogMessage(message));
                return;
            }

            TxtLog.AppendText(message + Environment.NewLine);
            TxtLog.ScrollToEnd();
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
            }

            public override void Write(string? value)
            {
                if (value != null)
                    _window.LogMessage(value);
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
        public string? AutoCADPath { get; set; }
        public string? SelectedCommand { get; set; }
        public int MaxParallel { get; set; } = 4;
        public bool VerboseLogging { get; set; }
    }

    #endregion
}

