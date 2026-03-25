using System;
using System.Runtime.InteropServices;

namespace NextValleyDock.Helpers
{
    public static class TaskbarManager
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref NativeMethods.RECT pvParam, uint fWinIni);

        private const uint SPI_SETWORKAREA = 0x002F;
        private const uint SPI_GETWORKAREA = 0x0030;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        public static void SetVisibility(bool visible)
        {
            IntPtr taskbarWnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarWnd != IntPtr.Zero)
            {
                ShowWindow(taskbarWnd, visible ? SW_SHOW : SW_HIDE);
                UpdateWorkArea(visible);
            }

            // Also hide the Start button if it's a separate window (Windows 10/11 behavior vary)
            IntPtr startBtnWnd = FindWindow("Button", "Start");
            if (startBtnWnd == IntPtr.Zero) 
                startBtnWnd = FindWindow("CommonWindow", null); // Windows 11 start button is often here

            if (startBtnWnd != IntPtr.Zero)
            {
                ShowWindow(startBtnWnd, visible ? SW_SHOW : SW_HIDE);
            }
        }

        private static void UpdateWorkArea(bool showTaskbar)
        {
            IntPtr taskbarWnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarWnd != IntPtr.Zero)
            {
                // The "AutoHide Trick": Windows reclaims space much better when the taskbar is in AutoHide state.
                // Setting it to AutoHide before hiding the window forces the system to recalculate work area.
                APPBARDATA abd = new APPBARDATA();
                abd.cbSize = (uint)Marshal.SizeOf(abd);
                abd.hWnd = taskbarWnd;
                abd.lParam = (IntPtr)(showTaskbar ? ABS_ALWAYSONTOP : ABS_AUTOHIDE);
                SHAppBarMessage(ABM_SETSTATE, ref abd);
            }

            NativeMethods.RECT rect = new NativeMethods.RECT();
            
            if (!showTaskbar)
            {
                IntPtr hMonitor = MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = (uint)Marshal.SizeOf(mi);
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    rect = mi.rcMonitor;
                }
                else
                {
                    rect.left = 0;
                    rect.top = 0;
                    rect.right = GetSystemMetrics(0);
                    rect.bottom = GetSystemMetrics(1);
                }
            }
            else
            {
                IntPtr hMonitor = MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = (uint)Marshal.SizeOf(mi);
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    rect = mi.rcWork;
                }
                else return;
            }

            if (rect.right > 0)
            {
                SystemParametersInfo(SPI_SETWORKAREA, 0, ref rect, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, (IntPtr)SPI_SETWORKAREA, "WorkArea", SMTO_ABORTIFHUNG, 2000, out _);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public NativeMethods.RECT rc;
            public IntPtr lParam;
        }

        private const uint ABM_SETSTATE = 0x0000000A;
        private const int ABS_AUTOHIDE = 0x01;
        private const int ABS_ALWAYSONTOP = 0x02;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public uint cbSize;
            public NativeMethods.RECT rcMonitor;
            public NativeMethods.RECT rcWork;
            public uint dwFlags;
        }

        private const uint MONITOR_DEFAULTTOPRIMARY = 1;
        private const uint HWND_BROADCAST = 0xFFFF;
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        public static void Show() => SetVisibility(true);
        public static void Hide() => SetVisibility(false);
    }
}
