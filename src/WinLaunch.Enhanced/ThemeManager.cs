using System.IO;
using System.Windows;
using WinLaunch.Legacy;
using WpfApplication = System.Windows.Application;

namespace WinLaunch.Enhanced;

// Applies Dark/Light dictionaries plus the user's legacy theme.xml backdrop
// preference (scrim opacity, blur on/off). Theme name persists in a plain
// "theme.mode" file next to the dataset; the original theme.xml is never rewritten.
public static class ThemeManager
{
    public static string CurrentMode { get; private set; } = "Dark";

    public static void Apply(string mode, LegacyStore store, ThemeCompat compat)
    {
        CurrentMode = mode == "Light" ? "Light" : "Dark";
        var dicts = WpfApplication.Current.Resources.MergedDictionaries;
        dicts.Clear();
        dicts.Add(new ResourceDictionary
        {
            Source = new Uri($"/WinLaunch.Enhanced;component/Theme/{CurrentMode}.xaml", UriKind.Relative),
        });
        if (WpfApplication.Current.MainWindow != null)
            ApplyScrim(WpfApplication.Current.MainWindow, compat);
        try { File.WriteAllText(Path.Combine(store.Directory, "theme.mode"), CurrentMode); }
        catch { }
    }

    public static string LoadMode(LegacyStore store)
    {
        try
        {
            var path = Path.Combine(store.Directory, "theme.mode");
            if (File.Exists(path) && File.ReadAllText(path).Trim() == "Light")
                return "Light";
        }
        catch { }
        return "Dark";
    }

    public static void ApplyScrim(Window window, ThemeCompat compat)
    {
        try
        {
            if (window.Background is System.Windows.Media.SolidColorBrush brush)
            {
                var c = brush.Color;
                brush.Opacity = compat.ScrimOpacity;
            }
        }
        catch { }
    }
}
