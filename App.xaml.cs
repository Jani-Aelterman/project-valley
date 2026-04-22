using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace NextValleyDock
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            string lang = Helpers.SettingsManager.Language;
            if (lang != "Default")
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;
            }

            m_window = new MainWindow();
            WinUIEx.WindowExtensions.SetIcon(m_window, "Assets/Project-Valley-Logo.png");
            m_window.Activate();

            m_dockWindow = new Views.DockWindow();
            WinUIEx.WindowExtensions.SetIcon(m_dockWindow, "Assets/Project-Valley-Logo.png");
            
            Helpers.SettingsManager.SettingChanged += OnSettingChanged;
            ApplyDockSettings();
        }

        private void OnSettingChanged(object? sender, string settingName)
        {
            if (settingName == "ShowDock" || settingName == "HideTaskbar")
            {
                m_window?.DispatcherQueue.TryEnqueue(() => ApplyDockSettings());
            }
        }

private bool _isStartup = true;

        private void ApplyDockSettings()
        {
            if (m_dockWindow == null) return;

            if (Helpers.SettingsManager.ShowDock)
            {
                m_dockWindow.Activate();

                if (Helpers.SettingsManager.HideTaskbar)
                    Helpers.TaskbarManager.Hide();
                else if (!_isStartup)
                    Helpers.TaskbarManager.Show();
            }
            else
            {
                m_dockWindow.AppWindow.Hide();
                if (!_isStartup)
                    Helpers.TaskbarManager.Show();
            }

            _isStartup = false;
        }

        private Window? m_window;
        private Views.DockWindow? m_dockWindow;

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
