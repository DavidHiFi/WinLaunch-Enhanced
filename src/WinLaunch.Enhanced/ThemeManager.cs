using System.IO;
using System.Windows;
using System.Windows.Media;
using WinLaunch.Enhanced.Theme;
using WinLaunch.Legacy;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace WinLaunch.Enhanced;

// Publishes the active house theme as app resources so every DynamicResource
// repaints live. Theme names persist in "theme.mode" beside the dataset; the
// original theme.xml backdrop preference is only read, never rewritten.
public static class ThemeManager
{
    public static string CurrentTheme { get; private set; } = Themes.Default;
    public static string CurrentBackdrop { get; private set; } = "Wallpaper";
    public static readonly string[] Backdrops = { "Wallpaper", "Blur", "Acrylic", "Solid" };

    private static WpfColor _shadowColor = Colors.Black;
    private static bool _labelShadow = true;

    public static void ApplyPrefs(Services.EnhancedPrefs p)
    {
        ApplyFont(p.FontFamily, p.TextScale);
        _labelShadow = p.LabelShadow;
        UpdateLabelEffect();
    }

    // One frozen shadow shared by every tile label; null turns shadows off.
    private static void UpdateLabelEffect()
    {
        var r = WpfApplication.Current.Resources;
        if (!_labelShadow)
        {
            r["LabelEffect"] = null;
            return;
        }
        var fx = new System.Windows.Media.Effects.DropShadowEffect
        {
            ShadowDepth = 1, BlurRadius = 5, Opacity = 0.85, Color = _shadowColor,
        };
        fx.Freeze();
        r["LabelEffect"] = fx;
    }

    public static string CurrentFont { get; private set; } = Services.EnhancedPrefs.DefaultFont;
    public static double TextScale { get; private set; } = 1.0;

    // The chosen font with Segoe UI Variable behind it, so a missing font or a
    // missing glyph falls back to the system face instead of boxes.
    public static void ApplyFont(string family, double scale)
    {
        CurrentFont = string.IsNullOrWhiteSpace(family) ? Services.EnhancedPrefs.DefaultFont : family.Trim();
        TextScale = Math.Clamp(scale <= 0 ? 1 : scale, 0.8, 1.4);
        var r = WpfApplication.Current.Resources;
        r["UiFont"] = new FontFamily($"{CurrentFont}, Segoe UI Variable Text, Segoe UI");
        r["TextScale"] = TextScale;
    }

    // Installed families, including per-user fonts that the machine-wide
    // list leaves out (Nerd Fonts installed without admin rights land there).
    public static List<string> InstalledFonts()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(IEnumerable<FontFamily> fams)
        {
            foreach (var f in fams)
            {
                var n = f.FamilyNames.TryGetValue(System.Windows.Markup.XmlLanguage.GetLanguage("en-us"), out var en)
                    ? en : f.Source;
                if (!string.IsNullOrWhiteSpace(n) && !n.StartsWith("@"))
                    names.Add(n.Contains('#') ? n[(n.LastIndexOf('#') + 1)..] : n);
            }
        }
        try { Add(Fonts.SystemFontFamilies); } catch { }
        try
        {
            var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Fonts");
            if (Directory.Exists(user))
                Add(Fonts.GetFontFamilies(user + Path.DirectorySeparatorChar));
        }
        catch { }
        return names.ToList();
    }

    public static void EnsureStyles()
    {
        var dicts = WpfApplication.Current.Resources.MergedDictionaries;
        if (dicts.Any(d => d.Source != null &&
            d.Source.OriginalString.EndsWith("Theme/Styles.xaml")))
            return;
        dicts.Add(new ResourceDictionary
        {
            Source = new Uri("/WinLaunchEnhanced;component/Theme/Styles.xaml", UriKind.Relative),
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
        r["WallScrimBrush"] = new SolidColorBrush(Themes.C(t.Bg));
        // Board labels sit on the wallpaper, whose brightness the theme cannot
        // know, so every theme uses Launchpad's white text with a dark shadow.
        // The folder band is a theme surface and uses the theme text instead.
        r["LabelBrush"] = new SolidColorBrush(Colors.White);
        r["LabelShadowColor"] = Colors.Black;
        _shadowColor = Colors.Black;
        UpdateLabelEffect();
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
                if (v == "Mica")
                    v = "Blur"; // Mica cannot render behind a fullscreen overlay
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
}
