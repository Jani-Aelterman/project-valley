using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Linq;
using Windows.Storage.Streams;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace NextValleyDock.Helpers
{
    public class WindowInfo : System.ComponentModel.INotifyPropertyChanged
    {
        private Microsoft.UI.Xaml.Media.ImageSource? _icon;
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public string ExePath { get; set; } = string.Empty;
        public Microsoft.UI.Xaml.Media.ImageSource? Icon 
        { 
            get => _icon; 
            set 
            { 
                if (_icon != value)
                {
                    _icon = value;
                    OnPropertyChanged(nameof(Icon));
                }
            } 
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    public class RunningAppsService
    {
        private static RunningAppsService? _instance;
        public static RunningAppsService Instance => _instance ??= new RunningAppsService();

        public ObservableCollection<WindowInfo> RunningApps { get; } = new ObservableCollection<WindowInfo>();

        private readonly DispatcherQueueTimer _timer;
        private readonly DispatcherQueue _dispatcherQueue;

        public IntPtr SelfHandle { get; set; } = IntPtr.Zero;

        private RunningAppsService()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _timer = _dispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(2);
            _timer.Tick += (s, e) => RefreshWindows();
            _timer.Start();
            
            RefreshWindows();
        }

        public void RefreshWindows()
        {
            var detectedWindows = new List<(IntPtr hWnd, string title, string processName, string exePath)>();
            uint currentProcessId = (uint)Process.GetCurrentProcess().Id;

            try {
                EnumWindows((hWnd, lParam) =>
                {
                    try {
                        if (hWnd == SelfHandle) return true;
                        
                        if (IsWindowVisible(hWnd))
                        {
                            GetWindowThreadProcessId(hWnd, out uint processId);
                            if (currentProcessId != 0 && processId == currentProcessId) return true;

                            if (IsAppWindow(hWnd))
                            {
                                string exePath = GetProcessPath(processId);
                                string processName = !string.IsNullOrEmpty(exePath) ? Path.GetFileNameWithoutExtension(exePath) : "Unknown";
                                string title = GetWindowTitle(hWnd);
                                if (string.IsNullOrEmpty(title)) title = processName;

                                if (title != "Unknown")
                                {
                                    detectedWindows.Add((hWnd, title, processName, exePath));
                                }
                            }
                        }
                    } catch { }
                    return true;
                }, IntPtr.Zero);
            } catch { }

            // Group by ExePath to ensure each app only appears once
            var groupedApps = detectedWindows
                .GroupBy(d => string.IsNullOrEmpty(d.exePath) ? d.hWnd.ToString() : d.exePath)
                .Select(g => g.First())
                .ToList();

            UpdateCollection(groupedApps);
        }

        private bool IsAppWindow(IntPtr hWnd)
        {
            // 1. Basic Visibility
            if (!IsWindowVisible(hWnd)) return false;

            // 2. Class Filtering (Shell & System)
            string className = GetClassName(hWnd);
            if (string.IsNullOrEmpty(className)) return false;
            
            string[] shellClasses = { 
                "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", 
                "Windows.UI.Core.CoreWindow", "SearchHost.exe", "ShellExperienceHost.exe",
                "ApplicationFrameWindow", "ThumbnailDeviceHelperWnd"
            };
            if (shellClasses.Contains(className)) return false;

            // 3. Cloaking Check (Windows 10/11 ghost windows)
            if (DwmGetWindowAttribute(hWnd, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) == 0)
            {
                if (cloaked != 0) return false;
            }

            // 4. Style Check
            int style = GetWindowLong(hWnd, -16 /* GWL_STYLE */);
            if ((style & 0x40000000 /* WS_CHILD */) != 0) return false;

            int exStyle = GetWindowLong(hWnd, -20 /* GWL_EXSTYLE */);
            bool isAppWindow = (exStyle & 0x00040000 /* WS_EX_APPWINDOW */) != 0;
            bool isToolWindow = (exStyle & 0x00000080 /* WS_EX_TOOLWINDOW */) != 0;

            // 5. Owner Check
            IntPtr owner = GetWindow(hWnd, 4 /* GW_OWNER */);

            // A window is a primary app window if:
            // (It has WS_EX_APPWINDOW) OR (It is NOT a ToolWindow AND has NO OWNER)
            bool shouldShow = isAppWindow || (!isToolWindow && owner == IntPtr.Zero);
            if (!shouldShow) return false;

            // 6. Title Check (Ignore windows without a meaningful title)
            string title = GetWindowTitle(hWnd);
            if (string.IsNullOrEmpty(title) || title == "Unknown") return false;

            return true;
        }

        private void UpdateCollection(List<(IntPtr hWnd, string title, string processName, string exePath)> detected)
        {
            var handled = new HashSet<IntPtr>();
            for (int i = RunningApps.Count - 1; i >= 0; i--)
            {
                if (!detected.Any(d => d.hWnd == RunningApps[i].Handle)) { RunningApps.RemoveAt(i); }
                else { handled.Add(RunningApps[i].Handle); }
            }

            foreach (var d in detected)
            {
                if (!handled.Contains(d.hWnd))
                {
                    var newApp = new WindowInfo { Handle = d.hWnd, Title = d.title, ProcessName = d.processName, ExePath = d.exePath };
                    RunningApps.Add(newApp);
                    LoadIconAsync(newApp);
                }
            }
        }

        private async void LoadIconAsync(WindowInfo app)
        {
            try
            {
                var iconData = await System.Threading.Tasks.Task.Run(() => ExtractIconData(app.Handle, app.ExePath));
                if (iconData != null)
                {
                    _dispatcherQueue.TryEnqueue(async () =>
                    {
                        var bitmapImage = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                        using var ms = new InMemoryRandomAccessStream();
                        using (var writer = new DataWriter(ms.GetOutputStreamAt(0)))
                        {
                            writer.WriteBytes(iconData);
                            await writer.StoreAsync();
                            writer.DetachStream();
                        }
                        ms.Seek(0);
                        await bitmapImage.SetSourceAsync(ms);
                        app.Icon = bitmapImage;
                    });
                }
            } catch { }
        }

        private byte[]? ExtractIconData(IntPtr hWnd, string exePath)
        {
            try
            {
                // 1. UWP Resolution
                if (!string.IsNullOrEmpty(exePath) && exePath.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase))
                {
                    var uwpData = TryGetUwpIcon(exePath);
                    if (uwpData != null) return uwpData;
                }

                IntPtr hIcon = IntPtr.Zero;

                // 2. High-Fidelity Resource Extraction (PrivateExtractIcons)
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    IntPtr[] hIcons = new IntPtr[1];
                    IntPtr[] ids = new IntPtr[1];
                    if (PrivateExtractIcons(exePath, 0, 256, 256, hIcons, ids, 1, 0) > 0 && hIcons[0] != IntPtr.Zero)
                    {
                        hIcon = hIcons[0];
                    }
                }

                // 3. System Image List (Jumbo)
                if (hIcon == IntPtr.Zero && !string.IsNullOrEmpty(exePath))
                {
                    SHFILEINFO shinfo = new SHFILEINFO();
                    if (SHGetFileInfo(exePath, 0, ref shinfo, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 0x000004000 /* SHGFI_SYSICONINDEX */) != IntPtr.Zero)
                    {
                        Guid iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                        if (SHGetImageList(4 /* SHIL_JUMBO */, ref iid, out IImageList iml) == 0 && iml != null)
                        {
                            iml.GetIcon(shinfo.iIcon, 1 /* ILD_TRANSPARENT */, out hIcon);
                        }
                    }
                }

                // 4. Message Fallback
                if (hIcon == IntPtr.Zero)
                {
                    hIcon = SendMessage(hWnd, 0x007F /* WM_GETICON */, (IntPtr)1 /* ICON_BIG */, IntPtr.Zero);
                    if (hIcon == IntPtr.Zero) hIcon = GetClassLongPtr(hWnd, -14 /* GCLP_HICON */);
                }

                if (hIcon != IntPtr.Zero)
                {
                    byte[]? data = ExtractRawPixels(hIcon);
                    
                    if (data == null)
                    {
                        try {
                            using (var icon = System.Drawing.Icon.FromHandle(hIcon))
                            using (var bmp = icon.ToBitmap())
                            using (var ms = new MemoryStream()) {
                                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                data = ms.ToArray();
                            }
                        } catch { }
                    }

                    DestroyIcon(hIcon);
                    return data;
                }
            } catch { }
            return null;
        }

        private byte[]? ExtractRawPixels(IntPtr hIcon)
        {
            ICONINFOEX info = new ICONINFOEX { cbSize = (uint)Marshal.SizeOf(typeof(ICONINFOEX)) };
            if (!GetIconInfoEx(hIcon, ref info)) return null;

            IntPtr hdc = IntPtr.Zero;
            try
            {
                if (info.hbmColor == IntPtr.Zero) return null;
                BITMAPINFO bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                hdc = CreateCompatibleDC(IntPtr.Zero);
                if (GetDIBits(hdc, info.hbmColor, 0, 0, IntPtr.Zero, ref bmi, 0) == 0) return null;

                int width = bmi.bmiHeader.biWidth;
                int height = Math.Abs(bmi.bmiHeader.biHeight);
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0;
                bmi.bmiHeader.biHeight = -height; 

                byte[] bits = new byte[width * height * 4];
                var handle = GCHandle.Alloc(bits, GCHandleType.Pinned);
                try {
                    if (GetDIBits(hdc, info.hbmColor, 0, (uint)height, handle.AddrOfPinnedObject(), ref bmi, 0) > 0)
                    {
                        using (var bitmap = new System.Drawing.Bitmap(width, height, width * 4, System.Drawing.Imaging.PixelFormat.Format32bppArgb, handle.AddrOfPinnedObject()))
                        using (var ms = new MemoryStream()) {
                            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            return ms.ToArray();
                        }
                    }
                } finally { handle.Free(); }
            } catch { }
            finally
            {
                if (hdc != IntPtr.Zero) DeleteDC(hdc);
                if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
                if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
            }
            return null;
        }

        private byte[]? TryGetUwpIcon(string exePath)
        {
            try {
                string pack = exePath;
                while (!string.IsNullOrEmpty(pack) && !File.Exists(Path.Combine(pack, "AppxManifest.xml"))) pack = Path.GetDirectoryName(pack) ?? string.Empty;
                if (string.IsNullOrEmpty(pack)) return null;

                string manifest = Path.Combine(pack, "AppxManifest.xml");
                if (File.Exists(manifest)) {
                    string content = File.ReadAllText(manifest);
                    var match = System.Text.RegularExpressions.Regex.Match(content, "<Logo>(.*?)</Logo>");
                    if (!match.Success) match = System.Text.RegularExpressions.Regex.Match(content, "Logo=\"(.*?)\"");
                    if (match.Success) {
                        string rel = match.Groups[1].Value;
                        string logo = Path.Combine(pack, rel);
                        string folder = Path.GetDirectoryName(logo) ?? string.Empty;
                        string fn = Path.GetFileNameWithoutExtension(logo);
                        string[] cans = { $"{fn}.targetsize-256.png", $"{fn}.targetsize-48.png", $"{fn}.scale-200.png", fn + Path.GetExtension(logo) };
                        foreach (var c in cans) {
                            string full = Path.Combine(folder, c);
                            if (File.Exists(full)) return File.ReadAllBytes(full);
                        }
                    }
                }
            } catch { }
            return null;
        }

        private string GetClassName(IntPtr hWnd) { StringBuilder sb = new StringBuilder(256); GetClassNameWin32(hWnd, sb, sb.Capacity); return sb.ToString(); }
        private string GetWindowTitle(IntPtr hWnd) { int len = GetWindowTextLength(hWnd); if (len == 0) return ""; StringBuilder sb = new StringBuilder(len + 1); GetWindowText(hWnd, sb, sb.Capacity); return sb.ToString(); }
        private string GetProcessPath(uint pid) {
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h != IntPtr.Zero) {
                try { StringBuilder sb = new StringBuilder(1024); int sz = sb.Capacity; if (QueryFullProcessImageName(h, 0, sb, ref sz)) return sb.ToString(); }
                finally { CloseHandle(h); }
            }
            return "";
        }

        #region Win32
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lp, IntPtr lp2);
        private delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int n);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint u);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr(IntPtr h, int n);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint PrivateExtractIcons(string f, int i, int cx, int cy, IntPtr[] h, IntPtr[] id, uint n, uint fl);
        [DllImport("shell32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SHGetFileInfo(string p, uint a, ref SHFILEINFO s, uint c, uint f);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint dwAttributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName; }
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint a, bool i, uint p);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "GetClassName")] private static extern int GetClassNameWin32(IntPtr h, StringBuilder s, int n);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int c);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr h, int f, StringBuilder s, ref int sz);
        [DllImport("shell32.dll", EntryPoint = "#727")] private static extern int SHGetImageList(int i, ref Guid r, out IImageList p);
        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList { [PreserveSig] int GetIcon(int i, int f, out IntPtr p); }
        [DllImport("user32.dll")] private static extern bool GetIconInfoEx(IntPtr h, ref ICONINFOEX i);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr h);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr h);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
        [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint s, uint c, IntPtr l, ref BITMAPINFO b, uint u);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct ICONINFOEX { public uint cbSize; public bool fIcon; public uint xHotspot; public uint yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; public ushort wResID; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szModName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szResName; }
        [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }
        [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount; public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter; public uint biClrUsed; public uint biClrImportant; }
        #endregion
    }
}
