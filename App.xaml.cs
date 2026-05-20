using System.Windows;
using BatchProcessor.JsonDiff;
using BatchProcessor.Scripts.PreScrutiny;
using BatchProcessor.Scripts.ScrutinyReports;
using BatchProcessor.Scripts.BulkDownload;
using BatchProcessor.Relations;
using BatchProcessor.Scripts.GenerateJsonZips;

namespace BatchProcessor
{
    public partial class App : System.Windows.Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Prevent application from shutting down when main window closes
            // We'll handle shutdown explicitly
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Show mode selection window
            var modeSelectionWindow = new ModeSelectionWindow();
            if (modeSelectionWindow.ShowDialog() == true && modeSelectionWindow.IsModeSelected)
            {
                Window mainWindow;

                switch (modeSelectionWindow.Mode)
                {
                    case ModeSelectionWindow.SelectedMode.JsonDiffComparison:
                        // Open JSON Diff Comparison window
                        mainWindow = new JsonDiffWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.ScrutinyReports:
                        // Open Scrutiny Reports Generation window
                        mainWindow = new ScrutinyReportsWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.BulkDownloadAndProcess:
                        // Open Bulk Download & Process window
                        mainWindow = new BulkDownloadAndProcessWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.RelationsCreation:
                        // Open Relations Creation window
                        mainWindow = new RelationsWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.GenerateJsonZips:
                        mainWindow = new GenerateJsonZipsWindow();
                        break;

                    case ModeSelectionWindow.SelectedMode.PreScrutinyValidations:
                    default:
                        // Open Pre Scrutiny Validations window
                        mainWindow = new PreScrutinyWindow();
                        break;
                }

                // Set as main window
                MainWindow = mainWindow;
                
                // Ensure window is enabled and visible
                mainWindow.IsEnabled = true;
                mainWindow.Visibility = Visibility.Visible;
                mainWindow.WindowState = WindowState.Normal;
                
                // Closing event handler is set in the window constructor
                
                // Show the window
                mainWindow.Show();
                
                // Activate and bring to front
                mainWindow.Activate();
                mainWindow.Focus();
            }
            else
            {
                // User closed the dialog without selecting, exit application
                Shutdown();
            }
        }
    }
}

