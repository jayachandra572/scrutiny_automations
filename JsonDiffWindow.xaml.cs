using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BatchProcessor.Models;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WinFormsFolderBrowser = System.Windows.Forms.FolderBrowserDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace BatchProcessor
{
    public partial class JsonDiffWindow : Window
    {
        private const string UserSettingsFile = "user_settings_diff.json";
        private JsonDiffComparer? _comparer;
        private DiffReportExporter? _exporter;
        private bool _isNavigatingBack = false;

        public JsonDiffWindow()
        {
            InitializeComponent();
            _comparer = new JsonDiffComparer();
            _exporter = new DiffReportExporter();
            LoadUserSettings();
            
            // Handle window closing - shutdown app when main window closes (unless navigating back)
            this.Closing += (s, args) =>
            {
                if (!_isNavigatingBack && System.Windows.Application.Current.MainWindow == this)
                {
                    System.Windows.Application.Current.Shutdown();
                }
            };
        }

        #region Browse Buttons

        private void BtnBrowseReference_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WinFormsFolderBrowser
            {
                Description = "Select reference folder (baseline JSON files)",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == WinFormsDialogResult.OK)
            {
                TxtReferenceFolder.Text = dialog.SelectedPath;
            }
        }

        private void BtnBrowseLatest_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WinFormsFolderBrowser
            {
                Description = "Select latest folder (current JSON files)",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == WinFormsDialogResult.OK)
            {
                TxtLatestFolder.Text = dialog.SelectedPath;
            }
        }

        private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WinFormsFolderBrowser
            {
                Description = "Select output folder for diff reports",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == WinFormsDialogResult.OK)
            {
                TxtOutputFolder.Text = dialog.SelectedPath;
            }
        }

        #endregion

        #region Compare

        private async void BtnCompare_Click(object sender, RoutedEventArgs e)
        {
            // Ensure we're on UI thread (should always be true for button click)
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => BtnCompare_Click(sender, e));
                return;
            }

            try
            {
                if (!ValidateInputs())
                {
                    return;
                }

                SaveUserSettings();

                BtnCompare.IsEnabled = false;
                TxtLog.Clear();
                TreeViewResults.Items.Clear();
                TxtEmptyState.Visibility = Visibility.Visible;
                PanelSavedFiles.Visibility = Visibility.Collapsed;

                // Capture folder paths on UI thread before starting background task
                string referenceFolder = TxtReferenceFolder.Text;
                string latestFolder = TxtLatestFolder.Text;
                string outputFolder = TxtOutputFolder.Text;

                LogMessage("═══════════════════════════════════════════════════════════════");
                LogMessage("Starting JSON diff comparison...");
                LogMessage($"Reference: {referenceFolder}");
                LogMessage($"Latest:    {latestFolder}");
                LogMessage("═══════════════════════════════════════════════════════════════\n");

                DiffReport? report = null;
                string? errorMessage = null;

                await Task.Run(() =>
                {
                    try
                    {
                        // Update UI directly via Dispatcher - LogMessage will handle thread check internally
                        Dispatcher.Invoke(() => 
                        {
                            try
                            {
                                if (TxtLog != null)
                                {
                                    TxtLog.AppendText("Scanning folders and comparing files...\n");
                                    TxtLog.ScrollToEnd();
                                }
                            }
                            catch (Exception logEx)
                            {
                                // Log to debug output if UI logging fails
                                System.Diagnostics.Debug.WriteLine($"Log update failed: {logEx.Message}");
                            }
                        });

                        report = _comparer!.CompareFolders(
                            referenceFolder,
                            latestFolder,
                            outputFolder
                        );
                    }
                    catch (Exception ex)
                    {
                        // Extract error message as string on background thread
                        // This prevents threading issues when accessing exception properties
                        try
                        {
                            // Get exception type and message safely
                            string exType = ex.GetType().Name;
                            string exMsg = ex.Message ?? "Unknown error occurred";
                            errorMessage = $"{exType}: {exMsg}";
                            
                            // Include inner exception if present
                            if (ex.InnerException != null)
                            {
                                try
                                {
                                    errorMessage += $"\nInner Exception: {ex.InnerException.Message}";
                                }
                                catch
                                {
                                    // Ignore if we can't access inner exception
                                }
                            }
                        }
                        catch
                        {
                            // If we can't even access the message, use a generic error
                            errorMessage = "An error occurred during comparison. Please check the folders and try again.";
                        }
                    }
                });

                // Check if exception occurred during comparison
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    try
                    {
                        LogMessage($"\n❌ Error: {errorMessage}");
                        WpfMessageBox.Show($"Error during comparison:\n{errorMessage}", "Error", 
                            WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                    }
                    catch (Exception uiEx)
                    {
                        // If UI update fails, at least try to log to debug
                        System.Diagnostics.Debug.WriteLine($"Error displaying error message: {uiEx.Message}");
                    }
                    finally
                    {
                        BtnCompare.IsEnabled = true;
                    }
                    return;
                }

                // If we got here, comparison succeeded - update UI on UI thread
                if (report != null)
                {
                    DisplayResults(report);
                    SaveReport(report, outputFolder);
                }
            }
            catch (Exception ex)
            {
                // This catches any exceptions from UI updates
                string errorMessage = ex.Message;
                LogMessage($"\n❌ Error: {errorMessage}");
                WpfMessageBox.Show($"Error during comparison:\n{errorMessage}", "Error", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            }
            finally
            {
                BtnCompare.IsEnabled = true;
            }
        }

        private void DisplayResults(DiffReport report)
        {
            // Ensure we're on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => DisplayResults(report));
                return;
            }

            // Update summary
            TxtTotalFiles.Text = report.TotalFiles.ToString();
            TxtMatchingFiles.Text = report.MatchingFiles.ToString();
            TxtDifferentFiles.Text = report.DifferentFiles.ToString();
            TxtMissingInLatest.Text = report.MissingInLatest.ToString();
            TxtMissingInReference.Text = report.MissingInReference.ToString();

            // Clear tree view
            TreeViewResults.Items.Clear();
            TxtEmptyState.Visibility = Visibility.Collapsed;

            // Add files with differences
            var filesWithDifferences = report.FileResults
                .Where(r => !r.FilesMatch && !r.IsMissingInLatest && !r.IsMissingInReference)
                .ToList();

            if (filesWithDifferences.Any())
            {
                var diffNode = new TreeViewItem
                {
                    Header = new TextBlock
                    {
                        Text = $"📄 Files with Differences ({filesWithDifferences.Count})",
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.Red)
                    }
                };

                foreach (var result in filesWithDifferences)
                {
                    diffNode.Items.Add(new TreeViewItem { Header = result.FileName });
                }

                TreeViewResults.Items.Add(diffNode);
            }

            // Add missing files
            var missingInLatest = report.FileResults.Where(r => r.IsMissingInLatest).ToList();
            if (missingInLatest.Any())
            {
                var missingNode = new TreeViewItem
                {
                    Header = new TextBlock
                    {
                        Text = $"⚠️ Missing in Latest ({missingInLatest.Count})",
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.Orange)
                    }
                };

                foreach (var result in missingInLatest)
                {
                    missingNode.Items.Add(new TreeViewItem { Header = result.FileName });
                }

                TreeViewResults.Items.Add(missingNode);
            }

            var missingInReference = report.FileResults.Where(r => r.IsMissingInReference).ToList();
            if (missingInReference.Any())
            {
                var missingNode = new TreeViewItem
                {
                    Header = new TextBlock
                    {
                        Text = $"⚠️ Missing in Reference ({missingInReference.Count})",
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.Orange)
                    }
                };

                foreach (var result in missingInReference)
                {
                    missingNode.Items.Add(new TreeViewItem { Header = result.FileName });
                }

                TreeViewResults.Items.Add(missingNode);
            }

            // Show empty state if no differences
            if (report.DifferentFiles == 0 && report.MissingInLatest == 0 && report.MissingInReference == 0)
            {
                TxtEmptyState.Text = "✅ No differences found - All files match!";
                TxtEmptyState.Foreground = new SolidColorBrush(Colors.Green);
                TxtEmptyState.Visibility = Visibility.Visible;
            }
        }

        private void SaveReport(DiffReport report, string outputFolder)
        {
            // Ensure we're on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SaveReport(report, outputFolder));
                return;
            }

            try
            {
                string savedPath = _exporter!.SaveDiffReport(report, outputFolder, saveOnlyIfDifferent: true);

                if (!string.IsNullOrEmpty(savedPath))
                {
                    string subfolderName = Path.GetFileName(savedPath);

                    // We're already on UI thread when called from Dispatcher.Invoke
                    PanelSavedFiles.Visibility = Visibility.Visible;
                    TxtSavedFiles.Text = $"Individual diff files saved in:\n  {subfolderName}/\n\n  Files: *.json";

                    LogMessage($"\n✅ Diff files saved:");
                    LogMessage($"   Location: {subfolderName}/");
                    LogMessage($"   Individual file logs: *.json");
                }
                else
                {
                    LogMessage("\n✅ No differences found - no report saved");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"\n⚠️ Warning: Could not save diff report: {ex.Message}");
            }
        }

        private string FormatValue(object? value)
        {
            if (value == null)
                return "null";

            if (value is string str)
                return $"\"{str}\"";

            if (value is decimal dec)
                return dec.ToString("F2");

            return value.ToString() ?? "null";
        }

        #endregion

        #region Validation

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(TxtReferenceFolder.Text))
            {
                WpfMessageBox.Show("Please select a reference folder", "Validation Error", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (!Directory.Exists(TxtReferenceFolder.Text))
            {
                WpfMessageBox.Show("Reference folder does not exist", "Validation Error", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtLatestFolder.Text))
            {
                WpfMessageBox.Show("Please select a latest folder", "Validation Error", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (!Directory.Exists(TxtLatestFolder.Text))
            {
                WpfMessageBox.Show("Latest folder does not exist", "Validation Error", 
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtOutputFolder.Text))
            {
                WpfMessageBox.Show("Please select an output folder", "Validation Error", 
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
                var settings = new
                {
                    ReferenceFolder = TxtReferenceFolder.Text,
                    LatestFolder = TxtLatestFolder.Text,
                    OutputFolder = TxtOutputFolder.Text
                };

                var json = System.Text.Json.JsonSerializer.Serialize(settings, 
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
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
                    var settings = System.Text.Json.JsonSerializer.Deserialize<DiffUserSettings>(json);

                    if (settings != null)
                    {
                        TxtReferenceFolder.Text = settings.ReferenceFolder ?? "";
                        TxtLatestFolder.Text = settings.LatestFolder ?? "";
                        TxtOutputFolder.Text = settings.OutputFolder ?? "";
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Could not load settings: {ex.Message}");
            }
        }

        private class DiffUserSettings
        {
            public string? ReferenceFolder { get; set; }
            public string? LatestFolder { get; set; }
            public string? OutputFolder { get; set; }
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

        #region Logging

        public void LogMessage(string message)
        {
            if (TxtLog == null) return;

            // Check if we're on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                // We're on a background thread - marshal to UI thread
                Dispatcher.Invoke(() => LogMessage(message));
                return;
            }

            // We're on UI thread - access control directly
            try
            {
                if (TxtLog != null)
                {
                    TxtLog.AppendText(message + Environment.NewLine);
                    TxtLog.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                // If UI access fails, log to debug output
                System.Diagnostics.Debug.WriteLine($"LogMessage error: {ex.Message}");
            }
        }

        #endregion
    }
}

