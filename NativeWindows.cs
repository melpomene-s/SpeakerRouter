using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpeakerRouter;

internal static class NativeWindows
{
    private const int DWMWA_CLOAKED = 14;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        return Screen.AllScreens
            .Select((screen, index) => new MonitorInfo(
                screen.DeviceName,
                GetStableMonitorKey(screen.DeviceName),
                $"{(screen.Primary ? "主屏幕" : "屏幕")} {index + 1}  {screen.Bounds.Width}x{screen.Bounds.Height} @ {screen.Bounds.X},{screen.Bounds.Y}",
                screen.Bounds,
                screen.Primary))
            .ToList();
    }

    public static (IntPtr Handle, string Title, string MonitorDeviceName) FindBestWindowForProcess(int processId)
    {
        foreach (var candidatePid in GetProcessSearchOrder(processId))
        {
            var window = FindBestWindowForExactProcess(candidatePid);
            if (window.Handle != IntPtr.Zero)
            {
                return window;
            }
        }

        var processName = GetProcessName(processId);
        if (!string.IsNullOrWhiteSpace(processName))
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    var window = FindBestWindowForExactProcess(process.Id);
                    if (window.Handle != IntPtr.Zero)
                    {
                        return window;
                    }
                }
            }
        }

        return default;
    }

    private static (IntPtr Handle, string Title, string MonitorDeviceName) FindBestWindowForExactProcess(int processId)
    {
        var candidates = new List<(IntPtr Handle, string Title, long Area)>();

        EnumWindows((hwnd, lParam) =>
        {
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != processId || !IsCandidateWindow(hwnd))
            {
                return true;
            }

            var title = GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            if (!GetWindowRect(hwnd, out var rect))
            {
                return true;
            }

            var width = Math.Max(0, rect.Right - rect.Left);
            var height = Math.Max(0, rect.Bottom - rect.Top);
            candidates.Add((hwnd, title, (long)width * height));
            return true;
        }, IntPtr.Zero);

        var best = candidates.OrderByDescending(item => item.Area).FirstOrDefault();
        if (best.Handle == IntPtr.Zero)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                var handle = process.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    return (handle, process.MainWindowTitle, GetMonitorDeviceName(handle));
                }
            }
            catch
            {
                return default;
            }
        }

        return best.Handle == IntPtr.Zero
            ? default
            : (best.Handle, best.Title, GetMonitorDeviceName(best.Handle));
    }

    private static IEnumerable<int> GetProcessSearchOrder(int processId)
    {
        var seen = new HashSet<int>();
        var current = processId;
        var originalProcessName = GetProcessName(processId);

        for (var depth = 0; depth < 8 && current > 0 && seen.Add(current); depth++)
        {
            yield return current;
            var parent = GetParentProcessId(current);
            if (parent <= 0 || !string.Equals(GetProcessName(parent), originalProcessName, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            current = parent;
        }
    }

    private static int GetParentProcessId(int processId)
    {
        var snapshot = CreateToolhelp32Snapshot(0x00000002, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return 0;
        }

        try
        {
            var entry = new PROCESSENTRY32();
            entry.dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>();
            if (!Process32First(snapshot, ref entry))
            {
                return 0;
            }

            do
            {
                if (entry.th32ProcessID == processId)
                {
                    return (int)entry.th32ParentProcessID;
                }
            }
            while (Process32Next(snapshot, ref entry));

            return 0;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsCandidateWindow(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || GetAncestor(hwnd, 2) != hwnd)
        {
            return false;
        }

        var exStyle = GetWindowLongPtr(hwnd, -20).ToInt64();
        if ((exStyle & 0x00000080L) != 0)
        {
            return false;
        }

        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, Marshal.SizeOf<int>()) == 0 && cloaked != 0)
        {
            return false;
        }

        return true;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetMonitorDeviceName(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return string.Empty;
        }

        var info = new MONITORINFOEX();
        info.cbSize = Marshal.SizeOf<MONITORINFOEX>();
        return GetMonitorInfo(monitor, ref info) ? info.szDevice : string.Empty;
    }

    private static string GetStableMonitorKey(string displayName)
    {
        var device = new DISPLAY_DEVICE();
        device.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        if (EnumDisplayDevices(displayName, 0, ref device, 0) && !string.IsNullOrWhiteSpace(device.DeviceID))
        {
            return device.DeviceID;
        }

        return displayName;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
