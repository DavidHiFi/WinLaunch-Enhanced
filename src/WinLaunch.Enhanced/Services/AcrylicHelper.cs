using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WinLaunch.Enhanced.Services;

// Windows 11 acrylic backdrop behind the overlay. If composition fails for any
// reason the theme scrim stays as the solid fallback; the overlay never depends
// on blur to be readable (all text pairs pass on the flat scrim alone).
public static class AcrylicHelper
{
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

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    public static bool EnableAcrylic(Window window, byte opacity = 0x99)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return false;
            var accent = new AccentPolicy
            {
                AccentState = AccentState.EnableAcrylicBlurBehind,
                AccentFlags = 0,
                GradientColor = (uint)((opacity << 24) | 0x0B0F14),
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
}
