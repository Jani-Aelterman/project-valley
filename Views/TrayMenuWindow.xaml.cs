using Microsoft.UI.Xaml;
using NextValleyDock.Helpers;
using WinUIEx;

namespace NextValleyDock.Views
{
    public sealed partial class TrayMenuWindow : WindowEx
    {
        public System.Collections.ObjectModel.ObservableCollection<TrayIconData> TrayIcons => TrayService.Instance.Icons;

        public TrayMenuWindow()
        {
            this.InitializeComponent();

            this.SystemBackdrop = new Helpers.AlwaysActiveDesktopAcrylic();

            this.IsTitleBarVisible = false;

            ApplyCurrentTheme();

            SettingsManager.SettingChanged += OnSettingChanged;
            this.Closed += (s, e) => SettingsManager.SettingChanged -= OnSettingChanged;
            
            this.Activated += TrayMenuWindow_Activated;

            UpdateWindowSize();
            TrayIcons.CollectionChanged += (s, e) => UpdateWindowSize();
        }

        public void UpdateWindowSize()
        {
            var appWindow = this.AppWindow;
            if (appWindow != null)
            {
                int iconCount = TrayIcons.Count;
                if (iconCount == 0) iconCount = 1;

                int columns = Math.Min(iconCount, 4);
                int rows = (int)Math.Ceiling((double)iconCount / 4.0);
                
                int paddingX = 16;
                int paddingY = 16;
                
                int windowWidth = (columns * 40) + paddingX;
                int windowHeight = Math.Min((rows * 40) + paddingY, 300);

                double scale = WinUIEx.WindowExtensions.GetDpiForWindow(this) / 96.0;
                
                var workArea = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
                int x = workArea.X + workArea.Width - (int)(windowWidth * scale) - 20 - 380;
                int y = workArea.Y + workArea.Height - (int)(windowHeight * scale) - 20;
                
                if (Helpers.SettingsManager.ShowTopPanel)
                {
                    y = workArea.Y + Helpers.SettingsManager.PanelHeight + 20;
                }

                appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, (int)(windowWidth * scale), (int)(windowHeight * scale)));
            }
        }

        private void TrayMenuWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                this.Close();
            }
        }

        private void OnSettingChanged(object? sender, string settingName)
        {
            if (settingName == "Theme")
            {
                DispatcherQueue.TryEnqueue(ApplyCurrentTheme);
            }
        }

        private void ApplyCurrentTheme()
        {
            try
            {
                ThemeRoot.RequestedTheme = SettingsManager.GetResolvedTheme();
            }
            catch { }
        }
    }
}
