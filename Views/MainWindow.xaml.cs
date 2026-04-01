using Microsoft.UI.Xaml;
using Microsoft.UI;
using System;
using NextValleyDock.Helpers;
using Windows.Graphics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using NAudio.CoreAudioApi;
using Windows.Networking.Connectivity;

namespace NextValleyDock
{
    public sealed partial class MainWindow : WinUIEx.WindowEx
    {
        private DispatcherTimer? dispatcherTimer;

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int Left, Right, Top, Bottom; }

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE dwAttribute, ref uint pvAttribute, uint cbAttribute);

        private enum DWMWINDOWATTRIBUTE : uint
        {
            DWMWA_WINDOW_CORNER_PREFERENCE = 33,
            DWMWA_BORDER_COLOR = 34,
            DWMWA_VISIBLE_FRAME_BORDER_THICKNESS = 37
        }

        private enum DWM_WINDOW_CORNER_PREFERENCE
        {
            DWMWCP_DEFAULT = 0,
            DWMWCP_DONOTROUND = 1,
            DWMWCP_ROUND = 2,
            DWMWCP_ROUNDSMALL = 3
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
        private const int GWL_EXSTYLE = -20;
        private const int GWL_STYLE = -16;
        private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;

        // SetWindowCompositionAttribute for true transparency (same as TranslucentTB)
        private enum AccentState { DISABLED = 0, EnableGradient = 1, EnableBlurBehind = 2, EnableAcrylicBlurBehind = 4, InvalidState = 5 }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy { public AccentState AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

        // --- Shell_NotifyIcon (PowerToys pattern) ---
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA { public uint cbSize; public IntPtr hWnd; public uint uID; public uint uFlags; public uint uCallbackMessage; public IntPtr hIcon; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip; }
        [DllImport("shell32.dll")] private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);
        private const uint NIM_ADD = 0, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
        private const uint WM_APP_TRAY = 0x0401; // WM_USER+1, same as H.NotifyIcon registers
        private const uint WM_RBUTTONUP_TRAY = 0x0205;

        // --- Win32 popup menu ---
        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool InsertMenuW(IntPtr hMenu, uint uPos, uint uFlags, UIntPtr uID, string text);
        [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        private const uint MF_BYPOSITION = 0x400, MF_STRING = 0x0, MF_SEPARATOR = 0x800;
        private const uint TPM_BOTTOMALIGN = 0x20, TPM_RETURNCMD = 0x100;
        private const uint IDM_SETTINGS = 1001, IDM_EXIT = 1002;

        // --- WndProc subclassing ---
        private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int i, IntPtr v);
        [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private const int GWLP_WNDPROC = -4;
        private WndProcDelegate? _wndProc;
        private IntPtr _prevWndProc = IntPtr.Zero;

        private IntPtr _popupMenu = IntPtr.Zero;
        private IntPtr _hWnd = IntPtr.Zero;   // main window
        private IntPtr _trayHwnd = IntPtr.Zero; // hidden tray message window
        private Microsoft.UI.Xaml.Window? _trayWindow;

        public MainWindow()
        {
            this.InitializeComponent();
            this.Closed += MainWindow_Closed;

            this.ExtendsContentIntoTitleBar = true;

            this.MinHeight = 16;
            this.MinWidth = 16;

            // Set up the custom dock behavior FIRST (changes presenter)
            SetupDock();
            SetupClock();
            SetupForegroundAppTracker();
            SetupStatusIcons();
            SetupWeather();

            // Use custom Desktop Acrylic that stays active even when unfocused (like the Taskbar)
            this.SystemBackdrop = new AlwaysActiveDesktopAcrylic();

            // Load settings and listen for changes
            Helpers.SettingsManager.SettingChanged += OnSettingChanged;
            ApplyPanelSettings();
        }

        private void OnSettingChanged(object? sender, string settingName)
        {
            if (settingName == "ShowTopPanel")
            {
                this.DispatcherQueue.TryEnqueue(() => ApplyPanelSettings());
            }
        }

        private void ApplyPanelSettings()
        {
            bool show = Helpers.SettingsManager.ShowTopPanel;
            if (show)
            {
                UpdateDockPosition(); 
                this.AppWindow.Show();
            }
            else
            {
                UnregisterAppBar();
                this.AppWindow.Hide();
            }
        }
        private Views.SettingsWindow? _settingsWindow;

        private const uint WM_DISPLAYCHANGE = 0x007E;
        private const uint WM_DPICHANGED = 0x02E0;
        private const uint WM_SETTINGCHANGE = 0x001A;

        // Custom WndProc: intercepts tray icon right-click notification (runs on UI thread)
        private IntPtr CustomWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_APP_TRAY && ((uint)lParam.ToInt32() & 0xFFFF) == WM_RBUTTONUP_TRAY)
            {
                GetCursorPos(out var pos);
                // TrackPopupMenuEx runs its own modal loop — safe to call directly on UI thread
                uint cmd = TrackPopupMenuEx(_popupMenu, TPM_BOTTOMALIGN | TPM_RETURNCMD, pos.X, pos.Y, hwnd, IntPtr.Zero);
                if (cmd == IDM_SETTINGS) OpenSettings();
                else if (cmd == IDM_EXIT) Application.Current.Exit();
                return IntPtr.Zero;
            }
            if (msg == WM_DISPLAYCHANGE || msg == WM_DPICHANGED || msg == WM_SETTINGCHANGE)
            {
                // Give Windows Shell a brief moment to stabilize the Work Area before querying
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timer.Tick += (s, e) => {
                    timer.Stop();
                    UpdateDockPosition();
                };
                timer.Start();
            }
            return CallWindowProc(_prevWndProc, hwnd, msg, wParam, lParam);
        }

        private void OpenSettings()
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new Views.SettingsWindow();
                WinUIEx.WindowExtensions.SetIcon(_settingsWindow, "Assets/Project-Valley-Logo.png");
                _settingsWindow.Closed += (s, args) => _settingsWindow = null;
            }
            _settingsWindow.Activate();
            WinUIEx.WindowExtensions.SetForegroundWindow(_settingsWindow);
            WinUIEx.WindowExtensions.CenterOnScreen(_settingsWindow);
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);

            // Clean up tray icon and menu
            if (_trayHwnd != IntPtr.Zero)
            {
                var nid = new NOTIFYICONDATA { cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _trayHwnd, uID = 1 };
                Shell_NotifyIcon(NIM_DELETE, ref nid);
            }
            if (_popupMenu != IntPtr.Zero) DestroyMenu(_popupMenu);
            _trayWindow?.Close();
            UnregisterAppBar();
            Helpers.TaskbarManager.Show();
        }

        private void SetupDock()
        {
            var appWindow = this.AppWindow;
            
            // Hide from taskbar and Alt-Tab
            appWindow.IsShownInSwitchers = false;

            var presenter = appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }

            // Get HWND and DPI
            IntPtr hWnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(appWindow.Id);
            uint dpi = GetDpiForWindow(hWnd);
            double scale = dpi / 96.0;

            // Remove native window styles that create invisible border hitboxes (which cause overlap)
            int style = GetWindowLong(hWnd, GWL_STYLE);
            style &= ~(WS_CAPTION | WS_THICKFRAME);
            style |= WS_POPUP;
            SetWindowLong(hWnd, GWL_STYLE, style);

            // Screen dimensions
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(appWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);

            // Remove the corners and frame
            try {
                var attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
                var preference = (uint)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(hWnd, attribute, ref preference, sizeof(uint));

                // Remove the default white 1px Windows 11 border (DWMWA_COLOR_NONE)
                var borderAttr = DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR;
                uint borderColor = 0xFFFFFFFE; 
                DwmSetWindowAttribute(hWnd, borderAttr, ref borderColor, sizeof(uint));
            } catch { }

            // Create a hidden tray message window (CmdPal pattern — subclass a separate Window)
            _hWnd = hWnd;
            _trayWindow = new Microsoft.UI.Xaml.Window();
            _trayHwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(_trayWindow.AppWindow.Id);

            // Subclass the tray window's WndProc (separate window is subclassable; main window may not be)
            _wndProc = CustomWndProc;
            _prevWndProc = SetWindowLongPtrW(_trayHwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProc));

            _popupMenu = CreatePopupMenu();
            InsertMenuW(_popupMenu, 0, MF_BYPOSITION | MF_STRING, (UIntPtr)IDM_SETTINGS, "Settings");
            InsertMenuW(_popupMenu, 1, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, "");
            InsertMenuW(_popupMenu, 2, MF_BYPOSITION | MF_STRING, (UIntPtr)IDM_EXIT, "Exit");

            // Load tray icon from branding assets
            IntPtr hIcon = System.Drawing.SystemIcons.Application.Handle;
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Project-Valley-Logo.png");
                if (System.IO.File.Exists(iconPath))
                {
                    using var bmp = new System.Drawing.Bitmap(iconPath);
                    hIcon = bmp.GetHicon();
                }
            }
            catch { }

            // Register Shell_NotifyIcon with tray window's HWND as callback
            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _trayHwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_APP_TRAY,
                hIcon = hIcon,
                szTip = "Project Valley"
            };
            Shell_NotifyIcon(NIM_ADD, ref nid);

            // Calculate height in physical pixels 
            // Reduced to 64 for a slimmer look, compatible with 40px search bar
            UpdateDockPosition();
        }

        private void UpdateDockPosition()
        {
            var appWindow = this.AppWindow;
            IntPtr hWnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(appWindow.Id);
            uint dpi = GetDpiForWindow(hWnd);
            double scale = dpi / 96.0;
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(appWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);

            double logicalHeight = 32; 
            int dockHeight = (int)Math.Ceiling(logicalHeight * scale);
            int screenWidth = displayArea.OuterBounds.Width;

            // Register as AppBar and resize window IF enabled
            if (Helpers.SettingsManager.ShowTopPanel)
            {
                RegisterAppBar(hWnd, screenWidth, dockHeight, appWindow);
            }
            else
            {
                // Resize anyway so if the user shows it manually via WinUIEx tools it has a size, 
                // but don't call RegisterAppBar which moves the Work Area.
                appWindow.MoveAndResize(new RectInt32(0, 0, screenWidth, dockHeight));
            }
        }

        private void RegisterAppBar(IntPtr hWnd, int width, int height, Microsoft.UI.Windowing.AppWindow appWindow)
        {
            NativeMethods.APPBARDATA abd = new NativeMethods.APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA));
            abd.hWnd = hWnd;

            // Register
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref abd);

            // Set Position
            abd.uEdge = NativeMethods.ABE_TOP;
            abd.rc.left = 0;
            abd.rc.top = 0;
            abd.rc.right = width;
            abd.rc.bottom = height;

            NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);

            // Ensure height remains exactly what we requested
            abd.rc.bottom = abd.rc.top + height;

            NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);

            // Accurately position based on Windows Shell's accepted rectangle
            appWindow.MoveAndResize(new RectInt32(abd.rc.left, abd.rc.top, abd.rc.right - abd.rc.left, abd.rc.bottom - abd.rc.top));
        }

        private void UnregisterAppBar()
        {
            IntPtr hWnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(this.AppWindow.Id);
            NativeMethods.APPBARDATA abd = new NativeMethods.APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA));
            abd.hWnd = hWnd;
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref abd);
        }

        private IntPtr _lastForegroundHwnd = IntPtr.Zero;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private WinEventDelegate? _foregroundDelegate;
        private IntPtr _foregroundHook = IntPtr.Zero;

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint EVENT_SYSTEM_FOREGROUND = 3;
        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private void SetupForegroundAppTracker()
        {
            _foregroundDelegate = new WinEventDelegate(WinEventProc);
            _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _foregroundDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            UpdateForegroundApp(GetForegroundWindow());
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND)
            {
                UpdateForegroundApp(hwnd);
            }
        }

        private async void UpdateForegroundApp(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || hwnd == _hWnd || hwnd == _lastForegroundHwnd) return;
            _lastForegroundHwnd = hwnd;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, 256);
            string windowTitle = sb.ToString();

            // Background task for icon extraction & lockbits coloring
            var result = await System.Threading.Tasks.Task.Run(() =>
            {
                string appName = windowTitle;
                string exePath = string.Empty;

                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    uint size = 1024;
                    var pathBuffer = new StringBuilder(1024);
                    if (QueryFullProcessImageName(hProcess, 0, pathBuffer, ref size))
                    {
                        exePath = pathBuffer.ToString();
                        if (string.IsNullOrWhiteSpace(appName)) appName = Path.GetFileNameWithoutExtension(exePath);
                    }
                    else
                    {
                        try { appName = string.IsNullOrWhiteSpace(appName) ? Process.GetProcessById((int)pid).ProcessName : appName; } catch { }
                    }
                    CloseHandle(hProcess);
                }

                if (string.IsNullOrWhiteSpace(appName)) appName = "Unknown";

                byte[]? iconStream = null;
                Windows.UI.Color domColor = Windows.UI.Color.FromArgb(255, 128, 128, 128);

                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    try
                    {
                        var extractor = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                        if (extractor != null)
                        {
                            using var bmp = extractor.ToBitmap();
                            domColor = GetDominantColor(bmp);

                            using var ms = new MemoryStream();
                            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            iconStream = ms.ToArray();
                        }
                    }
                    catch { } 
                }

                return new { AppName = appName, IconBytes = iconStream, Color = domColor };
            });

            ActiveAppName.Text = result.AppName;
            ActiveAppIconContainer.Background = new SolidColorBrush(result.Color);

            if (result.IconBytes != null)
            {
                using var ms = new MemoryStream(result.IconBytes);
                var bitmapImage = new BitmapImage();
                await bitmapImage.SetSourceAsync(ms.AsRandomAccessStream());
                ActiveAppIcon.Source = bitmapImage;
            }
            else
            {
                ActiveAppIcon.Source = null;
            }
        }

        private Windows.UI.Color GetDominantColor(System.Drawing.Bitmap bmp)
        {
            long r = 0, g = 0, b = 0;
            int count = 0;
            
            var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int bytes = Math.Abs(data.Stride) * bmp.Height;
            byte[] rgbValues = new byte[bytes];
            Marshal.Copy(data.Scan0, rgbValues, 0, bytes);
            bmp.UnlockBits(data);

            for (int i = 0; i < bytes; i += 4)
            {
                if (rgbValues[i + 3] > 20)
                {
                    b += rgbValues[i];
                    g += rgbValues[i + 1];
                    r += rgbValues[i + 2];
                    count++;
                }
            }
            if (count == 0) return Windows.UI.Color.FromArgb(255, 128, 128, 128);
            return Windows.UI.Color.FromArgb(255, (byte)(r / count), (byte)(g / count), (byte)(b / count));
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        
        private const byte VK_LWIN = 0x5B;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private void SimulateWinKey(byte key)
        {
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void OpenWidgets(object sender, RoutedEventArgs e) => SimulateWinKey(0x57); // Win + W
        private void OpenActionCenter(object sender, RoutedEventArgs e) => SimulateWinKey(0x41); // Win + A
        private void OpenCalendar(object sender, RoutedEventArgs e) => SimulateWinKey(0x4E); // Win + N


        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps);

        private DispatcherTimer? _weatherTimer;

        private void SetupWeather()
        {
            var dispatcher = this.DispatcherQueue;
            // Initial fetch
            System.Threading.Tasks.Task.Run(() => UpdateWeatherAsync(dispatcher));

            // Update every 30 minutes
            _weatherTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            _weatherTimer.Tick += (s, e) => System.Threading.Tasks.Task.Run(() => UpdateWeatherAsync(dispatcher));
            _weatherTimer.Start();
        }

        private async System.Threading.Tasks.Task UpdateWeatherAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                // Add an explicit user agent to avoid geoblocking or being flagged as a generic bot scraper.
                client.DefaultRequestHeaders.Add("User-Agent", "NextValleyDock/1.0");

                string latitude = Helpers.SettingsManager.Latitude;
                string longitude = Helpers.SettingsManager.Longitude;

                // Auto-detect the location based on IP if the user hasn't set one up yet.
                if (string.IsNullOrWhiteSpace(latitude) || string.IsNullOrWhiteSpace(longitude))
                {
                    try
                    {
                        var geoResponse = await client.GetAsync("https://get.geojs.io/v1/ip/geo.json");
                        if (geoResponse.IsSuccessStatusCode)
                        {
                            var geoJsonString = await geoResponse.Content.ReadAsStringAsync();
                            var geoJson = System.Text.Json.JsonDocument.Parse(geoJsonString);
                            latitude = geoJson.RootElement.GetProperty("latitude").GetString() ?? "50.8503";
                            longitude = geoJson.RootElement.GetProperty("longitude").GetString() ?? "4.3517";

                            // Cache the detected values in Settings so we only do this once
                            Helpers.SettingsManager.Latitude = latitude;
                            Helpers.SettingsManager.Longitude = longitude;
                        }
                        else
                        {
                            latitude = "50.8503"; // Failover coordinates
                            longitude = "4.3517";
                        }
                    }
                    catch
                    {
                        latitude = "50.8503";
                        longitude = "4.3517";
                    }
                }

                // InvariantCulture conversion to string just to be safe if a comma was stored
                latitude = latitude.Replace(",", ".");
                longitude = longitude.Replace(",", ".");

                var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current_weather=true";
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync();
                var json = System.Text.Json.JsonDocument.Parse(jsonString);

                if (json.RootElement.TryGetProperty("current_weather", out var currentWeather))
                {
                    double temp = currentWeather.GetProperty("temperature").GetDouble();
                    int weatherCode = currentWeather.GetProperty("weathercode").GetInt32();

                    var (icon, desc, color) = GetWeatherDisplayInfo(weatherCode);

                    dispatcher.TryEnqueue(() =>
                    {
                        WeatherTempTextBlock.Text = $"{Math.Round(temp)}°C";
                        WeatherDescTextBlock.Text = desc;
                        WeatherIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(icon));
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching weather: {ex.Message}");
                Console.WriteLine($"Error fetching weather: {ex.Message}");
                System.IO.File.WriteAllText("weather_error.txt", ex.ToString());
                
                dispatcher?.TryEnqueue(() =>
                {
                    WeatherDescTextBlock.Text = "Error";
                });
            }
        }

        private (string icon, string description, Windows.UI.Color color) GetWeatherDisplayInfo(int wmoc)
        {
            // WMO Weather interpretation codes (WW) mapped to Bing Weather icons
            return wmoc switch
            {
                0 => ("ms-appx:///Assets/WeatherIcons/Sunny.png", "Sunny", Windows.UI.Color.FromArgb(255, 255, 165, 0)), // Clear sky
                1 => ("ms-appx:///Assets/WeatherIcons/DayPartlyCloudy.png", "Mostly Sunny", Windows.UI.Color.FromArgb(255, 255, 165, 0)),
                2 => ("ms-appx:///Assets/WeatherIcons/DayPartlyCloudy.png", "Partly Cloudy", Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                3 => ("ms-appx:///Assets/WeatherIcons/Cloudy.png", "Overcast", Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                45 or 48 => ("ms-appx:///Assets/WeatherIcons/Fog.png", "Fog", Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                51 or 53 or 55 or 56 or 57 => ("ms-appx:///Assets/WeatherIcons/Rain.png", "Drizzle", Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                61 or 63 or 65 or 66 or 67 => ("ms-appx:///Assets/WeatherIcons/Rain.png", "Rain", Windows.UI.Color.FromArgb(255, 0, 120, 215)),
                71 or 73 or 75 or 77 => ("ms-appx:///Assets/WeatherIcons/LightSnow.png", "Snow", Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                80 or 81 or 82 => ("ms-appx:///Assets/WeatherIcons/DayRainShowers.png", "Showers", Windows.UI.Color.FromArgb(255, 0, 120, 215)),
                85 or 86 => ("ms-appx:///Assets/WeatherIcons/DaySnowShowers.png", "Snow Showers", Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                95 or 96 or 99 => ("ms-appx:///Assets/WeatherIcons/Thunderstorm.png", "Thunderstorm", Windows.UI.Color.FromArgb(255, 100, 100, 150)),
                _ => ("ms-appx:///Assets/WeatherIcons/Sunny.png", "Unknown", Windows.UI.Color.FromArgb(255, 150, 150, 150))
            };
        }

        private DispatcherTimer? _statusTimer;

        private void SetupStatusIcons()
        {
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _statusTimer.Tick += (s, e) => UpdateStatusIcons();
            _statusTimer.Start();
            UpdateStatusIcons();
        }

        private void UpdateStatusIcons()
        {
            // --- Battery ---
            if (GetSystemPowerStatus(out var sps))
            {
                int pct = sps.BatteryLifePercent;
                bool showPct = false;
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\NextValleyDock");
                    if (key?.GetValue("ShowBatteryPercentage") is int val) showPct = val == 1;
                }
                catch { }
                
                if (showPct && sps.BatteryFlag != 128)
                {
                    BatteryPercentageText.Text = $"{pct}%";
                    BatteryPercentageText.Visibility = Visibility.Visible;
                }
                else
                {
                    BatteryPercentageText.Visibility = Visibility.Collapsed;
                }

                if (sps.ACLineStatus == 1) // Plugged in
                {
                    BatteryIcon.Glyph = "\xEBB5";
                }
                else if (sps.BatteryFlag != 128)
                {
                    if (pct <= 10) BatteryIcon.Glyph = "\xE850";
                    else if (pct <= 20) BatteryIcon.Glyph = "\xE851";
                    else if (pct <= 30) BatteryIcon.Glyph = "\xE852";
                    else if (pct <= 40) BatteryIcon.Glyph = "\xE853";
                    else if (pct <= 50) BatteryIcon.Glyph = "\xE854";
                    else if (pct <= 60) BatteryIcon.Glyph = "\xE855";
                    else if (pct <= 70) BatteryIcon.Glyph = "\xE856";
                    else if (pct <= 80) BatteryIcon.Glyph = "\xE857";
                    else if (pct <= 90) BatteryIcon.Glyph = "\xE858";
                    else BatteryIcon.Glyph = "\xE83F";
                }
            }

            // --- Network ---
            bool isWifi = false;
            byte signalBars = 4;
            var profiles = NetworkInformation.GetConnectionProfiles();
            
            if (profiles != null)
            {
                foreach (var p in profiles)
                {
                    if (p.IsWlanConnectionProfile && p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None)
                    {
                        isWifi = true;
                        signalBars = p.GetSignalBars() ?? 4;
                        if (p.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess) break;
                    }
                }
            }

            bool isPhysicalEthernetUp = false;
            bool isPhysicalWlanUp = false;
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    {
                        if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                        {
                            isPhysicalWlanUp = true;
                        }
                        else if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet)
                        {
                            string desc = ni.Description.ToLower();
                            if (!desc.Contains("virtual") && !desc.Contains("vethernet") && !desc.Contains("hyper") && 
                                !desc.Contains("pseudo") && !desc.Contains("vpn") && !desc.Contains("tap") &&
                                !desc.Contains("vmware") && !desc.Contains("wsl") && !desc.Contains("multiplexor") &&
                                !desc.Contains("bridge") && !desc.Contains("host-only") && !desc.Contains("bluetooth"))
                            {
                                var props = ni.GetIPProperties();
                                // If it has a gateway, it's a real active internet/LAN connection
                                if (props.GatewayAddresses.Count > 0)
                                {
                                    isPhysicalEthernetUp = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            var internetProfile = NetworkInformation.GetInternetConnectionProfile();

            if (isWifi || (isPhysicalWlanUp && !isPhysicalEthernetUp))
            {
                if (signalBars == 1) NetworkIcon.Glyph = "\xE872"; 
                else if (signalBars == 2) NetworkIcon.Glyph = "\xE873";
                else if (signalBars == 3) NetworkIcon.Glyph = "\xE874";
                else NetworkIcon.Glyph = "\xE701"; 
            }
            else if (isPhysicalEthernetUp)
            {
                NetworkIcon.Glyph = "\xE839"; // Ethernet
            }
            else if (internetProfile == null && !isWifi && !isPhysicalWlanUp)
            {
                NetworkIcon.Glyph = "\xEB55"; // Globe disconnected
            }
            else
            {
                NetworkIcon.Glyph = "\xE701"; // Fallback to Wi-Fi
            }

            // --- Audio Volume ---
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var dev = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                if (dev.AudioEndpointVolume.Mute)
                {
                    VolumeIcon.Glyph = "\xE74F";
                }
                else
                {
                    float vol = dev.AudioEndpointVolume.MasterVolumeLevelScalar;
                    if (vol == 0) VolumeIcon.Glyph = "\xE992";
                    else if (vol < 0.33) VolumeIcon.Glyph = "\xE993";
                    else if (vol < 0.66) VolumeIcon.Glyph = "\xE994";
                    else VolumeIcon.Glyph = "\xE767";
                }
            }
            catch
            {
                VolumeIcon.Glyph = "\xE74F";
            }
        }

        private void SetupClock()
        {
            // Initial call to set time right away
            UpdateTime(this, new object());

            // Timer for ticking the clock every second/minute
            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += UpdateTime;
            dispatcherTimer.Interval = TimeSpan.FromSeconds(1);
            dispatcherTimer.Start();
        }

        private void UpdateTime(object? sender, object? e)
        {
            TimeTextBlock.Text = DateTime.Now.ToString("HH:mm");
            DateTextBlock.Text = DateTime.Now.ToString("ddd d MMMM"); 

        }
    }
}
