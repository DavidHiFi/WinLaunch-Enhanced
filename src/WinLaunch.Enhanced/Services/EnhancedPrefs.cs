using System.IO;
using System.Text.Json;

namespace WinLaunch.Enhanced.Services;

// Options that only Enhanced understands live in enhanced.json beside the
// shared dataset, so Settings.xml stays readable by the original WinLaunch.
public sealed class EnhancedPrefs
{
    public const string DefaultFont = "FiraCode Nerd Font";

    public string FontFamily { get; set; } = DefaultFont;
    public double TextScale { get; set; } = 1.0;
    public bool LabelShadow { get; set; } = true;
    public double Dim { get; set; } = 0.34;        // wallpaper tint strength
    public double Blur { get; set; } = 28;         // wallpaper blur radius
    public bool CloseOnLaunch { get; set; } = true;
    public bool HideOnEmptyClick { get; set; } = true;
    public bool RankByUse { get; set; } = true;
    public bool Calculator { get; set; } = true;
    public bool OpenAnimation { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public bool IncludePreReleases { get; set; } = false;
    public DateTime? LastUpdateCheck { get; set; } = null;
    public Dictionary<string, int> LaunchCounts { get; set; } = new();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private string _path;

    public static EnhancedPrefs Load(string dataDir)
    {
        var path = Path.Combine(dataDir, "enhanced.json");
        EnhancedPrefs p = null;
        try
        {
            if (File.Exists(path))
                p = JsonSerializer.Deserialize<EnhancedPrefs>(File.ReadAllText(path));
        }
        catch { }
        p ??= new EnhancedPrefs();
        p.LaunchCounts ??= new();
        p._path = path;
        return p;
    }

    public void Save()
    {
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch { }
    }

    public int UsesOf(string key) => key != null && LaunchCounts.TryGetValue(key, out var n) ? n : 0;

    public void CountLaunch(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;
        LaunchCounts[key] = UsesOf(key) + 1;
        Save();
    }
}
