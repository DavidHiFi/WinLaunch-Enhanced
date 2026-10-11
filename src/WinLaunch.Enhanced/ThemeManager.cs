using System.IO;
using System.Windows;
using System.Windows.Media;
using WinLaunch.Enhanced.Theme;
using WinLaunch.Legacy;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;

namespace WinLaunch.Enhanced;

// Publishes the active house theme as app resources so every DynamicResource
// repaints live. Theme names persist in "theme.mode" beside the dataset; the
// original theme.xml backdrop preference is only read, never rewritten.
public static class ThemeManager
{
    public static string CurrentTheme { get; private set; } = Themes.Default;
    public static string CurrentBackdrop { get; private set; } = "Acrylic";

    public static void EnsureStyles()
    {
        var dicts = WpfApplication.Current.Resources.MergedDictionaries;
        if (dicts.Any(d => d.Source != null &&
            d.Source.OriginalString.EndsWith("Theme/Styles.xaml")))
            return;
        dicts.Add(new ResourceDictionary
        {
            Source = new Uri("/WinLaunch.Enhanced;component/Theme/Styles.xaml", UriKind.Relative),
        });
    }

    public static void Apply(string themeName, LegacyStore store, ThemeCompat compat)
    {
        CurrentTheme = Themes.Resolve(themeName);
        CurrentBackdrop = LoadBackdrop(store, compat);
        var t = Themes.All[CurrentTheme];
        var r = WpfApplication.Current.Resources;
        Put(r, "BgColor", Themes.C(t.Bg));
        Put(r, "CardColor", Themes.C(t.Card));
        Put(r, "TextColor", Themes.C(t.Text));
        Put(r, "MutedColor", Themes.C(t.Sub));
        Put(r, "AccentColor", Themes.C(t.Accent));
        Put(r, "AccentInkColor", Themes.C(t.OnAccent));
        r["ScrimBrush"] = new SolidColorBrush(Themes.C(t.Bg)) { Opacity = compat.ScrimOpacity };
        r["SolidBgBrush"] = new SolidColorBrush(Themes.C(t.Bg));
        r["CardBrush"] = new SolidColorBrush(Themes.C(t.Card));
        r["CardBorderBrush"] = new SolidColorBrush(Themes.C(t.CardBorder));
        r["FieldBrush"] = new SolidColorBrush(Themes.C(t.Field));
        r["HoverBrush"] = new SolidColorBrush(Themes.C(t.Hover));
        r["PressBrush"] = new SolidColorBrush(Themes.C(t.Press));
        r["TextBrush"] = new SolidColorBrush(Themes.C(t.Text));
        r["MutedBrush"] = new SolidColorBrush(Themes.C(t.Sub));
        r["AccentBrush"] = new SolidColorBrush(Themes.C(t.Accent));
        r["AccentInkBrush"] = new SolidColorBrush(Themes.C(t.OnAccent));
        r["AccentHoverBrush"] = new SolidColorBrush(Themes.C(t.AccentHover));
        r["WarnBrush"] = new SolidColorBrush(Themes.C(t.Warn));
        if (WpfApplication.Current.MainWindow != null)
            ApplyScrim(WpfApplication.Current.MainWindow, compat);
        try { File.WriteAllText(Path.Combine(store.Directory, "theme.mode"), CurrentTheme); }
        catch { }
    }

    private static void Put(ResourceDictionary r, string key, WpfColor c) => r[key] = c;

    public static string LoadThemeName(LegacyStore store)
    {
        try
        {
            var path = Path.Combine(store.Directory, "theme.mode");
            if (File.Exists(path))
                return Themes.Resolve(File.ReadAllText(path));
        }
        catch { }
        return Themes.Default;
    }

    public static string LoadBackdrop(LegacyStore store, ThemeCompat compat = null)
    {
        try
        {
            var path = Path.Combine(store.Directory, "backdrop.mode");
            if (File.Exists(path))
            {
                var v = File.ReadAllText(path).Trim();
                if (v == "Solid" || v == "Acrylic" || v == "Mica")
                    return v;
            }
        }
        catch { }
        // No explicit choice yet: follow the legacy theme.xml blur preference.
        if (compat != null && !compat.WantBlur)
            return "Solid";
        return "Acrylic";
    }

    public static void SaveBackdrop(LegacyStore store, string mode)
    {
        CurrentBackdrop = mode;
        try { File.WriteAllText(Path.Combine(store.Directory, "backdrop.mode"), mode); }
        catch { }
    }

    public static bool IsDark() =>
        Themes.All.TryGetValue(CurrentTheme, out var t) && t.Dark;

    public static void ApplyScrim(Window window, ThemeCompat compat)
    {
        try
        {
            var brush = WpfApplication.Current.Resources["ScrimBrush"] as SolidColorBrush;
            if (brush != null)
            {
                brush.Opacity = compat.ScrimOpacity;
                window.Background = brush;
            }
        }
        catch { }
    }
}
