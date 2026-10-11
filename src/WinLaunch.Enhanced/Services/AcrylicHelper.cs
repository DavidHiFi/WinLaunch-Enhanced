using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WinLaunch.Enhanced.Services;

// Window composition: DWM system backdrop first (Solid/Acrylic/Mica, like the
// other house apps), legacy acrylic accent as fallback, forced recomposite
// after every change. Re-applied on every show because the live accent dies
// on window recreate; the theme scrim stays readable on its own if all else
// fails, so text never depends on blur.
public static class AcrylicHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_MICA = 2;
    private const int DWMSBT_ACRYLIC = 3;

    private const uint SWP_FLAGS = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020; // NOSIZE|NOMOVE|NOZORDER|NOACTIVATE|FRAMECHANGED

    private enum AccentState { Disabled = 0, EnableAcrylicBlurBehind = 4 }

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

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    public static void ApplyComposition(Window window, string backdrop, bool dark, uint tintArgb = 0xC01E1E2E)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;
            int darkMode = dark ? 1 : 0;
            try { DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int)); }
            catch { }
            bool backdropSet = false;
            if (backdrop != "Solid")
            {
                int type = backdrop == "Mica" ? DWMSBT_MICA : DWMSBT_ACRYLIC;
                try { backdropSet = DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) == 0; }
                catch { }
            }
            else
            {
                try
                {
                    int none = DWMSBT_NONE;
                    DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
                }
                catch { }
            }
            if (backdrop != "Solid" && !backdropSet)
                EnableAcrylic(handle, tintArgb);
            ForceRecomposite(handle);
        }
        catch { }
    }

    public static bool EnableAcrylic(Window window, uint tintArgb = 0xC01E1E2E)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return false;
            return EnableAcrylic(handle, tintArgb);
        }
        catch
        {
            return false;
        }
    }

    private static bool EnableAcrylic(IntPtr handle, uint tintArgb)
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentState.EnableAcrylicBlurBehind,
                AccentFlags = 0,
                GradientColor = tintArgb,
                AnimationId = 0,
            };
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf(accent));
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = 19, // WCA_ACCENT_POLICY
                    Data = ptr,
                    SizeOfData = Marshal.SizeOf(accent),
                };
                return SetWindowCompositionAttribute(handle, ref data) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            return false;
        }
    }

    private static void ForceRecomposite(IntPtr handle)
    {
        try { SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SWP_FLAGS); }
        catch { }
    }
}
