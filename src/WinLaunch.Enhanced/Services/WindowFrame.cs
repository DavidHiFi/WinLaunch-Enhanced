using System.Runtime.InteropServices;

namespace WinLaunch.Enhanced.Services;

// Win32 window setup for the fullscreen overlay: square corners and no border
// on Windows 11, constant-alpha fading, and the DWM glass frame that lets the
// live Blur and Acrylic backdrops show through WPF's transparent clear color.
public static class WindowFrame
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_LAYERED = 0x80000;
    private const uint LWA_ALPHA = 0x2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_DONOTROUND = 1;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    public static void Prepare(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        try
        {
            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_LAYERED));
            int round = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int none = unchecked((int)DWMWA_COLOR_NONE);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref none, sizeof(int));
        }
        catch { }
    }

    public static void SetAlpha(IntPtr hwnd, double alpha)
    {
        try { SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), LWA_ALPHA); }
        catch { }
    }

    public static void ExtendGlass(IntPtr hwnd, bool on)
    {
        var m = on ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new Margins();
        try { DwmExtendFrameIntoClientArea(hwnd, ref m); }
        catch { }
    }
}
