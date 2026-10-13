using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WinLaunch.Enhanced.Services;

// Live blur behind the launcher, done the way the original WinLaunch does it:
// a per-pixel transparent (layered) window plus the window-composition accent
// policy. The newer DWMWA_SYSTEMBACKDROP_TYPE reports success on a borderless
// fullscreen window but paints nothing there, so it is not used.
public static class AcrylicHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int WCA_ACCENT_POLICY = 19;

    private enum AccentState { Disabled = 0, EnableBlurBehind = 3, EnableAcrylicBlurBehind = 4 }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    // backdrop: "Blur" (classic blur behind), "Acrylic" (blur plus grain and
    // saturation), anything else turns the accent off.
    public static bool Apply(Window window, string backdrop, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return false;
        int darkMode = dark ? 1 : 0;
        try { DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int)); }
        catch { }
        var state = backdrop switch
        {
            "Blur" => AccentState.EnableBlurBehind,
            "Acrylic" => AccentState.EnableAcrylicBlurBehind,
            _ => AccentState.Disabled,
        };
        // Acrylic needs a nonzero gradient alpha or Windows skips the effect;
        // 0x01 leaves the tint to the window's own scrim, as the original does.
        var accent = new AccentPolicy
        {
            AccentState = state,
            GradientColor = state == AccentState.EnableAcrylicBlurBehind ? 0x01FFFFFFu : 0u,
        };
        int size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
            return SetWindowCompositionAttribute(handle, ref data) != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
