using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace NextValleyDock.Helpers
{
    public class TrayIconData : System.ComponentModel.INotifyPropertyChanged
    {
        public IntPtr Hwnd { get; set; }
        public uint UId { get; set; }
        
        private string _toolTip = string.Empty;
        public string ToolTip 
        { 
            get => _toolTip; 
            set { _toolTip = value; OnPropertyChanged(nameof(ToolTip)); } 
        }

        private WriteableBitmap? _icon;
        public WriteableBitmap? Icon 
        { 
            get => _icon; 
            set { _icon = value; OnPropertyChanged(nameof(Icon)); } 
        }

        public Guid GuidItem { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    public class TrayService
    {
        private static TrayService? _instance;
        public static TrayService Instance => _instance ??= new TrayService();

        public ObservableCollection<TrayIconData> Icons { get; } = new();

        private Thread? _pipeThread;
        private CancellationTokenSource _cts = new();
        private IntPtr _hHook = IntPtr.Zero;
        private Mutex? _watchdogMutex;
        private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string lpLibFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, IntPtr lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string? windowTitle);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint Msg, UIntPtr wParam, IntPtr lParam);

        private const int WH_GETMESSAGE = 3;

        private IntPtr FindShellTrayHwnd(uint explorerPid)
        {
            IntPtr hwnd = IntPtr.Zero;
            while (true)
            {
                hwnd = FindWindowEx(IntPtr.Zero, hwnd, "Shell_TrayWnd", null);
                if (hwnd == IntPtr.Zero) break;

                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == explorerPid)
                    return hwnd;
            }
            return IntPtr.Zero;
        }

        public void Start()
        {
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _cts = new CancellationTokenSource();
            
            bool createdNew;
            _watchdogMutex = new Mutex(true, "Global\\NextValleyTrayHookAlive", out createdNew);

            _pipeThread = new Thread(PipeServerLoop);
            _pipeThread.IsBackground = true;
            _pipeThread.Start();
        }

        public void Stop()
        {
            _cts.Cancel();
            if (_hHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hHook);
                _hHook = IntPtr.Zero;
            }
            _watchdogMutex?.ReleaseMutex();
            _watchdogMutex?.Dispose();
        }

        private async void PipeServerLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    uint explorerPid = (uint)Process.GetProcessesByName("explorer")[0].Id;
                    string dllPath = Path.Combine(AppContext.BaseDirectory, "TrayHook", "TrayHook.dll");

                    if (!File.Exists(dllPath))
                    {
                        Debug.WriteLine($"DLL not found at {dllPath}");
                        await Task.Delay(5000);
                        continue;
                    }

                    using (var pipeServer = new NamedPipeServerStream("nextvalley_tray_monitor", PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous))
                    {
                        IntPtr hDll = LoadLibraryW(dllPath);
                        if (hDll != IntPtr.Zero)
                        {
                            IntPtr hookProc = GetProcAddress(hDll, "GetMsgProc");
                            IntPtr hwndTray = FindShellTrayHwnd(explorerPid);
                            
                            if (hookProc != IntPtr.Zero)
                            {
                                uint tid = GetWindowThreadProcessId(hwndTray, out uint _);
                                _hHook = SetWindowsHookEx(WH_GETMESSAGE, hookProc, hDll, tid);

                                if (_hHook != IntPtr.Zero)
                                {
                                    PostThreadMessage(tid, 0, UIntPtr.Zero, IntPtr.Zero);
                                    Console.WriteLine("Hook injected successfully into Explorer!");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Failed to inject hook. hwndTray: {hwndTray}, hookProc: {hookProc}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Failed to load DLL from {dllPath}. GetLastError: {Marshal.GetLastWin32Error()}");
                        }

                        Console.WriteLine("Waiting for DLL to connect to Pipe...");
                        await pipeServer.WaitForConnectionAsync(_cts.Token);
                        Console.WriteLine("DLL Connected to Pipe!");

                        if (_hHook != IntPtr.Zero)
                        {
                            UnhookWindowsHookEx(_hHook);
                            _hHook = IntPtr.Zero;
                            if (hDll != IntPtr.Zero) FreeLibrary(hDll);
                            
                            uint msg = NativeMethods.RegisterWindowMessage("TaskbarCreated");
                            NativeMethods.PostMessage((IntPtr)0xFFFF, msg, IntPtr.Zero, IntPtr.Zero);
                        }

                        byte[] headerBuffer = new byte[4];
                        while (!_cts.Token.IsCancellationRequested && pipeServer.IsConnected)
                        {
                            int read = await pipeServer.ReadAsync(headerBuffer, 0, 4, _cts.Token);
                            if (read == 0) break;

                            uint msgType = BitConverter.ToUInt32(headerBuffer, 0);
                            if (msgType == 1) // Text
                            {
                                // read text logic... (not strictly needed)
                            }
                            else if (msgType == 2) // COPYDATA
                            {
                                byte[] payloadHeader = new byte[24]; // dwData(8), cbData(4), iconWidth(4), iconHeight(4), iconDataSize(4)
                                await pipeServer.ReadAsync(payloadHeader, 0, 24, _cts.Token);

                                uint cbData = BitConverter.ToUInt32(payloadHeader, 8);
                                uint iconWidth = BitConverter.ToUInt32(payloadHeader, 12);
                                uint iconHeight = BitConverter.ToUInt32(payloadHeader, 16);
                                uint iconDataSize = BitConverter.ToUInt32(payloadHeader, 20);

                                byte[] payload = new byte[cbData];
                                await pipeServer.ReadAsync(payload, 0, (int)cbData, _cts.Token);

                                byte[] iconData = new byte[iconDataSize];
                                if (iconDataSize > 0)
                                {
                                    int totalRead = 0;
                                    while (totalRead < iconDataSize)
                                    {
                                        int r = await pipeServer.ReadAsync(iconData, totalRead, (int)iconDataSize - totalRead, _cts.Token);
                                        if (r == 0) break;
                                        totalRead += r;
                                    }
                                }

                                Console.WriteLine($"Received COPYDATA message: cbData={cbData}, icon={iconWidth}x{iconHeight}, size={iconDataSize}");
                                ProcessMessage(payload, iconData, iconWidth, iconHeight);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Pipe server error: {ex.Message}");
                }

                await Task.Delay(3000);
            }
        }

        private async void ProcessMessage(byte[] payload, byte[] iconData, uint width, uint height)
        {
            // Parse SHELLTRAYDATA
            if (payload.Length < 12) return;
            
            uint dwMessage = BitConverter.ToUInt32(payload, 4);
            // NOTIFYICONDATA starts at offset 8
            if (payload.Length < 8 + 4) return;
            
            uint hWnd = BitConverter.ToUInt32(payload, 8 + 4);
            uint uID = BitConverter.ToUInt32(payload, 8 + 8);
            
            // Extract Tooltip (szTip starts at offset 8 + 24 in 32-bit NOTIFYICONDATA, but wait, struct padding!)
            // We'd need exact struct offsets. Let's just create a generic item for now.
            
            if (_dispatcherQueue == null) return;
            
            await _dispatcherQueue.EnqueueAsync(async () =>
            {
                var existing = default(TrayIconData);
                foreach(var item in Icons) {
                    if (item.Hwnd == (IntPtr)hWnd && item.UId == uID) { existing = item; break; }
                }

                if (dwMessage == 2) // NIM_DELETE
                {
                    if (existing != null) Icons.Remove(existing);
                    return;
                }

                WriteableBitmap? bmp = null;
                if (iconData.Length > 0 && width > 0 && height > 0)
                {
                    bmp = new WriteableBitmap((int)width, (int)height);
                    using (Stream stream = bmp.PixelBuffer.AsStream())
                    {
                        await stream.WriteAsync(iconData, 0, iconData.Length);
                    }
                }

                if (existing != null)
                {
                    if (bmp != null) existing.Icon = bmp;
                    // Trigger UI update
                    int index = Icons.IndexOf(existing);
                    Icons[index] = existing;
                }
                else
                {
                    Icons.Add(new TrayIconData
                    {
                        Hwnd = (IntPtr)hWnd,
                        UId = uID,
                        Icon = bmp,
                        ToolTip = "App"
                    });
                }
            });
        }
    }

    public static class DispatcherQueueExtensions
    {
        public static Task EnqueueAsync(this Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Action action)
        {
            var tcs = new TaskCompletionSource();
            dispatcher.TryEnqueue(() =>
            {
                try { action(); tcs.SetResult(); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }
    }
}
