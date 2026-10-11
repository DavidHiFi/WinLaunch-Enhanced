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
    public static string CurrentBackdrop { get; private set; } = "Wallpaper";
    public static readonly string[] Backdrops = { "Wallpaper", "Acrylic", "Mica", "Solid" };

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
        // Launchpad layer: glass surfaces over the blurred wallpaper. Dark
        // themes use white glass and dark label shadows, light themes invert.
        var ink = t.Dark ? Colors.White : Colors.Black;
        r["GlassBrush"] = Alpha(ink, t.Dark ? 0.14 : 0.07);
        r["GlassHoverBrush"] = Alpha(ink, t.Dark ? 0.22 : 0.12);
        r["GlassBorderBrush"] = Alpha(ink, t.Dark ? 0.20 : 0.12);
        r["FolderIconBrush"] = Alpha(t.Dark ? Colors.White : Colors.White, t.Dark ? 0.18 : 0.55);
        r["FolderBandBrush"] = new SolidColorBrush(Themes.C(t.Bg)) { Opacity = t.Dark ? 0.78 : 0.82 };
        r["DimBrush"] = Alpha(Colors.Black, t.Dark ? 0.45 : 0.22);
        r["WallScrimBrush"] = new SolidColorBrush(Themes.C(t.Bg)) { Opacity = t.Dark ? 0.34 : 0.42 };
        r["LabelBrush"] = new SolidColorBrush(t.Dark ? Colors.White : Themes.C(t.Text));
        r["LabelShadowColor"] = t.Dark ? Colors.Black : Colors.White;
        r["DeleteBadgeBrush"] = new SolidColorBrush(t.Dark ? WpfColor.FromRgb(0x6C, 0x70, 0x86) : WpfColor.FromRgb(0x8C, 0x8F, 0xA1));
        if (WpfApplication.Current.MainWindow != null)
            (WpfApplication.Current.MainWindow as MainWindow)?.ReapplyComposition();
        try { File.WriteAllText(Path.Combine(store.Directory, "theme.mode"), CurrentTheme); }
        catch { }
    }

    private static void Put(ResourceDictionary r, string key, WpfColor c) => r[key] = c;

    private static SolidColorBrush Alpha(WpfColor c, double opacity)
    {
        var b = new SolidColorBrush(c) { Opacity = opacity };
        b.Freeze();
        return b;
    }

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
                if (Backdrops.Contains(v))
                    return v;
            }
        }
        catch { }
        // No explicit choice yet: follow the legacy theme.xml blur preference.
        // The original drew a blurred wallpaper, so that is the default look.
        if (compat != null && !compat.WantBlur)
            return "Solid";
        return "Wallpaper";
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
