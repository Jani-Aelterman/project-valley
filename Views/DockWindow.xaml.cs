using System;
using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NextValleyDock.Helpers;
using Windows.Graphics;

namespace NextValleyDock.Views
{
    public sealed partial class DockWindow : WinUIEx.WindowEx
    {
        public ObservableCollection<WindowInfo> RunningApps => RunningAppsService.Instance.RunningApps;

        public DockWindow()
        {
            this.InitializeComponent();
            WinUIEx.WindowExtensions.SetIcon(this, System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Project-Valley-Logo-Rounded.ico"));
            this.PersistenceId = "DockWindow";
            
            // Remove title bar and make transparent
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(null);
            
            // Background - use AlwaysActiveDesktopAcrylic for Taskbar-style transparency
            this.SystemBackdrop = new NextValleyDock.Helpers.AlwaysActiveDesktopAcrylic();

            SetupWindow();

            // Setup position
            this.Activated += DockWindow_Activated;
            
            // Initial update
            UpdatePosition();

            // Dynamic resizing when apps change
            RunningApps.CollectionChanged += (s, e) => {
                this.DispatcherQueue.TryEnqueue(() => UpdatePosition());
            };

            SetupAutoHide();
        }

        private DispatcherTimer? _autoHideTimer;
        private bool _isHovered = false;
        private RECT _logicalVisibleRect;
        private DateTime _lastStateChange = DateTime.MinValue;

        private void SetupAutoHide()
        {
            _autoHideTimer = new DispatcherTimer();
            _autoHideTimer.Interval = TimeSpan.FromMilliseconds(200);
            _autoHideTimer.Tick += (s, e) => CheckMousePosition();
            _autoHideTimer.Start();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private void CheckMousePosition()
        {
            if (!Helpers.SettingsManager.ShowDock) return;

            if (GetCursorPos(out POINT p))
            {
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(this.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                int screenHeight = displayArea.OuterBounds.Height;
                int screenWidth = displayArea.OuterBounds.Width;

                // Threshold for showing (bottom of screen)
                bool nearBottom = p.Y >= screenHeight - 20;
                
                // Use actual window rect for hit-testing
                var pos = this.AppWindow.Position;
                var size = this.AppWindow.Size;
                
                // Check if mouse is within a slightly padded region of the dock
                bool overDock = p.X >= pos.X && p.X <= pos.X + size.Width &&
                               p.Y >= pos.Y - 20 && p.Y <= pos.Y + size.Height + 20;

                bool isOverlapped = IsOverlappedByWindows();

                // Hysteresis: Don't change state too fast (500ms cooldown)
                if ((DateTime.Now - _lastStateChange).TotalMilliseconds < 500) return;

                // Show if: Mouse is near bottom OR mouse is over dock OR desktop is clear (!isOverlapped)
                if (nearBottom || overDock || !isOverlapped)
                {
                    if (!_isHovered)
                    {
                        _isHovered = true;
                        _lastStateChange = DateTime.Now;
                        ShowDock();
                    }
                }
                else
                {
                    if (_isHovered)
                    {
                        _isHovered = false;
                        _lastStateChange = DateTime.Now;
                        HideDock();
                    }
                }
            }
        }

        private bool IsOverlappedByWindows()
        {
            // Use the FIXED logical rect, not the current window position
            RECT dockRect = _logicalVisibleRect;
            
            // Check all running apps
            foreach (var app in RunningAppsService.Instance.RunningApps)
            {
                if (app.Handle == WinRT.Interop.WindowNative.GetWindowHandle(this)) continue;
                if (IsIconic(app.Handle)) continue; // Minimized
                if (!IsWindowVisible(app.Handle)) continue; // Extra safety
                if (IsWindowCloaked(app.Handle)) continue; // Check for UWP/DWM cloaking
                
                if (GetWindowRect(app.Handle, out RECT appRect))
                {
                    // Filter out "Ghost" windows with empty rects
                    if (appRect.right - appRect.left < 5 || appRect.bottom - appRect.top < 5) continue;

                    // Check if foreground window is maximized on this monitor
                    if (IsWindowMaximized(app.Handle))
                    {
                        // Optimization: if any window is maximized on this monitor, hide
                        var monitor = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(this.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                        if (appRect.left <= monitor.OuterBounds.X && appRect.right >= monitor.OuterBounds.X + monitor.OuterBounds.Width &&
                            appRect.top <= monitor.OuterBounds.Y && appRect.bottom >= monitor.OuterBounds.Y + monitor.OuterBounds.Height)
                        {
                            return true;
                        }
                    }

                    // Check intersection
                    if (appRect.left < dockRect.right && appRect.right > dockRect.left &&
                        appRect.top < dockRect.bottom && appRect.bottom > dockRect.top)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void ShowDock()
        {
            UpdatePosition(); // UpdatePosition now handles the actual Move
            
            // Force Topmost again
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW | SWP_NOACTIVATE);
        }

        private void HideDock()
        {
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(this.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            int screenHeight = displayArea.OuterBounds.Height;
            int screenWidth = displayArea.OuterBounds.Width;
            int dockWidth = this.AppWindow.Size.Width;

            // Move below screen
            this.AppWindow.Move(new PointInt32((screenWidth - dockWidth) / 2, screenHeight + 50));
        }

        private void SetupWindow()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            RunningAppsService.Instance.SelfHandle = hWnd;

            // Hide from taskbar and Alt-Tab
            this.AppWindow.IsShownInSwitchers = false;

            var presenter = this.AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }

            // P/Invoke styles for true overlay
            int style = GetWindowLong(hWnd, GWL_STYLE);
            style &= ~(WS_CAPTION | WS_THICKFRAME);
            style |= WS_POPUP;
            SetWindowLong(hWnd, GWL_STYLE, style);

            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);
            
            // Corner preference
            try {
                var attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
                var preference = (uint)2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hWnd, attribute, ref preference, sizeof(uint));
            } catch { }

            // Ensure topmost
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        private bool IsWindowCloaked(IntPtr hWnd)
        {
            int cloaked = 0;
            if (DwmGetWindowAttribute(hWnd, 14 /* DWMWA_CLOAKED */, out cloaked, sizeof(int)) == 0)
                return cloaked != 0;
            return false;
        }

        private bool IsWindowMaximized(IntPtr hWnd)
        {
            const int GWL_STYLE = -16;
            const long WS_MAXIMIZE = 0x01000000;
            long style = GetWindowLong(hWnd, GWL_STYLE);
            return (style & WS_MAXIMIZE) != 0;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RECT { public int left; public int top; public int right; public int bottom; }
        
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOACTIVATE = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
        private const int GWL_EXSTYLE = -20;
        private const int GWL_STYLE = -16;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_CHILD = 0x40000000;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, PreserveSig = false)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE dwAttribute, ref uint pvAttribute, uint cbAttribute);

        private enum DWMWINDOWATTRIBUTE : uint { DWMWA_WINDOW_CORNER_PREFERENCE = 33 }
        private enum DWM_WINDOW_CORNER_PREFERENCE { DWMWCP_DONOTROUND = 1 }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int SW_RESTORE = 9;
        private const int SW_MINIMIZE = 6;

        private void AppIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is WindowInfo info)
            {
                if (info.IsRunning && info.Handle != IntPtr.Zero)
                {
                    IntPtr hWnd = info.Handle;
                    IntPtr foregroundHwnd = GetForegroundWindow();

                    if (hWnd == foregroundHwnd)
                    {
                        ShowWindow(hWnd, SW_MINIMIZE);
                    }
                    else
                    {
                        if (IsIconic(hWnd))
                        {
                            ShowWindow(hWnd, SW_RESTORE);
                        }
                        SetForegroundWindow(hWnd);
                    }
                }
                else if (!string.IsNullOrEmpty(info.ExePath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo 
                        { 
                            FileName = info.ExePath, 
                            UseShellExecute = true,
                            WorkingDirectory = System.IO.Path.GetDirectoryName(info.ExePath)
                        });
                    }
                    catch { }
                }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        private const byte VK_LWIN = 0x5B;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private void LauncherButton_Click(object sender, RoutedEventArgs e)
        {
            // Simulate Windows Key to open Start Menu
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void MenuFlyout_Opened(object sender, object e)
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            exStyle &= ~WS_EX_NOACTIVATE; // Allow activation to enable light-dismiss
            SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);
            this.Activate();
        }

        private void MenuFlyout_Closed(object sender, object e)
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            exStyle |= WS_EX_NOACTIVATE; // Restore no-activate behavior
            SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);
        }

