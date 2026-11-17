using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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

namespace BatchProcessor.Scripts.AnalyzeDrawings
{
    public partial class AnalyzeDrawingsWindow : Window
    {
        private bool _isNavigatingBack = false;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task<ProcessingSummary>? _currentProcessingTask;
        private int _totalFiles = 0;
        private int _completedFiles = 0;
        private DateTime? _processingStartTime = null;

        // Per-file execution timing (only touched on the UI thread)
        private readonly Dictionary<string, DateTime> _fileStartTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _fileDownloadEndTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FileExecutionRecord> _fileExecutionRecords = new List<FileExecutionRecord>();

        private class FileExecutionRecord
        {
            public string FileName { get; set; } = "";
            public DateTime StartTime { get; set; }
            public DateTime? DownloadEndTime { get; set; }
            public DateTime EndTime { get; set; }
            public TimeSpan Duration => EndTime - StartTime;
            public TimeSpan? DownloadDuration => DownloadEndTime.HasValue ? DownloadEndTime.Value - StartTime : null;
            public string Status { get; set; } = "";
        }

        public AnalyzeDrawingsWindow()
        {
            InitializeComponent();
            LoadUserSettings();
            UpdateSourceFieldStates();

            this.Closing += (s, args) =>
            {
                if (!_isNavigatingBack && System.Windows.Application.Current.MainWindow == this)
                {
                    System.Windows.Application.Current.Shutdown();
                }
            };
        }

        #region Drawing Source Selection

        private void SourceMode_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSourceFieldStates();
        }

