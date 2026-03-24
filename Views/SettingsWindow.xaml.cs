using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NextValleyDock.Views
{
    public sealed partial class SettingsWindow : WinUIEx.WindowEx
    {
        public SettingsWindow()
        {
            this.InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;

            // Mica backdrop
            this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

            // Select first nav item
            NavView.SelectedItem = GeneralItem;

            CheckStartupStatus();
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

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();

            GeneralPage.Visibility      = tag == "General"       ? Visibility.Visible : Visibility.Collapsed;
            PanelPage.Visibility        = tag == "Panel"          ? Visibility.Visible : Visibility.Collapsed;
            DockPage.Visibility          = tag == "Dock"            ? Visibility.Visible : Visibility.Collapsed;
            DynamicDockPage.Visibility = tag == "DynamicDock" ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
