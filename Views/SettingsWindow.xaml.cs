using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NextValleyDock.Views
{
    public sealed partial class SettingsWindow : WinUIEx.WindowEx
    {
        public SettingsWindow()
        {
            this.InitializeComponent();
            WinUIEx.WindowExtensions.SetIcon(this, System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Assets", "Project-Valley-Logo-Rounded.ico"));
            this.ExtendsContentIntoTitleBar = true;

            // Mica backdrop
            this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

            // Select first nav item
            NavView.SelectedItem = GeneralItem;

            CheckStartupStatus();

            // Load settings via SettingsManager
            ShowBatteryPercentageToggle.IsOn = Helpers.SettingsManager.ShowBatteryPercentage;
            ShowTopPanelToggle.IsOn = Helpers.SettingsManager.ShowTopPanel;
            ShowDockToggle.IsOn = Helpers.SettingsManager.ShowDock;
            HideTaskbarToggle.IsOn = Helpers.SettingsManager.HideTaskbar;

            LatTextBox.Text = Helpers.SettingsManager.Latitude;
            LonTextBox.Text = Helpers.SettingsManager.Longitude;
            
            PanelHeightBox.Value = Helpers.SettingsManager.PanelHeight;

            // Load language setting
            string lang = Helpers.SettingsManager.Language;
            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if ((string)item.Tag == lang)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private const string RunKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string AppName = "NextValleyDock";

        private void CheckStartupStatus()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false);
                if (key?.GetValue(AppName) != null)
                    StartAtBootToggle.IsOn = true;
            }
            catch { }
        }

        private void ToggleStartAtBoot_Toggled(object sender, RoutedEventArgs e)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null) return;

                if (StartAtBootToggle.IsOn)
                {
                    var module = System.Diagnostics.Process.GetCurrentProcess().MainModule;
                    if (module != null)
                        key.SetValue(AppName, $"\"{module.FileName}\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch { }
        }

        private void ToggleBatteryPercentage_Toggled(object sender, RoutedEventArgs e)
        {
            Helpers.SettingsManager.ShowBatteryPercentage = ShowBatteryPercentageToggle.IsOn;
        }

        private void ToggleShowTopPanel_Toggled(object sender, RoutedEventArgs e)
        {
            Helpers.SettingsManager.ShowTopPanel = ShowTopPanelToggle.IsOn;
        }

        private void ToggleShowDock_Toggled(object sender, RoutedEventArgs e)
        {
            Helpers.SettingsManager.ShowDock = ShowDockToggle.IsOn;
        }

        private void ToggleHideTaskbar_Toggled(object sender, RoutedEventArgs e)
        {
            Helpers.SettingsManager.HideTaskbar = HideTaskbarToggle.IsOn;
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();

            GeneralPage.Visibility      = tag == "General"       ? Visibility.Visible : Visibility.Collapsed;
            PanelPage.Visibility        = tag == "Panel"          ? Visibility.Visible : Visibility.Collapsed;
            DockPage.Visibility          = tag == "Dock"            ? Visibility.Visible : Visibility.Collapsed;
            DynamicDockPage.Visibility = tag == "DynamicDock" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Location_TextChanged(object sender, TextChangedEventArgs e)
        {
            Helpers.SettingsManager.Latitude = LatTextBox.Text;
            Helpers.SettingsManager.Longitude = LonTextBox.Text;
        }

        private void PanelHeightBox_ValueChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (double.IsNaN(sender.Value)) return;
            Helpers.SettingsManager.PanelHeight = (int)sender.Value;
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem is ComboBoxItem item)
            {
                string tag = (string)item.Tag;
                if (Helpers.SettingsManager.Language != tag)
                {
                    Helpers.SettingsManager.Language = tag;
                    // Usually requires restart, can prompt or ignore
                }
            }
        }
    }
}