        private void DownloadOnly_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSourceFieldStates();
        }

        /// <summary>
        /// True when the user only wants the DWG files downloaded, with no AutoCAD analysis.
        /// Only meaningful in links mode - there is nothing to download from a local folder.
        /// </summary>
        private bool IsDownloadOnly =>
            RbSourceLinks?.IsChecked == true && ChkDownloadOnly?.IsChecked == true;

        private void UpdateSourceFieldStates()
        {
            // Checked events fire during InitializeComponent, before all controls exist
            if (TxtLinksFile == null || TxtDownloadFolder == null || TxtDrawingFilesFolder == null ||
                BtnBrowseLinksFile == null || BtnBrowseDownloadFolder == null || BtnBrowseDrawingFilesFolder == null ||
                LblLinksFile == null || LblDownloadFolder == null || LblDrawingFilesFolder == null ||
                ChkDownloadOnly == null || LblOutputFolder == null || TxtOutputFolder == null ||
                BtnBrowseOutputFolder == null || GrpDllPath == null || GrpAutoCADSettings == null ||
                LblCommandName == null || TxtCommandName == null || BtnRun == null)
            {
                return;
            }

            bool useDrawingFolder = RbSourceFolder?.IsChecked == true;
            bool downloadOnly = IsDownloadOnly;

            var linksVisibility = useDrawingFolder ? Visibility.Collapsed : Visibility.Visible;
            var folderVisibility = useDrawingFolder ? Visibility.Visible : Visibility.Collapsed;

            LblLinksFile.Visibility = linksVisibility;
            TxtLinksFile.Visibility = linksVisibility;
            BtnBrowseLinksFile.Visibility = linksVisibility;
            LblDownloadFolder.Visibility = linksVisibility;
            TxtDownloadFolder.Visibility = linksVisibility;
            BtnBrowseDownloadFolder.Visibility = linksVisibility;
            ChkDownloadOnly.Visibility = linksVisibility;

            LblDrawingFilesFolder.Visibility = folderVisibility;
            TxtDrawingFilesFolder.Visibility = folderVisibility;
            BtnBrowseDrawingFilesFolder.Visibility = folderVisibility;

            // Analysis-only inputs are irrelevant when nothing is processed
            var analysisVisibility = downloadOnly ? Visibility.Collapsed : Visibility.Visible;

            LblOutputFolder.Visibility = analysisVisibility;
            TxtOutputFolder.Visibility = analysisVisibility;
            BtnBrowseOutputFolder.Visibility = analysisVisibility;
            GrpDllPath.Visibility = analysisVisibility;
            GrpAutoCADSettings.Visibility = analysisVisibility;
            LblCommandName.Visibility = analysisVisibility;
            TxtCommandName.Visibility = analysisVisibility;

            BtnRun.Content = downloadOnly ? "▶ Download Drawings" : "▶ Analyze Drawings";
        }

        #endregion

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

        private void BtnBrowseDrawingFilesFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WinFormsFolderBrowser
            {
                Description = "Select the local folder containing the DWG files to process",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == WinFormsDialogResult.OK)
            {
                TxtDrawingFilesFolder.Text = dialog.SelectedPath;
                LogMessage($"📁 Drawing files folder selected: {dialog.SelectedPath}");
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
                    UseDrawingFolder = RbSourceFolder.IsChecked == true,
                    DownloadOnly = ChkDownloadOnly.IsChecked == true,
                    LinksFile = TxtLinksFile.Text,
                    DrawingFilesFolder = TxtDrawingFilesFolder.Text,
                    DownloadFolder = TxtDownloadFolder.Text,
                    OutputFolder = TxtOutputFolder.Text,
                    DllPath = TxtDllPath.Text,
                    AutoCADPath = TxtAutoCADPath.Text,
                    CommandName = TxtCommandName.Text,
                    MaxParallel = int.TryParse(TxtMaxParallel.Text, out int mp) ? mp : 4,
                    VerboseLogging = ChkVerbose.IsChecked ?? false
                };

                var settingsFile = Path.Combine(Directory.GetCurrentDirectory(), "Settings", "analyze_drawings_settings.json");
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
                var settingsFile = Path.Combine(Directory.GetCurrentDirectory(), "Settings", "analyze_drawings_settings.json");
                if (!File.Exists(settingsFile))
                {
                    // Fall back to the settings file from before the feature was renamed
                    settingsFile = Path.Combine(Directory.GetCurrentDirectory(), "Settings", "bulk_download_settings.json");
                }

                if (File.Exists(settingsFile))
                {
                    var json = File.ReadAllText(settingsFile);
                    var settings = JsonSerializer.Deserialize<UserSettings>(json);

                    if (settings != null)
                    {
                        RbSourceLinks.IsChecked = !settings.UseDrawingFolder;
                        RbSourceFolder.IsChecked = settings.UseDrawingFolder;
                        ChkDownloadOnly.IsChecked = settings.DownloadOnly;
                        TxtLinksFile.Text = settings.LinksFile ?? "";
                        TxtDrawingFilesFolder.Text = settings.DrawingFilesFolder ?? "";
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
                    ResetRunButtonCaption();
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
            TxtExecutionTime.Visibility = Visibility.Collapsed;
            TxtExecutionTime.Text = "";
            ClearExecutionTimes();
            _completedFiles = 0;
            _totalFiles = 0;
            _processingStartTime = DateTime.Now;

            // Download-only runs have no output folder - their timings go next to the downloads
            string executionTimesFolder = IsDownloadOnly ? TxtDownloadFolder.Text : TxtOutputFolder.Text;

            try
            {
                bool useDrawingFolder = RbSourceFolder.IsChecked == true;
                bool downloadOnly = IsDownloadOnly;
                string linksFile = TxtLinksFile.Text;
                string drawingFilesFolder = TxtDrawingFilesFolder.Text?.Trim() ?? "";
                string downloadFolder = TxtDownloadFolder.Text;
                string outputFolder = TxtOutputFolder.Text;
                string commandName = TxtCommandName.Text;
                int maxParallel = int.Parse(TxtMaxParallel.Text);
                bool verbose = ChkVerbose.IsChecked ?? false;

                var dllsToLoad = new List<string> { TxtDllPath.Text };

                LogMessage("═══════════════════════════════════════════════════════════════");
                if (downloadOnly)
                {
                    LogMessage($"Starting download only (no analysis)");
                    LogMessage($"Links File:      {linksFile}");
                    LogMessage($"Download Folder: {downloadFolder}");
                }
                else if (useDrawingFolder)
                {
                    LogMessage($"Starting bulk processing from local drawing folder");
                    LogMessage($"Drawing Folder:  {drawingFilesFolder}");
                }
                else
                {
                    LogMessage($"Starting drawing analysis (download from links)");
                    LogMessage($"Links File:      {linksFile}");
                    LogMessage($"Download Folder: {downloadFolder}");
                }
                if (!downloadOnly)
                {
                    LogMessage($"Output Folder:   {outputFolder}");
                    LogMessage($"Command:         {commandName}");
                }
                LogMessage($"Max Parallel:    {maxParallel}");
                LogMessage("═══════════════════════════════════════════════════════════════\n");

                _cancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = _cancellationTokenSource.Token;

                if (downloadOnly)
                {
                    // Just fetch the files from the links - no AutoCAD, no reports
                    await DownloadOnlyAsync(linksFile, downloadFolder, maxParallel, cancellationToken);
                }
                else if (useDrawingFolder)
                {
                    // Process DWG files already present in the local folder (no download)
                    var processor = new DrawingBatchProcessor(
                        accoreconsoleExePath: TxtAutoCADPath.Text,
                        dllsToLoad: dllsToLoad,
                        mainCommand: commandName,
                        maxParallelism: maxParallel,
                        tempScriptFolder: "",
                        enableVerboseLogging: verbose
                    );

                    var originalOut = Console.Out;
                    Console.SetOut(new TextBoxWriter(this));
                    try
                    {
                        await processor.ProcessFolderAsync(
                            inputFolder: drawingFilesFolder,
                            outputFolder: outputFolder,
                            inputJsonPath: string.Empty,
                            cancellationToken: cancellationToken);
                    }
                    finally
                    {
                        Console.SetOut(originalOut);
                    }
                }
                else
                {
                    // Download from links, then process each file
                    await DownloadAndProcessAsync(linksFile, downloadFolder, outputFolder, commandName, dllsToLoad, maxParallel, verbose, cancellationToken);
                }

                await WriteExecutionTimesFileAsync(executionTimesFolder);

                if (_processingStartTime.HasValue)
                {
                    var totalDuration = DateTime.Now - _processingStartTime.Value;
                    string timeString = FormatDuration(totalDuration);
                    TxtExecutionTime.Text = $"⏱️ Total Execution Time: {timeString}";
                    TxtExecutionTime.Visibility = Visibility.Visible;
                }

                if (downloadOnly)
                {
                    TxtStatus.Text = "✅ Download complete!";
                    LogMessage("\n✅ All downloads complete!");
                    WpfMessageBox.Show("Drawing files downloaded successfully!", "Success", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                }
                else
                {
                    TxtStatus.Text = "✅ Processing complete!";
                    LogMessage("\n✅ All processing complete!");
                    WpfMessageBox.Show("Download and processing completed successfully!", "Success", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                }
            }
            catch (OperationCanceledException)
            {
                TxtStatus.Text = "❌ Processing cancelled";
                LogMessage("\n❌ Processing was cancelled by user.");

                // Keep the timings of the files that finished before cancelling
                await WriteExecutionTimesFileAsync(executionTimesFolder);
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Error occurred";
                LogMessage($"\n❌ Error: {ex.Message}");
                WpfMessageBox.Show($"Error during processing:\n{ex.Message}", "Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            }
            finally
            {
                _currentProcessingTask = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                ResetRunButtonCaption();
            }
        }

        private void ResetRunButtonCaption()
        {
            BtnRun.Content = IsDownloadOnly ? "▶ Download Drawings" : "▶ Analyze Drawings";
        }

        /// <summary>
        /// Downloads every DWG from the links file into a timestamped subfolder of the
        /// download folder. No AutoCAD command is run and downloaded files are always kept.
        /// </summary>
        private async Task DownloadOnlyAsync(string linksFile, string downloadFolder, int maxParallel,
            CancellationToken cancellationToken)
        {
            var links = ReadLinks(linksFile);

            Directory.CreateDirectory(downloadFolder);

            string batchStartTime = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string batchDownloadFolder = Path.Combine(downloadFolder, batchStartTime + "_drawing_files");
            Directory.CreateDirectory(batchDownloadFolder);
            LogMessage($"📁 Downloads: {batchStartTime}_drawing_files");

            _totalFiles = links.Count;
            _completedFiles = 0;

            int successCount = 0;
            int failedCount = 0;
            var lockObj = new object();
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            LogMessage($"\n📥 Downloading {links.Count} file(s) ({maxParallel} concurrent)...");

            await Parallel.ForEachAsync(links, new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallel,
                CancellationToken = cancellationToken
            },
            async (link, ct) =>
            {
                string fileName = "";
                string filePath = "";
                DateTime fileStart = DateTime.Now;

                try
                {
                    fileName = GetFileNameFromLink(link);

                    // Two links can resolve to the same name - keep both instead of overwriting
                    lock (lockObj)
                    {
                        string candidate = fileName;
                        int suffix = 1;
                        while (!usedFileNames.Add(candidate))
                        {
                            candidate = $"{Path.GetFileNameWithoutExtension(fileName)}_{suffix}{Path.GetExtension(fileName)}";
                            suffix++;
                        }
                        fileName = candidate;
                    }

                    filePath = Path.Combine(batchDownloadFolder, fileName);

                    using (var client = new WebClient())
                    using (ct.Register(client.CancelAsync))
                    {
                        await client.DownloadFileTaskAsync(new Uri(link), filePath);
                    }

                    lock (lockObj)
                    {
                        successCount++;
                        LogMessage($"✅ Downloaded: {fileName}");
                    }
                    RecordFileExecution(fileName, fileStart, null, DateTime.Now, "Downloaded");
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        failedCount++;
                        LogMessage($"❌ Failed ({(string.IsNullOrEmpty(fileName) ? link : fileName)}): {ex.Message}");
                    }
                    RecordFileExecution(string.IsNullOrEmpty(fileName) ? link : fileName, fileStart, null, DateTime.Now, "Failed");

                    // Don't leave a partial file behind
                    try
                    {
                        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                            File.Delete(filePath);
                    }
                    catch { }
                }
                finally
                {
                    int done;
                    lock (lockObj)
                    {
                        _completedFiles++;
                        done = _completedFiles;
                    }

                    int ok = successCount;
                    int bad = failedCount;
                    Dispatcher.Invoke(() =>
                    {
                        TxtStatus.Text = $"Downloading: {done}/{_totalFiles} | Success: {ok} | Failed: {bad}";
                    });
                }
            });

            LogMessage($"\n✅ Download complete!");
            LogMessage($"   - Downloaded: {successCount}");
            LogMessage($"   - Failed: {failedCount}");
            LogMessage($"   - Folder: {batchDownloadFolder}");
        }

        /// <summary>
        /// Reads non-empty, non-comment lines from the links file.
        /// </summary>
        private List<string> ReadLinks(string linksFile)
        {
            LogMessage("📥 Reading download links from file...");
            List<string> links;
            try
            {
                links = File.ReadAllLines(linksFile)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    .ToList();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to read links file: {ex.Message}");
            }

            LogMessage($"✅ Found {links.Count} download link(s)");

            if (links.Count == 0)
            {
                throw new Exception("No valid download links found in the file");
            }

            return links;
        }

        /// <summary>
        /// Derives a safe local file name from a download URL, falling back to a generated
        /// name when the URL carries no usable name.
        /// </summary>
        private static string GetFileNameFromLink(string link)
        {
            string fileName = "";
            try
            {
                fileName = Path.GetFileName(new Uri(link).LocalPath);
                fileName = Uri.UnescapeDataString(fileName);
            }
            catch
            {
                // Fall through to the generated name below
            }

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"drawing_{Guid.NewGuid():N}.dwg";
            }

            return fileName;
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

            // Create download and output folders
            Directory.CreateDirectory(downloadFolder);
            Directory.CreateDirectory(outputFolder);

            // Create timestamped folders for this batch run
            string batchStartTime = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string batchDownloadFolder = Path.Combine(downloadFolder, batchStartTime + "_drawing_files");
            string batchOutputFolder = Path.Combine(outputFolder, batchStartTime + "_reports");
            Directory.CreateDirectory(batchDownloadFolder);
            Directory.CreateDirectory(batchOutputFolder);
            LogMessage($"📁 Downloads: {batchStartTime}_drawing_files");
            LogMessage($"📁 Reports: {batchStartTime}_reports");

            // Create processor once
            var processor = new DrawingBatchProcessor(
                accoreconsoleExePath: TxtAutoCADPath.Text,
                dllsToLoad: dllsToLoad,
                mainCommand: commandName,
                maxParallelism: 1, // Process one file at a time per pipeline
                tempScriptFolder: "",
                enableVerboseLogging: verbose
            );

            // Route processor output to the log so it is visible and per-file timings can be tracked
            var linksOriginalOut = Console.Out;
            Console.SetOut(new TextBoxWriter(this));
            try
            {
                await processor.ProcessFromLinksAsync(
                    links: links,
                    downloadFolder: downloadFolder,
                    outputFolder: outputFolder,
                    inputJsonPath: string.Empty,
                    maxParallel: maxParallel,
                    cancellationToken: cancellationToken);
            }
            finally
            {
                Console.SetOut(linksOriginalOut);
            }
            return;

            var originalOut = Console.Out;
            var textBoxWriter = new TextBoxWriter(this);
            Console.SetOut(textBoxWriter);

            try
            {
                // Parallel pipeline: download → process each file → check JSON → delete if needed
                LogMessage($"\n📥 Starting parallel pipeline ({maxParallel} concurrent)...");
                var pipelineTasks = new List<Task>();
                var semaphore = new System.Threading.SemaphoreSlim(maxParallel);
                int successCount = 0;
                int failedCount = 0;
                int processingCount = 0;
                int totalFiles = links.Count;
                var lockObj = new object();

                for (int i = 0; i < links.Count; i++)
                {
                    int index = i;
                    string link = links[i].Trim();

                    pipelineTasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            string fileName = Path.GetFileName(new Uri(link).LocalPath);
                            if (string.IsNullOrEmpty(fileName))
                            {
                                fileName = $"drawing_{index + 1}.dwg";
                            }

                            string filePath = Path.Combine(batchDownloadFolder, fileName);

                            lock (lockObj)
                            {
                                processingCount++;
                                int remaining = totalFiles - processingCount;
                                LogMessage($"\n[{processingCount}/{totalFiles}] 📥 Downloading: {fileName}");
                                Dispatcher.Invoke(() =>
                                {
                                    TxtStatus.Text = $"Processing: {processingCount}/{totalFiles} | Success: {successCount} | Failed: {failedCount}";
                                });
                            }

                            try
                            {
                                // STEP 1: DOWNLOAD
                                using (var client = new WebClient())
                                {
                                    await Task.Run(() => client.DownloadFile(link, filePath), cancellationToken);
                                }
                                lock (lockObj) { LogMessage($"         ✅ Downloaded"); }

                                // STEP 2: PROCESS individual file
                                lock (lockObj) { LogMessage($"         ⚙️ Processing..."); }

                                await processor.ProcessSingleDrawingAsync(
                                    filePath,
                                    "", // inputJsonPath - empty = use default config
                                    batchOutputFolder);

                                // STEP 3: CHECK JSON
                                string drawingName = Path.GetFileNameWithoutExtension(filePath);
                                string jsonFileName = $"{drawingName}.json";
                                var jsonFiles = Directory.GetFiles(batchOutputFolder, jsonFileName, SearchOption.TopDirectoryOnly);

                                // STEP 4: DELETE OR KEEP
                                if (jsonFiles.Length == 0)
                                {
                                    File.Delete(filePath);
                                    lock (lockObj)
                                    {
                                        LogMessage($"         🗑️ Deleted (no JSON)");
                                        failedCount++;
                                        int remaining = totalFiles - (successCount + failedCount);
                                        Dispatcher.Invoke(() =>
                                        {
                                            TxtStatus.Text = $"Processing: {successCount + failedCount}/{totalFiles} | Success: {successCount} | Failed: {failedCount}";
                                        });
                                    }
                                }
                                else
                                {
                                    lock (lockObj)
                                    {
                                        LogMessage($"         ✅ Kept (JSON created)");
                                        successCount++;
                                        int remaining = totalFiles - (successCount + failedCount);
                                        Dispatcher.Invoke(() =>
                                        {
                                            TxtStatus.Text = $"Processing: {successCount + failedCount}/{totalFiles} | Success: {successCount} | Failed: {failedCount}";
                                        });
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                lock (lockObj)
                                {
                                    LogMessage($"         ❌ Error: {ex.Message}");
                                    failedCount++;
                                    int remaining = totalFiles - (successCount + failedCount);
                                    Dispatcher.Invoke(() =>
                                    {
                                        TxtStatus.Text = $"Processing: {successCount + failedCount}/{totalFiles} | Success: {successCount} | Failed: {failedCount}";
                                    });
                                }
                                try
                                {
                                    if (File.Exists(filePath))
                                        File.Delete(filePath);
                                }
                                catch { }
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cancellationToken));
                }

                await Task.WhenAll(pipelineTasks);

                lock (lockObj)
                {
                    LogMessage($"\n✅ Pipeline complete!");
                    LogMessage($"   - Successful: {successCount}");
                    LogMessage($"   - Failed: {failedCount}");
                }
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        private bool ValidateInputs()
        {
            bool useDrawingFolder = RbSourceFolder.IsChecked == true;

            if (useDrawingFolder)
            {
                if (string.IsNullOrWhiteSpace(TxtDrawingFilesFolder.Text) || !Directory.Exists(TxtDrawingFilesFolder.Text))
                {
                    WpfMessageBox.Show("Please select a valid drawing folder", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                    return false;
                }

                if (Directory.GetFiles(TxtDrawingFilesFolder.Text, "*.dwg", SearchOption.TopDirectoryOnly).Length == 0)
                {
                    WpfMessageBox.Show("No DWG files found in the selected drawing folder", "Validation Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                    return false;
                }
            }
            else
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
            }

            // Download-only runs never touch AutoCAD, so skip the analysis-related checks
            if (!IsDownloadOnly)
            {
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
            // Capture the time now - the UI thread may only get to the message later
            LogMessage(message, DateTime.Now);
        }

        private void LogMessage(string message, DateTime loggedAt)
        {
            if (TxtLog == null) return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => LogMessage(message, loggedAt)), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            try
            {
                TxtLog.AppendText(message + Environment.NewLine);
                TxtLog.ScrollToEnd();
                TrackFileTiming(message, loggedAt);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LogMessage error: {ex.Message}");
            }
        }

        #endregion

        #region File Execution Times

        /// <summary>
        /// Derives per-file start/end times from the batch processor's log lines.
        /// Folder mode: "Starting processing" → "Completed processing" / "UNHANDLED EXCEPTION".
        /// Links mode:  "Downloading" → "Downloaded" → "JSON created" / "Deleted (no JSON)" / "Timeout" / "Error".
        /// </summary>
        private void TrackFileTiming(string message, DateTime at)
        {
            Match m;

            if ((m = Regex.Match(message, @"🔄 Starting processing:\s*([^(]+)")).Success ||
                (m = Regex.Match(message, @"📥 Downloading:\s*(.+)$")).Success)
            {
                string name = m.Groups[1].Value.Trim();
                _fileStartTimes[name] = at;
                _fileDownloadEndTimes.Remove(name);
            }
            else if ((m = Regex.Match(message, @"✅ Downloaded:\s*(.+)$")).Success)
            {
                string name = m.Groups[1].Value.Trim();
                if (_fileStartTimes.ContainsKey(name))
                {
                    _fileDownloadEndTimes[name] = at;
                }
            }
            else if ((m = Regex.Match(message, @"✅ Completed processing:\s*([^(]+)(?:\(Success:\s*(\w+)\))?")).Success)
            {
                string status = m.Groups[2].Value.Equals("True", StringComparison.OrdinalIgnoreCase) ? "Success" : "Failed";
                FinishFileTiming(m.Groups[1].Value.Trim(), at, status);
            }
            else if ((m = Regex.Match(message, @"UNHANDLED EXCEPTION processing\s+([^:]+)")).Success)
            {
                FinishFileTiming(m.Groups[1].Value.Trim(), at, "Exception");
            }
            else if ((m = Regex.Match(message, @"JSON created → KEEP:\s*(.+)$")).Success)
            {
                FinishFileTiming(m.Groups[1].Value.Trim(), at, "Success");
            }
            else if ((m = Regex.Match(message, @"Deleted \(no JSON\):\s*(.+)$")).Success)
            {
                FinishFileTiming(m.Groups[1].Value.Trim(), at, "No JSON");
            }
            else if ((m = Regex.Match(message, @"Timeout → Skipping:\s*(.+)$")).Success)
            {
                FinishFileTiming(m.Groups[1].Value.Trim(), at, "Timeout");
            }
            else if ((m = Regex.Match(message, @"^❌ Error \((.+?)\):")).Success)
            {
                FinishFileTiming(m.Groups[1].Value.Trim(), at, "Error");
            }
        }

        private void FinishFileTiming(string name, DateTime at, string status)
        {
            if (!_fileStartTimes.TryGetValue(name, out DateTime start))
            {
                return;
            }

            _fileStartTimes.Remove(name);
            DateTime? downloadEnd = _fileDownloadEndTimes.TryGetValue(name, out DateTime d) ? d : null;
            _fileDownloadEndTimes.Remove(name);

            RecordFileExecution(name, start, downloadEnd, at, status);
        }

        private void RecordFileExecution(string name, DateTime start, DateTime? downloadEnd, DateTime end, string status)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => RecordFileExecution(name, start, downloadEnd, end, status)));
                return;
            }

            _fileExecutionRecords.Add(new FileExecutionRecord
            {
                FileName = name,
                StartTime = start,
                DownloadEndTime = downloadEnd,
                EndTime = end,
                Status = status
            });
            UpdateExecutionTimesDisplay();
        }

        private void ClearExecutionTimes()
        {
            _fileStartTimes.Clear();
            _fileDownloadEndTimes.Clear();
            _fileExecutionRecords.Clear();
            TxtExecutionTimes.Text = "";
            TxtExecutionTimesSummary.Text = "";
            GrpExecutionTimes.Visibility = Visibility.Collapsed;
        }

        private static string FormatSeconds(TimeSpan? duration)
        {
            return duration.HasValue ? duration.Value.TotalSeconds.ToString("F1") : "-";
        }

        private void UpdateExecutionTimesDisplay()
        {
            if (_fileExecutionRecords.Count == 0)
            {
                GrpExecutionTimes.Visibility = Visibility.Collapsed;
                return;
            }

            GrpExecutionTimes.Visibility = Visibility.Visible;

            bool hasDownloads = _fileExecutionRecords.Any(r => r.DownloadEndTime.HasValue);
            int nameWidth = Math.Max(10, _fileExecutionRecords.Max(r => r.FileName.Length));
            var sb = new System.Text.StringBuilder();
            sb.Append($"{"#",-4} {"File".PadRight(nameWidth)}  {"Status",-10}");
            if (hasDownloads) sb.Append($" {"Download s",10}");
            sb.AppendLine($" {"Total s",10}  {"Duration",-10} {"Start",-8} {"End",-8}");

            int index = 1;
            foreach (var r in _fileExecutionRecords)
            {
                sb.Append($"{index++,-4} {r.FileName.PadRight(nameWidth)}  {r.Status,-10}");
                if (hasDownloads) sb.Append($" {FormatSeconds(r.DownloadDuration),10}");
                sb.AppendLine($" {r.Duration.TotalSeconds,10:F1}  {FormatDuration(r.Duration),-10} {r.StartTime:HH:mm:ss} {r.EndTime:HH:mm:ss}");
            }
            TxtExecutionTimes.Text = sb.ToString().TrimEnd();

            var durations = _fileExecutionRecords.Select(r => r.Duration.TotalSeconds).ToList();
            TxtExecutionTimesSummary.Text =
                $"Files: {durations.Count}   Avg: {durations.Average():F1}s   Min: {durations.Min():F1}s   Max: {durations.Max():F1}s   Sum: {durations.Sum():F1}s";
        }

        private static string CsvField(string value)
        {
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }

        /// <summary>
        /// Writes the per-file execution times of the finished run to a CSV in the given folder.
        /// </summary>
        private async Task WriteExecutionTimesFileAsync(string folder)
        {
            // Log lines (and the timings derived from them) are dispatched at Background priority -
            // let the queued ones run so the last files are included
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            if (_fileExecutionRecords.Count == 0 || string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
                string filePath = Path.Combine(folder, $"execution_times_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("#,File,Status,Download Seconds,Total Seconds,Duration,Start,End");
                int index = 1;
                foreach (var r in _fileExecutionRecords)
                {
                    sb.AppendLine(string.Join(",",
                        index++,
                        CsvField(r.FileName),
                        CsvField(r.Status),
                        r.DownloadDuration.HasValue
                            ? r.DownloadDuration.Value.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                            : "",
                        r.Duration.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                        FormatDuration(r.Duration),
                        r.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        r.EndTime.ToString("yyyy-MM-dd HH:mm:ss")));
                }
                sb.AppendLine();
                sb.AppendLine(CsvField(TxtExecutionTimesSummary.Text));
                if (_processingStartTime.HasValue)
                {
                    sb.AppendLine(CsvField($"Total Execution Time: {FormatDuration(DateTime.Now - _processingStartTime.Value)}"));
                }

                await File.WriteAllTextAsync(filePath, sb.ToString());
                LogMessage($"⏱️ Execution times saved: {filePath}");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Could not save execution times file: {ex.Message}");
            }
        }

        private void BtnCopyExecutionTimes_Click(object sender, RoutedEventArgs e)
        {
            if (_fileExecutionRecords.Count == 0)
            {
                return;
            }

            // Tab-separated so it pastes cleanly into Excel / Sheets
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("#\tFile\tStatus\tDownload Seconds\tTotal Seconds\tDuration\tStart\tEnd");
            int index = 1;
            foreach (var r in _fileExecutionRecords)
            {
                sb.AppendLine($"{index++}\t{r.FileName}\t{r.Status}\t{FormatSeconds(r.DownloadDuration)}\t{r.Duration.TotalSeconds:F1}\t{FormatDuration(r.Duration)}\t{r.StartTime:yyyy-MM-dd HH:mm:ss}\t{r.EndTime:yyyy-MM-dd HH:mm:ss}");
            }
            sb.AppendLine();
            sb.AppendLine(TxtExecutionTimesSummary.Text);
            if (!string.IsNullOrEmpty(TxtExecutionTime.Text))
            {
                sb.AppendLine(TxtExecutionTime.Text);
            }

            try
            {
                System.Windows.Clipboard.SetText(sb.ToString());
                BtnCopyExecutionTimes.Content = "✓ Copied";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, args) =>
                {
                    BtnCopyExecutionTimes.Content = "📋 Copy All";
                    timer.Stop();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Copy Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            }
        }

        #endregion

        #region Console Redirect

        private class TextBoxWriter : System.IO.TextWriter
        {
            private AnalyzeDrawingsWindow _window;

            public TextBoxWriter(AnalyzeDrawingsWindow window)
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

                    case ModeSelectionWindow.SelectedMode.GenerateJsonZips:
                        newWindow = new Scripts.GenerateJsonZips.GenerateJsonZipsWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.AnalyzeDrawings:
                    default:
                        newWindow = new AnalyzeDrawingsWindow();
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
        public bool UseDrawingFolder { get; set; }
        public bool DownloadOnly { get; set; }
        public string? LinksFile { get; set; }
        public string? DrawingFilesFolder { get; set; }
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
