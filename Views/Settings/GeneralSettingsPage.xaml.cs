using Project_FREAK;
using System.Windows.Controls;
using System.Windows;
using Project_FREAK.Controllers;

namespace Project_FREAK.Views.Settings
{ 
    public partial class GeneralSettingsPage : Page
    {
        private readonly SettingsManager _settingsManager;
        private SettingsManager.SettingsData _pendingSettings;

        public GeneralSettingsPage()
        {
            InitializeComponent();
            _settingsManager = ((App)Application.Current).SettingsManager;
            _pendingSettings = new SettingsManager.SettingsData
            {
                DemoModeEnabled = _settingsManager.AppliedSettings.DemoModeEnabled
            };
            DemoModeCheckBox.DataContext = _pendingSettings;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            _settingsManager.UpdateAppliedSettings(_pendingSettings);
            MessageBox.Show("Settings applied to current session!", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _settingsManager.SaveAppliedSettingsToDisk();
            MessageBox.Show("Settings saved for future sessions!", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}