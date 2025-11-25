using System.Windows;

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

                    case ModeSelectionWindow.SelectedMode.CommandsExecution:
                    default:
                        // Open Commands Execution window (existing MainWindow)
                        mainWindow = new MainWindow();
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

