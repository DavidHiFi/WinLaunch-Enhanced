using System.Runtime.InteropServices;
using System.Text;

namespace WinLaunch.Enhanced.Services;

// True when the foreground window covers its whole monitor (a game, a video
// player in fullscreen, a presentation), so the launcher stays out of the way.
public static class FullscreenGuard
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    public static bool ForegroundIsFullscreen()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero || h == GetShellWindow() || h == GetDesktopWindow())
                return false;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == Environment.ProcessId)
                return false;
            var cls = new StringBuilder(64);
            GetClassName(h, cls, cls.Capacity);
            var c = cls.ToString();
            if (c is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return false;
            if (!GetWindowRect(h, out var r))
                return false;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref mi))
                return false;
            var m = mi.rcMonitor;
            return r.L <= m.L && r.T <= m.T && r.R >= m.R && r.B >= m.B;
        }
        catch
        {
            return false;
        }
    }
}
