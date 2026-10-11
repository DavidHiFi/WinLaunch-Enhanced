using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace WinLaunch.Enhanced.Services;

// Global show/hide hotkey, registered from the same settings the original app
// reads (modifiers + Key name such as "OemComma"). Event-driven via the window
// proc; zero polling, zero idle CPU.
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x5EED;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource _source;
    private bool _registered;

    public event Action Pressed;

    public bool Register(IntPtr handle, bool alt, bool ctrl, bool shift, bool win, string keyName)
    {
        Unregister();
        uint mods = 0;
        if (alt) mods |= MOD_ALT;
        if (ctrl) mods |= MOD_CONTROL;
        if (shift) mods |= MOD_SHIFT;
        if (win) mods |= MOD_WIN;
        if (mods == 0 || !Enum.TryParse<Key>(keyName, out var key))
            return false;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
            return false;
        if (!RegisterHotKey(handle, HOTKEY_ID, mods, vk))
            return false;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        _registered = true;
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Unregister()
    {
        if (!_registered)
            return;
        try
        {
            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                UnregisterHotKey(_source.Handle, HOTKEY_ID);
            }
        }
        catch { }
        _source = null;
        _registered = false;
    }

    public void Dispose() => Unregister();
}
