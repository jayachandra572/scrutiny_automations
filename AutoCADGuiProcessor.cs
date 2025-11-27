using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace BatchProcessor
{
    /// <summary>
    /// Results from batch processing multiple drawings
    /// </summary>
    public class ProcessingResults
    {
        public int TotalFiles { get; set; }
        public List<string> ProcessedFiles { get; set; } = new List<string>();
        public List<string> FailedFiles { get; set; } = new List<string>();
    }

    /// <summary>
    /// Handles automation of AutoCAD GUI for processing drawings with UIPlugin DLL
    /// </summary>
    public class AutoCADGuiProcessor
    {
        private readonly string _autocadExePath;
        private readonly string _uiPluginDllPath;
        private const int CommandTimeoutSeconds = 300; // 5 minutes max per file
        private const int PopupCheckIntervalMs = 200;
        private Process? _autocadProcess;

        public AutoCADGuiProcessor(string autocadExePath, string uiPluginDllPath)
        {
            _autocadExePath = autocadExePath ?? throw new ArgumentNullException(nameof(autocadExePath));
            _uiPluginDllPath = uiPluginDllPath ?? throw new ArgumentNullException(nameof(uiPluginDllPath));
        }

        /// <summary>
        /// Process all drawing files in a single AutoCAD session
        /// </summary>
        public async Task<ProcessingResults> ProcessAllDrawingsAsync(
            string[] drawingPaths,
            CancellationToken cancellationToken,
            Action<string>? logCallback = null,
            IProgress<(int current, int total, string currentFile)>? progress = null)
        {
            var results = new ProcessingResults
            {
                TotalFiles = drawingPaths.Length,
                ProcessedFiles = new List<string>(),
                FailedFiles = new List<string>()
            };

            if (drawingPaths.Length == 0)
            {
                logCallback?.Invoke("No drawing files to process");
                return results;
            }

            // Create a single script file that processes all drawings
            string tempScriptPath = "";
            try
            {
                tempScriptPath = CreateBatchProcessingScript(drawingPaths);
                logCallback?.Invoke($"📝 Created batch script: {Path.GetFileName(tempScriptPath)}");
                logCallback?.Invoke($"📊 Processing {drawingPaths.Length} file(s) in single AutoCAD session");

                // Launch AutoCAD once
                logCallback?.Invoke("🚀 Launching AutoCAD...");
                _autocadProcess = LaunchAutoCAD(tempScriptPath);

                if (_autocadProcess == null)
                {
                    logCallback?.Invoke("❌ Failed to launch AutoCAD");
                    results.FailedFiles.AddRange(drawingPaths.Select(Path.GetFileName));
                    return results;
                }

                // Wait for AutoCAD to start
                await Task.Delay(3000, cancellationToken);
                logCallback?.Invoke("⏳ Waiting for AutoCAD to initialize...");

                // Monitor for popups and handle them
                var popupMonitorTask = MonitorAndHandlePopupsAsync(cancellationToken, logCallback);

                // Wait for script to complete or timeout
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(CommandTimeoutSeconds * drawingPaths.Length), cancellationToken);
                var processTask = WaitForProcessCompletionAsync(_autocadProcess, cancellationToken);

                var completedTask = await Task.WhenAny(processTask, timeoutTask, popupMonitorTask);

                if (completedTask == timeoutTask)
                {
                    logCallback?.Invoke($"⚠️ Processing timed out after {CommandTimeoutSeconds * drawingPaths.Length} seconds");
                    KillAutoCADProcess();
                    results.FailedFiles.AddRange(drawingPaths.Select(Path.GetFileName));
                    return results;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    logCallback?.Invoke("⚠️ Processing was cancelled");
                    KillAutoCADProcess();
                    return results;
                }

                // Check if process exited successfully
                if (_autocadProcess.HasExited)
                {
                    int exitCode = _autocadProcess.ExitCode;
                    logCallback?.Invoke($"✅ AutoCAD process completed with exit code: {exitCode}");
                    
                    if (exitCode == 0)
                    {
                        results.ProcessedFiles.AddRange(drawingPaths.Select(Path.GetFileName));
                    }
                    else
                    {
                        results.FailedFiles.AddRange(drawingPaths.Select(Path.GetFileName));
                    }
                }
                else
                {
                    // If process is still running, wait a bit more for it to finish
                    await Task.Delay(2000, cancellationToken);

                    if (_autocadProcess.HasExited)
                    {
                        if (_autocadProcess.ExitCode == 0)
                        {
                            results.ProcessedFiles.AddRange(drawingPaths.Select(Path.GetFileName));
                        }
                        else
                        {
                            results.FailedFiles.AddRange(drawingPaths.Select(Path.GetFileName));
                        }
                    }
                    else
                    {
                        logCallback?.Invoke("⚠️ AutoCAD process did not exit - forcing termination");
                        KillAutoCADProcess();
                        results.FailedFiles.AddRange(drawingPaths.Select(Path.GetFileName));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                logCallback?.Invoke("⚠️ Processing cancelled");
                KillAutoCADProcess();
                return results;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"❌ Error during processing: {ex.Message}");
                KillAutoCADProcess();
                results.FailedFiles.AddRange(drawingPaths.Select(Path.GetFileName));
                return results;
            }
            finally
            {
                // Clean up script file
                try
                {
                    if (!string.IsNullOrEmpty(tempScriptPath) && File.Exists(tempScriptPath))
                    {
                        await Task.Delay(1000); // Wait a bit before deleting
                        File.Delete(tempScriptPath);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }

                // Ensure process is killed
                KillAutoCADProcess();
            }

            return results;
        }

        /// <summary>
        /// Process a single drawing file through AutoCAD GUI (legacy method - kept for compatibility)
        /// </summary>
        public async Task<bool> ProcessDrawingAsync(
            string drawingPath,
            CancellationToken cancellationToken,
            Action<string>? logCallback = null)
        {
            if (!File.Exists(drawingPath))
            {
                logCallback?.Invoke($"❌ Drawing file not found: {drawingPath}");
                return false;
            }

            if (!File.Exists(_uiPluginDllPath))
            {
                logCallback?.Invoke($"❌ UIPlugin DLL not found: {_uiPluginDllPath}");
                return false;
            }

            string tempScriptPath = "";
            try
            {
                // Create script file
                tempScriptPath = CreateProcessingScript(drawingPath);
                logCallback?.Invoke($"📝 Created script: {Path.GetFileName(tempScriptPath)}");

                // Launch AutoCAD with drawing and script
                logCallback?.Invoke("🚀 Launching AutoCAD...");
                logCallback?.Invoke($"📂 Drawing: {Path.GetFileName(drawingPath)}");
                _autocadProcess = LaunchAutoCAD(drawingPath, tempScriptPath);
                
                if (_autocadProcess == null)
                {
                    logCallback?.Invoke("❌ Failed to launch AutoCAD");
                    return false;
                }

                // Wait for AutoCAD to start
                await Task.Delay(3000, cancellationToken);
                logCallback?.Invoke("⏳ Waiting for AutoCAD to initialize...");

                // Monitor for popups and handle them
                var popupMonitorTask = MonitorAndHandlePopupsAsync(cancellationToken, logCallback);

                // Wait for script to complete or timeout
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(CommandTimeoutSeconds), cancellationToken);
                var processTask = WaitForProcessCompletionAsync(_autocadProcess, cancellationToken);

                var completedTask = await Task.WhenAny(processTask, timeoutTask, popupMonitorTask);

                if (completedTask == timeoutTask)
                {
                    logCallback?.Invoke($"⚠️ Processing timed out after {CommandTimeoutSeconds} seconds");
                    KillAutoCADProcess();
                    return false;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    logCallback?.Invoke("⚠️ Processing was cancelled");
                    KillAutoCADProcess();
                    return false;
                }

                // Check if process exited successfully
                if (_autocadProcess.HasExited)
                {
                    int exitCode = _autocadProcess.ExitCode;
                    logCallback?.Invoke($"✅ AutoCAD process completed with exit code: {exitCode}");
                    return exitCode == 0;
                }

                // If process is still running, wait a bit more for it to finish
                await Task.Delay(2000, cancellationToken);
                
                if (_autocadProcess.HasExited)
                {
                    return _autocadProcess.ExitCode == 0;
                }

                // Process still running - kill it and return false
                logCallback?.Invoke("⚠️ AutoCAD process did not exit - forcing termination");
                KillAutoCADProcess();
                return false;
            }
            catch (OperationCanceledException)
            {
                logCallback?.Invoke("⚠️ Processing cancelled");
                KillAutoCADProcess();
                return false;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"❌ Error during processing: {ex.Message}");
                KillAutoCADProcess();
                return false;
            }
            finally
            {
                // Clean up script file
                try
                {
                    if (!string.IsNullOrEmpty(tempScriptPath) && File.Exists(tempScriptPath))
                    {
                        await Task.Delay(1000); // Wait a bit before deleting
                        File.Delete(tempScriptPath);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }

                // Ensure process is killed
                KillAutoCADProcess();
            }
        }

        /// <summary>
        /// Create a batch script file that processes all drawings in a single AutoCAD session
        /// </summary>
        private string CreateBatchProcessingScript(string[] drawingPaths)
        {
            string tempDir = Path.GetTempPath();
            string scriptName = $"create_relations_batch_{Guid.NewGuid():N}.scr";
            string scriptPath = Path.Combine(tempDir, scriptName);

            // Escape paths - use forward slashes for AutoCAD
            string escapedDllPath = _uiPluginDllPath.Replace("\\", "/");

            // Create AutoCAD script file that processes all drawings
            var scriptBuilder = new System.Text.StringBuilder();
            
            // Process each drawing
            for (int i = 0; i < drawingPaths.Length; i++)
            {
                string escapedDwgPath = drawingPaths[i].Replace("\\", "/");
                string fileName = Path.GetFileName(drawingPaths[i]);
                
                scriptBuilder.AppendLine($"; Processing file {i + 1}/{drawingPaths.Length}: {fileName}");
                
                // Open the drawing
                scriptBuilder.AppendLine($"OPEN");
                scriptBuilder.AppendLine($"\"{escapedDwgPath}\"");
                
                // If opening while a drawing is already open, AutoCAD will prompt:
                // "Close the current drawing?" - answer Y
                // AutoCAD may also ask "Save changes?" - answer Y if prompted
                // Since we save after processing, it usually won't ask to save, but be ready
                if (i > 0)
                {
                    scriptBuilder.AppendLine($"Y"); // Answer "Close current drawing?" with Yes
                }
                
                // Load DLL (only needed once, after first drawing is opened)
                if (i == 0)
                {
                    scriptBuilder.AppendLine($"; Loading UIPlugin DLL");
                    scriptBuilder.AppendLine($"NETLOAD");
                    scriptBuilder.AppendLine($"\"{escapedDllPath}\"");
                }
                
                // Run the command
                scriptBuilder.AppendLine($"CREATE_RELATIONS_FOR_ENTITIES");
                
                // Save the drawing
                scriptBuilder.AppendLine($"QSAVE");
                
                // Close the current drawing explicitly (only if not the last file)
                // This ensures clean state before opening the next file
                if (i < drawingPaths.Length - 1)
                {
                    scriptBuilder.AppendLine($"; Closing drawing before opening next file");
                    scriptBuilder.AppendLine($"CLOSE");
                }
                
                scriptBuilder.AppendLine();
            }

            // Quit AutoCAD after all drawings are processed
            scriptBuilder.AppendLine($"; All drawings processed - closing AutoCAD");
            scriptBuilder.AppendLine($"QUIT");
            scriptBuilder.AppendLine($"Y");

            File.WriteAllText(scriptPath, scriptBuilder.ToString());
            return scriptPath;
        }

        /// <summary>
        /// Create a script file for processing a single drawing (legacy method)
        /// </summary>
        private string CreateProcessingScript(string drawingPath)
        {
            string tempDir = Path.GetTempPath();
            string scriptName = $"create_relations_{Guid.NewGuid():N}.scr";
            string scriptPath = Path.Combine(tempDir, scriptName);

            // Escape paths - use forward slashes or double backslashes for AutoCAD
            string escapedDllPath = _uiPluginDllPath.Replace("\\", "/");

            // Create AutoCAD script file (.scr format)
            // Script format: each command on its own line
            // Open drawing first, then load DLL and run command
            string escapedDwgPath = drawingPath.Replace("\\", "/");
            
            var scriptContent = $@"
OPEN
""{escapedDwgPath}""
NETLOAD
""{escapedDllPath}""
CREATE_RELATIONS_FOR_ENTITIES
QSAVE
QUIT
Y
";

            File.WriteAllText(scriptPath, scriptContent);
            return scriptPath;
        }

        /// <summary>
        /// Launch AutoCAD with the script (overloaded for batch processing)
        /// </summary>
        private Process? LaunchAutoCAD(string scriptPath)
        {
            try
            {
                // First, try to close any existing AutoCAD instances
                CloseExistingAutoCADInstances();

                var startInfo = new ProcessStartInfo
                {
                    FileName = _autocadExePath,
                    // Run script file: acad.exe /b script.scr
                    Arguments = $"/nologo /b \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(_autocadExePath) ?? ""
                };

                var process = Process.Start(startInfo);
                return process;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error launching AutoCAD: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Launch AutoCAD with the script (legacy method - kept for single file processing)
        /// </summary>
        private Process? LaunchAutoCAD(string drawingPath, string scriptPath)
        {
            try
            {
                // First, try to close any existing AutoCAD instances
                CloseExistingAutoCADInstances();

                var startInfo = new ProcessStartInfo
                {
                    FileName = _autocadExePath,
                    // Run script file: acad.exe /b script.scr
                    // Script file will open the drawing and process it
                    Arguments = $"/nologo /b \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(_autocadExePath) ?? ""
                };

                var process = Process.Start(startInfo);
                return process;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error launching AutoCAD: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Monitor for popups and automatically dismiss them
        /// </summary>
        private async Task MonitorAndHandlePopupsAsync(CancellationToken cancellationToken, Action<string>? logCallback)
        {
            while (!cancellationToken.IsCancellationRequested && (_autocadProcess == null || !_autocadProcess.HasExited))
            {
                try
                {
                    await Task.Delay(PopupCheckIntervalMs, cancellationToken);

                    // Find AutoCAD windows
                    var autocadWindows = FindAutoCADWindows();
                    
                    foreach (var window in autocadWindows)
                    {
                        // Check if this is a dialog/popup
                        if (IsPopupWindow(window))
                        {
                            logCallback?.Invoke("⚠️ Popup detected - attempting to dismiss...");
                            if (DismissPopup(window))
                            {
                                logCallback?.Invoke("✅ Popup dismissed");
                            }
                            else
                            {
                                logCallback?.Invoke("⚠️ Failed to dismiss popup - trying Enter key");
                                // Try pressing Enter as fallback
                                SendKeys.SendWait("{ENTER}");
                                await Task.Delay(500, cancellationToken);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Log but continue monitoring
                    System.Diagnostics.Debug.WriteLine($"Error monitoring popups: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Find AutoCAD windows using UI Automation
        /// </summary>
        private AutomationElement[] FindAutoCADWindows()
        {
            try
            {
                var root = AutomationElement.RootElement;
                var condition = new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                    new OrCondition(
                        new PropertyCondition(AutomationElement.NameProperty, "AutoCAD"),
                        new PropertyCondition(AutomationElement.ClassNameProperty, "Afx:00400000:8:00010003:00000006:00000000")
                    )
                );

                var windows = root.FindAll(TreeScope.Children, condition);
                var result = new AutomationElement[windows.Count];
                for (int i = 0; i < windows.Count; i++)
                {
                    result[i] = windows[i];
                }
                return result;
            }
            catch
            {
                return Array.Empty<AutomationElement>();
            }
        }

        /// <summary>
        /// Check if a window is a popup/dialog
        /// </summary>
        private bool IsPopupWindow(AutomationElement window)
        {
            try
            {
                // Check if window is a dialog by looking for dialog characteristics
                // (DialogPattern is not available in all .NET versions, so we check other patterns)

                // Check for window pattern
                var windowPattern = window.GetCurrentPattern(WindowPattern.Pattern) as WindowPattern;
                if (windowPattern != null && windowPattern.Current.WindowInteractionState == WindowInteractionState.ReadyForUserInteraction)
                {
                    string name = window.Current.Name ?? "";
                    // Common AutoCAD dialog titles/keywords
                    if (name.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Alert", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Dialog", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Message", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                // Check for button patterns (dialogs usually have buttons)
                var buttons = window.FindAll(TreeScope.Descendants, 
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                
                if (buttons.Count > 0 && buttons.Count <= 5) // Dialogs typically have 1-5 buttons
                {
                    // Check if it's a modal dialog by looking at its parent
                    AutomationElement parent = TreeWalker.RawViewWalker.GetParent(window);
                    if (parent != null && parent.Current.ProcessId == window.Current.ProcessId)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempt to dismiss a popup window
        /// </summary>
        private bool DismissPopup(AutomationElement popup)
        {
            try
            {
                // Try to find OK, Yes, Close, or Skip button
                var buttonConditions = new[]
                {
                    new PropertyCondition(AutomationElement.NameProperty, "OK"),
                    new PropertyCondition(AutomationElement.NameProperty, "Yes"),
                    new PropertyCondition(AutomationElement.NameProperty, "Close"),
                    new PropertyCondition(AutomationElement.NameProperty, "Skip"),
                    new PropertyCondition(AutomationElement.NameProperty, "Cancel"),
                    new PropertyCondition(AutomationElement.NameProperty, "Continue")
                };

                foreach (var condition in buttonConditions)
                {
                    var buttons = popup.FindAll(TreeScope.Descendants, 
                        new AndCondition(
                            condition,
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)
                        ));

                    if (buttons.Count > 0)
                    {
                        var button = buttons[0];
                        var invokePattern = button.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
                        if (invokePattern != null)
                        {
                            invokePattern.Invoke();
                            return true;
                        }
                    }
                }

                // Fallback: try to find first button and click it
                var allButtons = popup.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

                if (allButtons.Count > 0)
                {
                    var firstButton = allButtons[0];
                    var invokePattern = firstButton.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
                    if (invokePattern != null)
                    {
                        invokePattern.Invoke();
                        return true;
                    }
                }

                // Last resort: send Enter key
                SendKeys.SendWait("{ENTER}");
                return false;
            }
            catch
            {
                // Try Enter key as fallback
                try
                {
                    SendKeys.SendWait("{ENTER}");
                }
                catch { }
                return false;
            }
        }

        /// <summary>
        /// Wait for process to complete
        /// </summary>
        private async Task WaitForProcessCompletionAsync(Process process, CancellationToken cancellationToken)
        {
            try
            {
                while (!process.HasExited && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(500, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
        }

        /// <summary>
        /// Close any existing AutoCAD instances
        /// </summary>
        private void CloseExistingAutoCADInstances()
        {
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => p.ProcessName.Equals("acad", StringComparison.OrdinalIgnoreCase) ||
                               p.ProcessName.Equals("acadlt", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var proc in processes)
                {
                    try
                    {
                        if (!proc.HasExited)
                        {
                            proc.CloseMainWindow();
                            if (!proc.WaitForExit(3000))
                            {
                                proc.Kill();
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors closing processes
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
        }

        /// <summary>
        /// Kill the AutoCAD process if it's still running
        /// </summary>
        private void KillAutoCADProcess()
        {
            try
            {
                if (_autocadProcess != null && !_autocadProcess.HasExited)
                {
                    _autocadProcess.Kill();
                    _autocadProcess.WaitForExit(5000);
                }
            }
            catch
            {
                // Ignore errors
            }
            finally
            {
                _autocadProcess?.Dispose();
                _autocadProcess = null;
            }
        }
    }
}

