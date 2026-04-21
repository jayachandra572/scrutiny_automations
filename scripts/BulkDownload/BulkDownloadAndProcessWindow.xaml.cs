using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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

namespace BatchProcessor.Scripts.BulkDownload
{
    public partial class BulkDownloadAndProcessWindow : Window
    {
        private bool _isNavigatingBack = false;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task<ProcessingSummary>? _currentProcessingTask;
        private int _totalFiles = 0;
        private int _completedFiles = 0;
        private DateTime? _processingStartTime = null;

        public BulkDownloadAndProcessWindow()
        {
            InitializeComponent();
            LoadUserSettings();

            this.Closing += (s, args) =>
            {
                if (!_isNavigatingBack && System.Windows.Application.Current.MainWindow == this)
                {
                    System.Windows.Application.Current.Shutdown();
                }
            };
        }

        #region Browse Buttons

        private void BtnBrowseLinksFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "Select links file (text file with URLs, one per line)",
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                TxtLinksFile.Text = dialog.FileName;
                LogMessage($"📄 Links file selected: {Path.GetFileName(dialog.FileName)}");
            }
        }

        private void BtnBrowseDownloadFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WinFormsFolderBrowser
            {
                Description = "Select download folder (where DWG files will be downloaded)",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == WinFormsDialogResult.OK)
            {
                TxtDownloadFolder.Text = dialog.SelectedPath;
            }
        }

        private void BtnBrowseOutputFolder_Click(object sender, RoutedEventArgs e)
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

        private void BtnBrowseDll_Click(object sender, RoutedEventArgs e)
        {
            BrowseForDll(TxtDllPath, "Select DLL file");
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

        private void SaveUserSettings()
        {
            try
            {
                var settings = new UserSettings
                {
                    LinksFile = TxtLinksFile.Text,
                    DownloadFolder = TxtDownloadFolder.Text,
                    OutputFolder = TxtOutputFolder.Text,
                    DllPath = TxtDllPath.Text,
                    AutoCADPath = TxtAutoCADPath.Text,
                    CommandName = TxtCommandName.Text,
                    MaxParallel = int.TryParse(TxtMaxParallel.Text, out int mp) ? mp : 4,
                    VerboseLogging = ChkVerbose.IsChecked ?? false
                };

                var settingsFile = Path.Combine(Directory.GetCurrentDirectory(), "Settings", "bulk_download_settings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFile, json);
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Could not save settings: {ex.Message}");
            }
        }

        private void LoadUserSettings()
        {
            try
            {
                var settingsFile = Path.Combine(Directory.GetCurrentDirectory(), "Settings", "bulk_download_settings.json");
                if (File.Exists(settingsFile))
                {
                    var json = File.ReadAllText(settingsFile);
                    var settings = JsonSerializer.Deserialize<UserSettings>(json);

                    if (settings != null)
                    {
                        TxtLinksFile.Text = settings.LinksFile ?? "";
                        TxtDownloadFolder.Text = settings.DownloadFolder ?? "";
                        TxtOutputFolder.Text = settings.OutputFolder ?? "";
                        TxtDllPath.Text = settings.DllPath ?? "";
                        TxtAutoCADPath.Text = settings.AutoCADPath ?? "";
                        TxtCommandName.Text = settings.CommandName ?? "RunPreScrutinyValidationsBatch";
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

        #region Download and Process

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProcessingTask != null && !_currentProcessingTask.IsCompleted)
            {
                LogMessage("\n⚠️ Cancelling previous task...");
                TxtStatus.Text = "Cancelling...";
                _cancellationTokenSource?.Cancel();

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
                    BtnRun.Content = "▶ Download & Process";
                    TxtStatus.Text = "Processing cancelled";
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
            TxtLog.Clear();
            _completedFiles = 0;
            _totalFiles = 0;
            _processingStartTime = DateTime.Now;

            try
            {
                string linksFile = TxtLinksFile.Text;
                string downloadFolder = TxtDownloadFolder.Text;
                string outputFolder = TxtOutputFolder.Text;
                string commandName = TxtCommandName.Text;
                int maxParallel = int.Parse(TxtMaxParallel.Text);
                bool verbose = ChkVerbose.IsChecked ?? false;

                var dllsToLoad = new List<string> { TxtDllPath.Text };

                LogMessage("═══════════════════════════════════════════════════════════════");
                LogMessage($"Starting bulk download and processing");
                LogMessage($"Download Folder: {downloadFolder}");
                LogMessage($"Output Folder:   {outputFolder}");
                LogMessage($"Command:         {commandName}");
                LogMessage($"Max Parallel:    {maxParallel}");
                LogMessage("═══════════════════════════════════════════════════════════════\n");

                _cancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = _cancellationTokenSource.Token;

                // Start the download and processing task
                await DownloadAndProcessAsync(linksFile, downloadFolder, outputFolder, commandName, dllsToLoad, maxParallel, verbose, cancellationToken);

                if (_processingStartTime.HasValue)
                {
                    var totalDuration = DateTime.Now - _processingStartTime.Value;
                    string timeString = FormatDuration(totalDuration);
                    TxtExecutionTime.Text = $"⏱️ Total Execution Time: {timeString}";
                    TxtExecutionTime.Visibility = Visibility.Visible;
                }

                TxtStatus.Text = "✅ Processing complete!";
                LogMessage("\n✅ All processing complete!");
                WpfMessageBox.Show("Download and processing completed successfully!", "Success", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                TxtStatus.Text = "❌ Processing cancelled";
                LogMessage("\n❌ Processing was cancelled by user.");
                BtnRun.Content = "▶ Download & Process";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Error occurred";
                LogMessage($"\n❌ Error: {ex.Message}");
                WpfMessageBox.Show($"Error during processing:\n{ex.Message}", "Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                BtnRun.Content = "▶ Download & Process";
            }
            finally
            {
                _currentProcessingTask = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                BtnRun.Content = "▶ Download & Process";
            }
        }

        private async Task DownloadAndProcessAsync(string linksFile, string downloadFolder, string outputFolder,
            string commandName, List<string> dllsToLoad, int maxParallel, bool verbose, CancellationToken cancellationToken)
        {
            // Read links from file
            LogMessage("📥 Reading download links from file...");
            var links = new List<string>();
            try
            {
                links = File.ReadAllLines(linksFile)
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    .ToList();

                LogMessage($"✅ Found {links.Count} download link(s)");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to read links file: {ex.Message}");
            }

            if (links.Count == 0)
            {
                throw new Exception("No valid download links found in the file");
            }

            // Create download folder
            Directory.CreateDirectory(downloadFolder);

            // Download files
            LogMessage($"\n⬇️ Starting downloads to: {downloadFolder}");
            var downloadedFiles = new List<string>();

            using (var client = new WebClient())
            {
                for (int i = 0; i < links.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string link = links[i].Trim();
                    string fileName = Path.GetFileName(new Uri(link).LocalPath);
                    if (string.IsNullOrEmpty(fileName))
                    {
                        fileName = $"drawing_{i + 1}.dwg";
                    }

                    string filePath = Path.Combine(downloadFolder, fileName);

                    try
                    {
                        LogMessage($"📥 Downloading ({i + 1}/{links.Count}): {fileName}");
                        await Task.Run(() => client.DownloadFile(link, filePath), cancellationToken);
                        downloadedFiles.Add(filePath);
                        LogMessage($"✅ Downloaded: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"❌ Failed to download {fileName}: {ex.Message}");
                    }
                }
            }

            if (downloadedFiles.Count == 0)
            {
                throw new Exception("No files were successfully downloaded");
            }

            LogMessage($"\n✅ Downloaded {downloadedFiles.Count} file(s)");

            // Process downloaded files
            LogMessage($"\n⚙️ Starting processing with command: {commandName}");
            var processor = new DrawingBatchProcessor(
                accoreconsoleExePath: TxtAutoCADPath.Text,
                dllsToLoad: dllsToLoad,
                mainCommand: commandName,
                maxParallelism: maxParallel,
                tempScriptFolder: "",
                enableVerboseLogging: verbose
            );

            var originalOut = Console.Out;
            var textBoxWriter = new TextBoxWriter(this);
            Console.SetOut(textBoxWriter);

            try
            {
                var progress = new Progress<(int completed, int total)>(update =>
                {
                    _completedFiles = update.completed;
                    _totalFiles = update.total;
                    Dispatcher.Invoke(() =>
                    {
                        TxtStatus.Text = $"Processing... {_completedFiles}/{_totalFiles} files completed";
                    });
                });

                _currentProcessingTask = processor.ProcessFolderAsync(
                    downloadFolder,
                    outputFolder,
                    "",
                    cancellationToken,
                    progress);

                var summary = await _currentProcessingTask;
                LogMessage($"\n✅ Processing complete!");
                LogMessage($"   - Successful: {_totalFiles - summary.FailedFiles.Count - summary.NonProcessedFiles.Count}");
                LogMessage($"   - Failed: {summary.FailedFiles.Count}");
                LogMessage($"   - Non-processed: {summary.NonProcessedFiles.Count}");
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(TxtLinksFile.Text) || !File.Exists(TxtLinksFile.Text))
            {
                WpfMessageBox.Show("Please select a valid links file", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtDownloadFolder.Text))
            {
                WpfMessageBox.Show("Please select a download folder", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtOutputFolder.Text))
            {
                WpfMessageBox.Show("Please select an output folder", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtDllPath.Text) || !File.Exists(TxtDllPath.Text))
            {
                WpfMessageBox.Show("Please select a valid DLL file", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtAutoCADPath.Text) || !File.Exists(TxtAutoCADPath.Text))
            {
                WpfMessageBox.Show("Please select AutoCAD accoreconsole.exe", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtCommandName.Text))
            {
                WpfMessageBox.Show("Please enter a command name", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }

            if (!int.TryParse(TxtMaxParallel.Text, out int maxParallel) || maxParallel < 1)
            {
                WpfMessageBox.Show("Max parallel processes must be at least 1", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LogMessage error: {ex.Message}");
            }
        }

        private class TextBoxWriter : System.IO.TextWriter
        {
            private BulkDownloadAndProcessWindow _window;

            public TextBoxWriter(BulkDownloadAndProcessWindow window)
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

                    case ModeSelectionWindow.SelectedMode.ScrutinyReports:
                        newWindow = new Scripts.ScrutinyReports.ScrutinyReportsWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.JsonDiffComparison:
                        newWindow = new JsonDiffWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.RelationsCreation:
                        newWindow = new RelationsWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.BulkDownloadAndProcess:
                    default:
                        newWindow = new BulkDownloadAndProcessWindow();
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
        public string? LinksFile { get; set; }
        public string? DownloadFolder { get; set; }
        public string? OutputFolder { get; set; }
        public string? DllPath { get; set; }
        public string? AutoCADPath { get; set; }
        public string? CommandName { get; set; }
        public int MaxParallel { get; set; } = 4;
        public bool VerboseLogging { get; set; }
    }

    #endregion
}
