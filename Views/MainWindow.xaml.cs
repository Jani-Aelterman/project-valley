using Microsoft.UI.Xaml;
using Microsoft.UI;
using System;
using Windows.Graphics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;

namespace NextValleyDock
{
    public sealed partial class MainWindow : WinUIEx.WindowEx
    {
        private DispatcherTimer? dispatcherTimer;

        // P/Invoke and AppBar definitions
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        private const uint ABM_NEW = 0x00000000;
        private const uint ABM_REMOVE = 0x00000001;
        private const uint ABM_QUERYPOS = 0x00000002;
        private const uint ABM_SETPOS = 0x00000003;
        private const uint ABE_TOP = 1;

        [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

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

            // Use custom Desktop Acrylic that stays active even when unfocused (like the Taskbar)
            this.SystemBackdrop = new AlwaysActiveDesktopAcrylic();
        }

        // Custom SystemBackdrop that forces IsInputActive to true, preventing the backdrop from 
        // falling back to a solid color when the panel loses focus.
        public class AlwaysActiveDesktopAcrylic : Microsoft.UI.Xaml.Media.SystemBackdrop
        {
            private Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController? _controller;

            protected override void OnTargetConnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop connectedTarget, Microsoft.UI.Xaml.XamlRoot xamlRoot)
            {
                base.OnTargetConnected(connectedTarget, xamlRoot);
                _controller = new Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController();
                
                var configuration = new Microsoft.UI.Composition.SystemBackdrops.SystemBackdropConfiguration();
                
                // CRUCIAL: Force it to be always active so it never dims when clicking outside the panel
                configuration.IsInputActive = true; 

                switch (Application.Current.RequestedTheme)
                {
                    case ApplicationTheme.Dark: configuration.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Dark; break;
                    case ApplicationTheme.Light: configuration.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Light; break;
                    default: configuration.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Default; break;
                }

                _controller.SetSystemBackdropConfiguration(configuration);
                _controller.AddSystemBackdropTarget(connectedTarget);
            }

            protected override void OnTargetDisconnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop disconnectedTarget)
            {
                base.OnTargetDisconnected(disconnectedTarget);
                if (_controller != null)
                {
                    _controller.RemoveSystemBackdropTarget(disconnectedTarget);
                    _controller.Dispose();
                    _controller = null;
                }
            }
        }

        private Views.SettingsWindow? _settingsWindow;

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
            return CallWindowProc(_prevWndProc, hwnd, msg, wParam, lParam);
        }

        private void OpenSettings()
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new Views.SettingsWindow();
                _settingsWindow.Closed += (s, args) => _settingsWindow = null;
            }
            _settingsWindow.Activate();
            WinUIEx.WindowExtensions.SetForegroundWindow(_settingsWindow);
            WinUIEx.WindowExtensions.CenterOnScreen(_settingsWindow);
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            // Clean up tray icon and menu
            if (_trayHwnd != IntPtr.Zero)
            {
                var nid = new NOTIFYICONDATA { cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _trayHwnd, uID = 1 };
                Shell_NotifyIcon(NIM_DELETE, ref nid);
            }
            if (_popupMenu != IntPtr.Zero) DestroyMenu(_popupMenu);
            _trayWindow?.Close();
            UnregisterAppBar();
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

            // Register Shell_NotifyIcon with tray window's HWND as callback
            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _trayHwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_APP_TRAY,
                hIcon = System.Drawing.SystemIcons.Application.Handle,
                szTip = "Project Valley"
            };
            Shell_NotifyIcon(NIM_ADD, ref nid);

            // Calculate height in physical pixels 
            // Reduced to 64 for a slimmer look, compatible with 40px search bar
            double logicalHeight = 32; 
            int dockHeight = (int)Math.Ceiling(logicalHeight * scale);
            int screenWidth = displayArea.OuterBounds.Width;

            // Register as AppBar and resize window
            RegisterAppBar(hWnd, screenWidth, dockHeight, appWindow);
        }

        private void RegisterAppBar(IntPtr hWnd, int width, int height, Microsoft.UI.Windowing.AppWindow appWindow)
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = hWnd;

            // Register
            SHAppBarMessage(ABM_NEW, ref abd);

            // Set Position
            abd.uEdge = ABE_TOP;
            abd.rc.Left = 0;
            abd.rc.Top = 0;
            abd.rc.Right = width;
            abd.rc.Bottom = height;

            SHAppBarMessage(ABM_QUERYPOS, ref abd);

            // Ensure height remains exactly what we requested
            abd.rc.Bottom = abd.rc.Top + height;

            SHAppBarMessage(ABM_SETPOS, ref abd);

            // Accurately position based on Windows Shell's accepted rectangle
            appWindow.MoveAndResize(new RectInt32(abd.rc.Left, abd.rc.Top, abd.rc.Right - abd.rc.Left, abd.rc.Bottom - abd.rc.Top));
        }

        private void UnregisterAppBar()
        {
            IntPtr hWnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(this.AppWindow.Id);
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = hWnd;
            SHAppBarMessage(ABM_REMOVE, ref abd);
        }

        private DispatcherTimer? _foregroundTimer;
        private IntPtr _lastForegroundHwnd = IntPtr.Zero;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private void SetupForegroundAppTracker()
        {
            _foregroundTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _foregroundTimer.Tick += (s, e) => UpdateForegroundApp();
            _foregroundTimer.Start();
            UpdateForegroundApp();
        }

        private async void UpdateForegroundApp()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == _hWnd) return;
            if (hwnd == _lastForegroundHwnd) return;
            _lastForegroundHwnd = hwnd;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            try
            {
                var proc = Process.GetProcessById((int)pid);

                var sb = new StringBuilder(256);
                GetWindowText(hwnd, sb, 256);
                string appName = sb.ToString();

                if (string.IsNullOrWhiteSpace(appName)) appName = proc.ProcessName;

                ActiveAppName.Text = appName;

                string exePath = proc.MainModule?.FileName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    var extractor = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (extractor != null)
                    {
                        using var bmp = extractor.ToBitmap();
                        using var ms = new MemoryStream();
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Position = 0;
                        var bitmapImage = new BitmapImage();
                        await bitmapImage.SetSourceAsync(ms.AsRandomAccessStream());
                        ActiveAppIcon.Source = bitmapImage;

                        var domColor = GetDominantColor(bmp);
                        ActiveAppIconContainer.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, domColor.R, domColor.G, domColor.B));
                    }
                }
            }
            catch
            {
                // Skip assignment if ProcessModule is restricted (e.g., system platform apps)
            }
        }

        private Windows.UI.Color GetDominantColor(System.Drawing.Bitmap bmp)
        {
            long r = 0, g = 0, b = 0;
            int count = 0;
            for (int y = 0; y < bmp.Height; y += 2)
            {
                for (int x = 0; x < bmp.Width; x += 2)
                {
                    var p = bmp.GetPixel(x, y);
                    if (p.A > 20)
                    {
                        r += p.R;
                        g += p.G;
                        b += p.B;
                        count++;
                    }
                }
            }
            if (count == 0) return Windows.UI.Color.FromArgb(255, 128, 128, 128);
            return Windows.UI.Color.FromArgb(255, (byte)(r / count), (byte)(g / count), (byte)(b / count));
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