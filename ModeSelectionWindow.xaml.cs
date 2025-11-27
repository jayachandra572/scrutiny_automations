using System.Windows;

namespace BatchProcessor
{
    public partial class ModeSelectionWindow : Window
    {
        public enum SelectedMode
        {
            CommandsExecution,
            JsonDiffComparison,
            RelationsCreation
        }

        public SelectedMode Mode { get; private set; }
        public bool IsModeSelected { get; private set; }

        public ModeSelectionWindow()
        {
            InitializeComponent();
            IsModeSelected = false;
        }

        private void BtnCommandsMode_Click(object sender, RoutedEventArgs e)
        {
            SelectMode(SelectedMode.CommandsExecution);
        }

        private void BtnDiffMode_Click(object sender, RoutedEventArgs e)
        {
            SelectMode(SelectedMode.JsonDiffComparison);
        }

        private void Border_CommandsMode_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectMode(SelectedMode.CommandsExecution);
        }

        private void Border_DiffMode_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectMode(SelectedMode.JsonDiffComparison);
        }

        private void BtnRelationsMode_Click(object sender, RoutedEventArgs e)
        {
            SelectMode(SelectedMode.RelationsCreation);
        }

        private void Border_RelationsMode_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectMode(SelectedMode.RelationsCreation);
        }

        private void SelectMode(SelectedMode mode)
        {
            Mode = mode;
            IsModeSelected = true;
            DialogResult = true;
            Close();
        }
    }
}

