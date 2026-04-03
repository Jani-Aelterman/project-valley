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
    }
}
