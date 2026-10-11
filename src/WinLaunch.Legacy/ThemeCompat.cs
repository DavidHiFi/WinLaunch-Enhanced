using System.Xml;
using System.Xml.Serialization;

namespace WinLaunch.Legacy;

// Tolerant reader for the original theme.xml. Only the flags Enhanced acts on
// are mapped; everything else (vector folders, text shadows, blur caches) is
// ignored on load and left untouched on disk.
public class ThemeCompat
{
    public bool UseAeroBlur { get; set; } = true;
    public bool UseAcrylic { get; set; } = false;
    public double BackgroundBlurRadius { get; set; } = 0.5;
    public double BackgroundTransparency { get; set; } = 0.25;
    public bool UseCustomBackground { get; set; } = false;

    // Scrim alpha derived from the user's transparency: 0.25 -> 0.75.
    public double ScrimOpacity =>
        Math.Clamp(1.0 - BackgroundTransparency, 0.5, 0.92);

    public bool WantBlur => UseAeroBlur || UseAcrylic;

    public static ThemeCompat Load(string themeDir)
    {
        try
        {
            var path = Path.Combine(themeDir, "theme.xml");
            if (!File.Exists(path))
                return new ThemeCompat();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = XmlReader.Create(fs);
            return (ThemeCompat)new XmlSerializer(typeof(ThemeCompat)).Deserialize(reader);
        }
        catch
        {
            return new ThemeCompat();
        }
    }
}
