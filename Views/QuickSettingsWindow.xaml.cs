using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using NextValleyDock.Helpers;
using WinUIEx;

namespace NextValleyDock.Views
{
    public sealed partial class QuickSettingsWindow : WindowEx
    {
        private bool _isUpdatingFromSystem = false;

        public QuickSettingsWindow()
        {
            this.InitializeComponent();

            this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop() 
            { 
                Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt 
            };

            this.IsTitleBarVisible = false;

            ApplyCurrentTheme();

            SettingsManager.SettingChanged += OnSettingChanged;
            this.Closed += (s, e) => SettingsManager.SettingChanged -= OnSettingChanged;

            LoadCurrentHardwareStates();
        }

        private async void LoadCurrentHardwareStates()
        {
            _isUpdatingFromSystem = true;
            
            // Load Volume
            int volume = HardwareService.GetVolume();
            VolumeSlider.Value = volume;

            // Load Brightness
            int brightness = HardwareService.GetBrightness();
            BrightnessSlider.Value = brightness;

            // Load Radios
            WifiToggle.IsChecked = await HardwareService.IsWifiEnabledAsync();
            BluetoothToggle.IsChecked = await HardwareService.IsBluetoothEnabledAsync();

            WifiToggle.Checked += WifiToggle_Checked;
            WifiToggle.Unchecked += WifiToggle_Unchecked;
            BluetoothToggle.Checked += BluetoothToggle_Checked;
            BluetoothToggle.Unchecked += BluetoothToggle_Unchecked;

            _isUpdatingFromSystem = false;
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

        private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (!_isUpdatingFromSystem)
            {
                HardwareService.SetVolume((int)e.NewValue);
            }
        }

        private void BrightnessSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (!_isUpdatingFromSystem)
            {
                HardwareService.SetBrightness((int)e.NewValue);
            }
        }

        private async void WifiToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isUpdatingFromSystem) await HardwareService.SetWifiEnabledAsync(true);
        }

        private async void WifiToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!_isUpdatingFromSystem) await HardwareService.SetWifiEnabledAsync(false);
        }

        private async void BluetoothToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isUpdatingFromSystem) await HardwareService.SetBluetoothEnabledAsync(true);
        }

        private async void BluetoothToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!_isUpdatingFromSystem) await HardwareService.SetBluetoothEnabledAsync(false);
        }
    }
}
