using System;
using System.Management;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace NextValleyDock.Helpers
{
    public static class HardwareService
    {
        // -------------------------
        // Audio Volume
        // -------------------------
        private static MMDeviceEnumerator? _deviceEnumerator;
        private static MMDevice? _defaultDevice;

        private static void EnsureAudioDevice()
        {
            if (_deviceEnumerator == null)
            {
                _deviceEnumerator = new MMDeviceEnumerator();
            }
            if (_defaultDevice == null)
            {
                try
                {
                    _defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }
                catch { }
            }
        }

        public static int GetVolume()
        {
            EnsureAudioDevice();
            if (_defaultDevice != null)
            {
                try
                {
                    return (int)(_defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                }
                catch { }
            }
            return 50;
        }

        public static void SetVolume(int volume)
        {
            EnsureAudioDevice();
            if (_defaultDevice != null)
            {
                try
                {
                    float val = volume / 100f;
                    if (val < 0) val = 0;
                    if (val > 1) val = 1;
                    _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar = val;
                }
                catch { }
            }
        }

        // -------------------------
        // Brightness
        // -------------------------
        public static int GetBrightness()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightness");
                using var instances = searcher.Get();
                foreach (ManagementObject instance in instances)
                {
                    return (byte)instance["CurrentBrightness"];
                }
            }
            catch { }
            return 100;
        }

        public static void SetBrightness(int brightness)
        {
            try
            {
                if (brightness < 0) brightness = 0;
                if (brightness > 100) brightness = 100;

                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                using var instances = searcher.Get();
                foreach (ManagementObject instance in instances)
                {
                    object[] args = new object[] { 1, brightness };
                    instance.InvokeMethod("WmiSetBrightness", args);
                    break;
                }
            }
            catch { }
        }

        // -------------------------
        // Wi-Fi
        // -------------------------
        public static async System.Threading.Tasks.Task<bool> IsWifiEnabledAsync()
        {
            try
            {
                var access = await Windows.Devices.WiFi.WiFiAdapter.RequestAccessAsync();
                if (access == Windows.Devices.WiFi.WiFiAccessStatus.Allowed)
                {
                    var result = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Devices.WiFi.WiFiAdapter.GetDeviceSelector());
                    if (result.Count > 0)
                    {
                        var adapter = await Windows.Devices.WiFi.WiFiAdapter.FromIdAsync(result[0].Id);
                        // Windows API does not directly expose 'IsOn' for the radio via WiFiAdapter easily without Radio API.
                        // We will use Radio API.
                        var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
                        foreach (var radio in radios)
                        {
                            if (radio.Kind == Windows.Devices.Radios.RadioKind.WiFi)
                            {
                                return radio.State == Windows.Devices.Radios.RadioState.On;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static async System.Threading.Tasks.Task SetWifiEnabledAsync(bool enabled)
        {
            try
            {
                var access = await Windows.Devices.Radios.Radio.RequestAccessAsync();
                if (access == Windows.Devices.Radios.RadioAccessStatus.Allowed)
                {
                    var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
                    foreach (var radio in radios)
                    {
                        if (radio.Kind == Windows.Devices.Radios.RadioKind.WiFi)
                        {
                            await radio.SetStateAsync(enabled ? Windows.Devices.Radios.RadioState.On : Windows.Devices.Radios.RadioState.Off);
                        }
                    }
                }
            }
            catch { }
        }

        // -------------------------
        // Bluetooth
        // -------------------------
        public static async System.Threading.Tasks.Task<bool> IsBluetoothEnabledAsync()
        {
            try
            {
                var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
                foreach (var radio in radios)
                {
                    if (radio.Kind == Windows.Devices.Radios.RadioKind.Bluetooth)
                    {
                        return radio.State == Windows.Devices.Radios.RadioState.On;
                    }
                }
            }
            catch { }
            return false;
        }

        public static async System.Threading.Tasks.Task SetBluetoothEnabledAsync(bool enabled)
        {
            try
            {
                var access = await Windows.Devices.Radios.Radio.RequestAccessAsync();
                if (access == Windows.Devices.Radios.RadioAccessStatus.Allowed)
                {
                    var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
                    foreach (var radio in radios)
                    {
                        if (radio.Kind == Windows.Devices.Radios.RadioKind.Bluetooth)
                        {
                            await radio.SetStateAsync(enabled ? Windows.Devices.Radios.RadioState.On : Windows.Devices.Radios.RadioState.Off);
                        }
                    }
                }
            }
            catch { }
        }
    }
}
