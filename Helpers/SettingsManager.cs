using System;
using Microsoft.Win32;

namespace NextValleyDock.Helpers
{
    public static class SettingsManager
    {
        private const string RegistryPath = @"SOFTWARE\NextValleyDock";

        public static event EventHandler<string>? SettingChanged;

        public static bool GetBool(string name, bool defaultValue = true)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key?.GetValue(name) is int val)
                    return val == 1;
            }
            catch { }
            return defaultValue;
        }

        public static void SetBool(string name, bool value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue(name, value ? 1 : 0);
                SettingChanged?.Invoke(null, name);
            }
            catch { }
        }

        public static string GetString(string name, string defaultValue = "")
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key?.GetValue(name) is string val)
                    return val;
            }
            catch { }
            return defaultValue;
        }

        public static void SetString(string name, string value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue(name, value);
                SettingChanged?.Invoke(null, name);
            }
            catch { }
        }

        public static int GetInt(string name, int defaultValue = 0)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key?.GetValue(name) is int val)
                    return val;
            }
            catch { }
            return defaultValue;
        }

        public static void SetInt(string name, int value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue(name, value, RegistryValueKind.DWord);
                SettingChanged?.Invoke(null, name);
            }
            catch { }
        }

        // Specific properties for convenience
        public static bool ShowBatteryPercentage
        {
            get => GetBool("ShowBatteryPercentage", false);
            set => SetBool("ShowBatteryPercentage", value);
        }

        public static bool ShowTopPanel
        {
            get => GetBool("ShowTopPanel", true);
            set => SetBool("ShowTopPanel", value);
        }

        public static bool ShowDock
        {
            get => GetBool("ShowDock", true);
            set => SetBool("ShowDock", value);
        }

        public static bool HideTaskbar
        {
            get => GetBool("HideTaskbar", false);
            set => SetBool("HideTaskbar", value);
        }

        public static bool UseCustomTrayMenu
        {
            get => GetBool("UseCustomTrayMenu", true);
            set => SetBool("UseCustomTrayMenu", value);
        }

        public static bool UseCustomActionCenter
        {
            get => GetBool("UseCustomActionCenter", true);
            set => SetBool("UseCustomActionCenter", value);
        }

        public static string Latitude
        {
            get => GetString("Latitude", ""); // Empty means auto-detect
            set => SetString("Latitude", value);
        }

        public static string Longitude
        {
            get => GetString("Longitude", ""); // Empty means auto-detect
            set => SetString("Longitude", value);
        }

        public static int PanelHeight
        {
            get => GetInt("PanelHeight", 32); // Default is 32px
            set => SetInt("PanelHeight", value);
        }

        public static string Language
        {
            get => GetString("Language", "Default"); // "Default", "en-US", "nl-NL"
            set => SetString("Language", value);
        }

        public static string Theme
        {
            get => GetString("Theme", "System"); // "System", "Light", "Dark"
            set => SetString("Theme", value);
        }

        public static void NotifyThemeChanged()
        {
            SettingChanged?.Invoke(null, "Theme");
        }

        public static Microsoft.UI.Xaml.ElementTheme GetResolvedTheme()
        {
            string theme = Theme;
            if (theme == "Light") return Microsoft.UI.Xaml.ElementTheme.Light;
            if (theme == "Dark") return Microsoft.UI.Xaml.ElementTheme.Dark;

            // Default to System, check Windows theme (SystemUsesLightTheme)
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    object? val = key.GetValue("SystemUsesLightTheme");
                    if (val is int i && i == 1)
                    {
                        return Microsoft.UI.Xaml.ElementTheme.Light;
                    }
                }
            }
            catch { }
            return Microsoft.UI.Xaml.ElementTheme.Dark; // Fallback
        }
    }
}
