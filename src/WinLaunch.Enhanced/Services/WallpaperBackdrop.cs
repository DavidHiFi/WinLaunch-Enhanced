using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace WinLaunch.Enhanced.Services;

// The original WinLaunch look: the desktop wallpaper, heavily blurred, under
// the icons. Blurred once into a small frozen bitmap and stretched, so the
// overlay costs one textured quad per frame instead of a live blur shader.
public static class WallpaperBackdrop
{
    private const int SPI_GETDESKWALLPAPER = 0x0073;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(int action, int param,
        System.Text.StringBuilder value, int winIni);

    private static string _cachedPath;
    private static DateTime _cachedStamp;
    private static double _cachedRadius;
    private static BitmapSource _cached;

    public static string CurrentPath()
    {
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            if (SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0) &&
                File.Exists(sb.ToString()))
                return sb.ToString();
        }
        catch { }
        var transcoded = System.IO.Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        return File.Exists(transcoded) ? transcoded : null;
    }

    // Returns null when there is no readable wallpaper (solid color desktops);
    // the caller then shows the theme background.
    public static BitmapSource GetBlurred(string path = null, double radius = 28)
    {
        path ??= CurrentPath();
        if (path == null)
            return null;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            radius = Math.Clamp(radius, 0, 80);
            if (_cached != null && path == _cachedPath && stamp == _cachedStamp && radius == _cachedRadius)
                return _cached;

            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            img.DecodePixelWidth = 640;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                img.StreamSource = fs;
                img.EndInit();
            }
            img.Freeze();

            const int w = 640;
            int h = Math.Max(1, (int)Math.Round(w * img.PixelHeight / (double)img.PixelWidth));
            // Draw oversized so the blur has no transparent fringe at the edges.
            var host = new System.Windows.Controls.Grid { Width = w, Height = h, ClipToBounds = true };
            var rect = new Rectangle
            {
                Width = w + 80, Height = h + 80, Margin = new Thickness(-40),
                Fill = new ImageBrush(img) { Stretch = Stretch.UniformToFill },
                Effect = radius > 0 ? new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian } : null,
            };
            host.Children.Add(rect);
            host.Measure(new Size(w, h));
            host.Arrange(new Rect(0, 0, w, h));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(host);
            rtb.Freeze();
            _cached = rtb;
            _cachedPath = path;
            _cachedStamp = stamp;
            _cachedRadius = radius;
            return rtb;
        }
        catch
        {
            return null;
        }
    }
}
