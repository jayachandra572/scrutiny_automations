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
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WinFormsFolderBrowser = System.Windows.Forms.FolderBrowserDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace BatchProcessor
{
    public partial class RelationsWindow : Window
    {
        private const string UserSettingsFile = "user_settings_relations.json";
        private bool _isNavigatingBack = false;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _currentProcessingTask;
        private AutoCADGuiProcessor? _processor;

        public RelationsWindow()
        {
            InitializeComponent();
            LoadUserSettings();
            
            // Handle window closing
            this.Closing += (s, args) =>
            {
                if (!_isNavigatingBack && System.Windows.Application.Current.MainWindow == this)
                {
                    // Cancel processing if running
                    _cancellationTokenSource?.Cancel();
                    System.Windows.Application.Current.Shutdown();
                }
            };
        }

        #region Browse Buttons

        private void BtnBrowseUIPlugin_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "Select UIPlugin.dll",
                Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                TxtUIPluginDll.Text = dialog.FileName;
            }
        }

        private void BtnBrowseAutoCAD_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "Select AutoCAD executable (acad.exe)",
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true,
                FileName = "acad.exe"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtAutoCADExe.Text = dialog.FileName;
            }
        }

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

        #endregion

        #region Processing

        private async void BtnStartProcessing_Click(object sender, RoutedEventArgs e)
        {
            // Validate inputs
            if (!ValidateInputs())
            {
                return;
            }

            // Save settings
            SaveUserSettings();

            // Disable start button, enable stop button
            BtnStartProcessing.Visibility = Visibility.Collapsed;
            BtnStopProcessing.Visibility = Visibility.Visible;
            BtnStartProcessing.IsEnabled = false;

            // Clear log
            TxtLog.Clear();
            ProgressBar.Value = 0;

            // Create cancellation token
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            // Get settings
            string uiPluginDll = TxtUIPluginDll.Text;
            string autocadExe = TxtAutoCADExe.Text;
            string inputFolder = TxtInputFolder.Text;

            // Get all DWG files
            string[] dwgFiles;
            try
            {
                dwgFiles = Directory.GetFiles(inputFolder, "*.dwg", SearchOption.TopDirectoryOnly);
                if (dwgFiles.Length == 0)
                {
                    WpfMessageBox.Show("No DWG files found in the input folder.", "No Files Found", 
                        WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                    ResetUI();
                    return;
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error reading input folder: {ex.Message}", "Error", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                ResetUI();
                return;
            }

            LogMessage("═══════════════════════════════════════════════════════════════");
            LogMessage("Starting CREATE_RELATIONS_FOR_ENTITIES processing...");
            LogMessage($"UIPlugin DLL: {Path.GetFileName(uiPluginDll)}");
            LogMessage($"AutoCAD EXE:  {Path.GetFileName(autocadExe)}");
            LogMessage($"Input Folder: {inputFolder}");
            LogMessage($"Total Files:  {dwgFiles.Length}");
            LogMessage("═══════════════════════════════════════════════════════════════\n");

            // Create processor
            _processor = new AutoCADGuiProcessor(autocadExe, uiPluginDll);

            // Start processing task
            _currentProcessingTask = ProcessFilesSequentiallyAsync(dwgFiles, cancellationToken);
            
            try
            {
                await _currentProcessingTask;
                
                LogMessage("\n✅ All processing complete!");
                TxtStatus.Text = "✅ Processing complete!";
                WpfMessageBox.Show("Processing completed successfully!", "Success", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                LogMessage("\n⚠️ Processing was cancelled by user.");
                TxtStatus.Text = "Processing cancelled";
            }
            catch (Exception ex)
            {
                LogMessage($"\n❌ Error during processing: {ex.Message}");
                TxtStatus.Text = "Error occurred";
                WpfMessageBox.Show($"Error during processing:\n{ex.Message}", "Error", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            }
            finally
            {
                ResetUI();
            }
        }

        private void BtnStopProcessing_Click(object sender, RoutedEventArgs e)
        {
            LogMessage("\n⚠️ Cancelling processing...");
            _cancellationTokenSource?.Cancel();
            BtnStopProcessing.IsEnabled = false;
        }

        private async Task ProcessFilesSequentiallyAsync(string[] dwgFiles, CancellationToken cancellationToken)
        {
            int totalFiles = dwgFiles.Length;

            // Update progress UI
            Dispatcher.Invoke(() =>
            {
                TxtCurrentFile.Text = $"Preparing to process {totalFiles} file(s)...";
                TxtProgress.Text = $"0/{totalFiles}";
                ProgressBar.Value = 0;
                TxtStatus.Text = "Starting batch processing in single AutoCAD session...";
            });

            LogMessage($"\n{new string('═', 80)}");
            LogMessage($"Starting batch processing: {totalFiles} file(s)");
            LogMessage($"AutoCAD will open once and process all files in sequence");
            LogMessage($"{new string('═', 80)}");

            try
            {
                // Process all files in a single AutoCAD session
                var progress = new Progress<(int current, int total, string currentFile)>(update =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtCurrentFile.Text = string.IsNullOrEmpty(update.currentFile) 
                            ? $"Processing file {update.current} of {update.total}..." 
                            : $"Processing: {update.currentFile} ({update.current}/{update.total})";
                        TxtProgress.Text = $"{update.current}/{update.total}";
                        ProgressBar.Value = update.total > 0 ? (update.current * 100.0) / update.total : 0;
                        TxtStatus.Text = $"Processing file {update.current} of {update.total}...";
                    });
                });

                var results = await _processor!.ProcessAllDrawingsAsync(
                    dwgFiles, 
                    cancellationToken, 
                    (message) => LogMessage(message),
                    progress);

                // Update final progress
                Dispatcher.Invoke(() =>
                {
                    TxtCurrentFile.Text = $"Completed: {results.ProcessedFiles.Count} successful, {results.FailedFiles.Count} failed";
                    TxtProgress.Text = $"{results.ProcessedFiles.Count}/{totalFiles}";
                    ProgressBar.Value = 100;
                    TxtStatus.Text = "Processing complete!";
                });

                // Log summary
                LogMessage($"\n{new string('═', 80)}");
                LogMessage($"Summary: {results.ProcessedFiles.Count} successful, {results.FailedFiles.Count} failed out of {totalFiles} total");
                
                if (results.ProcessedFiles.Count > 0)
                {
                    LogMessage($"\n✅ Successfully processed files:");
                    foreach (var file in results.ProcessedFiles)
                    {
                        LogMessage($"   - {file}");
                    }
                }

                if (results.FailedFiles.Count > 0)
                {
                    LogMessage($"\n❌ Failed files:");
                    foreach (var file in results.FailedFiles)
                    {
                        LogMessage($"   - {file}");
                    }
                }

                LogMessage($"{new string('═', 80)}");
            }
            catch (OperationCanceledException)
            {
                LogMessage($"\n⚠️ Processing was cancelled");
                Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = "Processing cancelled";
                });
                throw;
            }
            catch (Exception ex)
            {
                LogMessage($"\n❌ Error during batch processing: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = "Error occurred";
                });
                throw;
            }
        }

        private void ResetUI()
        {
            Dispatcher.Invoke(() =>
            {
                BtnStartProcessing.Visibility = Visibility.Visible;
                BtnStopProcessing.Visibility = Visibility.Collapsed;
                BtnStartProcessing.IsEnabled = true;
                BtnStopProcessing.IsEnabled = true;
            });
        }

        #endregion

        #region Validation

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(TxtUIPluginDll.Text))
            {
                WpfMessageBox.Show("Please select UIPlugin.dll file", "Validation Error", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (!File.Exists(TxtUIPluginDll.Text))
            {
                WpfMessageBox.Show("UIPlugin.dll file does not exist", "Validation Error", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtAutoCADExe.Text))
            {
                WpfMessageBox.Show("Please select AutoCAD executable (acad.exe)", "Validation Error", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (!File.Exists(TxtAutoCADExe.Text))
            {
                WpfMessageBox.Show("AutoCAD executable does not exist", "Validation Error", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtInputFolder.Text))
            {
                WpfMessageBox.Show("Please select an input folder", "Validation Error", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (!Directory.Exists(TxtInputFolder.Text))
            {
                WpfMessageBox.Show("Input folder does not exist", "Validation Error", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        #endregion

        #region Settings

        private void SaveUserSettings()
        {
            try
            {
                var settings = new RelationsUserSettings
                {
                    UIPluginDll = TxtUIPluginDll.Text,
                    AutoCADExe = TxtAutoCADExe.Text,
                    InputFolder = TxtInputFolder.Text
                };

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(UserSettingsFile, json);
            }
            catch (Exception ex)
            {
                LogMessage($"Could not save settings: {ex.Message}");
            }
        }

        private void LoadUserSettings()
        {
            try
            {
                if (File.Exists(UserSettingsFile))
                {
                    var json = File.ReadAllText(UserSettingsFile);
                    var settings = JsonSerializer.Deserialize<RelationsUserSettings>(json);

                    if (settings != null)
                    {
                        TxtUIPluginDll.Text = settings.UIPluginDll ?? "";
                        TxtAutoCADExe.Text = settings.AutoCADExe ?? "";
                        TxtInputFolder.Text = settings.InputFolder ?? "";
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Could not load settings: {ex.Message}");
            }
        }

        private class RelationsUserSettings
        {
            public string? UIPluginDll { get; set; }
            public string? AutoCADExe { get; set; }
            public string? InputFolder { get; set; }
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LogMessage error: {ex.Message}");
            }
        }

        #endregion

        #region Mode Switching

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            // Cancel processing if running
            _cancellationTokenSource?.Cancel();

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
                    case ModeSelectionWindow.SelectedMode.RelationsCreation:
                        newWindow = new RelationsWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.JsonDiffComparison:
                        newWindow = new JsonDiffWindow();
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
                
                // Show and activate
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
}