        private void PowerMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string tag)
            {
                try
                {
                    switch (tag)
                    {
                        case "wt-admin":
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "wt", Verb = "runas", UseShellExecute = true });
                            break;
                        case "run":
                            System.Diagnostics.Process.Start("explorer.exe", "shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}");
                            break;
                        case "logoff":
                            System.Diagnostics.Process.Start("shutdown", "/l");
                            break;
                        case "sleep":
                            System.Diagnostics.Process.Start("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
                            break;
                        case "shutdown":
                            System.Diagnostics.Process.Start("shutdown", "/s /t 0");
                            break;
                        case "restart":
                            System.Diagnostics.Process.Start("shutdown", "/r /t 0");
                            break;
                        case "desktop":
                            // Simulate Win+D
                            keybd_event(0x5B /* VK_LWIN */, 0, 0, UIntPtr.Zero);
                            keybd_event(0x44 /* D */, 0, 0, UIntPtr.Zero);
                            keybd_event(0x44 /* D */, 0, 0x0002 /* KEYEVENTF_KEYUP */, UIntPtr.Zero);
                            keybd_event(0x5B /* VK_LWIN */, 0, 0x0002 /* KEYEVENTF_KEYUP */, UIntPtr.Zero);
                            break;
                        default:
                            if (tag.StartsWith("ms-settings:"))
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = tag, UseShellExecute = true });
                            }
                            else
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = tag, UseShellExecute = true });
                            }
                            break;
                    }
                }
                catch (System.ComponentModel.Win32Exception) { /* Handle cancel or missing apps */ }
                catch (Exception) { }
            }
        }

        private void DockWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            UpdatePosition();
        }

        private void UpdatePosition()
        {
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(this.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            int screenWidth = displayArea.OuterBounds.Width;
            int screenHeight = displayArea.OuterBounds.Height;
            
            // Get DPI Scale Factor
            double scaleFactor = GetScaleFactor();
            
            // Dynamic width based on app count + Launcher Logo (~68px for logo+sep+margins)
            int appCount = RunningApps.Count;
            int logicalWidth = (appCount * 56) + 68 + 72; // Increased base padding for Launcher/Logo
            int logicalHeight = 68; 

            // Convert to Physical Pixels for AppWindow commands
            int physicalWidth = (int)(logicalWidth * scaleFactor);
            int physicalHeight = (int)(logicalHeight * scaleFactor);
            
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));
            
            // Position exactly at the bottom with a 16px logical gap from screen edge
            int x = (screenWidth - physicalWidth) / 2;
            int y = screenHeight - physicalHeight - (int)(16 * scaleFactor); 
            
            _logicalVisibleRect = new RECT { 
                left = x, 
                top = y,
                right = x + physicalWidth, 
                bottom = y + physicalHeight
            };

            // Only move if we are supposed to be visible (OR if we are explicitly initializing)
            if (_isHovered || _lastStateChange == DateTime.MinValue)
            {
                this.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
            }
            else
            {
                // Maintain hidden position
                this.AppWindow.Move(new Windows.Graphics.PointInt32(x, screenHeight + 100));
            }
        }

        private double GetScaleFactor()
        {
            try {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                uint dpi = GetDpiForWindow(hwnd);
                return dpi / 96.0;
            } catch { return 1.0; }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
        private void PinItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is WindowInfo info)
            {
                RunningAppsService.Instance.PinToTaskbar(info);
            }
        }

        private void UnpinItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is WindowInfo info)
            {
                RunningAppsService.Instance.UnpinFromTaskbar(info);
            }
        }
    }
}
