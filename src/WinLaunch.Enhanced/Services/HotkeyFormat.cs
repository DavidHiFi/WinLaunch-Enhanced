using System.Windows.Input;
using WinLaunch.Legacy;

namespace WinLaunch.Enhanced.Services;

// Human-readable shortcut text ("Ctrl + Alt + Shift + ,") for menus and settings.
public static class HotkeyFormat
{
    public static string Describe(LegacySettings s)
    {
        var parts = new List<string>();
        if (s.HotControl) parts.Add("Ctrl");
        if (s.HotAlt) parts.Add("Alt");
        if (s.HotShift) parts.Add("Shift");
        if (s.HotWin) parts.Add("Win");
        parts.Add(KeyName(s.HotKeyExtend));
        return string.Join(" + ", parts);
    }

    public static string KeyName(string key) => key switch
    {
        "OemComma" => ",",
        "OemPeriod" => ".",
        "OemMinus" => "-",
        "OemPlus" => "=",
        "Oem1" or "OemSemicolon" => ";",
        "Oem2" or "OemQuestion" => "/",
        "Oem3" or "OemTilde" => "`",
        "Oem4" or "OemOpenBrackets" => "[",
        "Oem5" or "OemPipe" => "\\",
        "Oem6" or "OemCloseBrackets" => "]",
        "Oem7" or "OemQuotes" => "'",
        "Space" => "Space",
        "Return" => "Enter",
        "Capital" => "Caps Lock",
        null or "" => "?",
        _ when key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1]) => key[1].ToString(),
        _ => key,
    };

    public static bool IsModifier(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;
}
