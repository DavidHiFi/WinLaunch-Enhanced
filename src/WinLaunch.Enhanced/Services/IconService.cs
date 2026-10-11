using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinLaunch.Legacy;

namespace WinLaunch.Enhanced.Services;

// Async, cached icon provider. Decodes each target once, freezes the bitmap so it
// can cross threads, and persists PNGs under IconCache keyed by target+mtime.
// Folders composite up to 4 child icons; unknown targets get an initial-letter
// tile drawn in code, never a fabricated logo.
public static class IconService
{
    public static BitmapSource GetInitialTile(string name, double size = 128)
    {
        var letter = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[0].ToString().ToUpperInvariant();
        var bg = TileColor();
        var fg = MutedColor();
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRoundedRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, size, size), size * 0.22, size * 0.22);
            var text = new FormattedText(letter, System.Globalization.CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI Variable"), size * 0.5,
                new SolidColorBrush(fg), 96);
            ctx.DrawText(text, new System.Windows.Point(
                (size - text.Width) / 2, (size - text.Height) / 2));
        }
        var bmp = new RenderTargetBitmap((int)size, (int)size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static System.Windows.Media.Color TileColor() =>
        TryThemeColor("CardColor", 0x31, 0x32, 0x44);

    private static System.Windows.Media.Color MutedColor() =>
        TryThemeColor("MutedColor", 0x9A, 0xA7, 0xB4);

    private static System.Windows.Media.Color TryThemeColor(string key, byte r, byte g, byte b)
    {
        try
        {
            var app = System.Windows.Application.Current;
            if (app != null && app.Resources[key] is System.Windows.Media.Color c)
                return c;
        }
        catch { }
        return System.Windows.Media.Color.FromRgb(r, g, b);
    }

    public static async Task<BitmapSource> GetIconAsync(ICItem item, LegacyStore store,
        string cacheDir, CancellationToken ct)
    {
        try
        {
            if (item.IsFolder)
                return await Task.Run(() => GetFolderIcon(item, store, cacheDir, ct), ct);
            var target = store.ResolveLinkTarget(item.Application);
            if (target == null)
                return GetInitialTile(item.Name);
            return await Task.Run(() => GetFileIcon(target, cacheDir), ct);
        }
        catch
        {
            return GetInitialTile(item.Name);
        }
    }

    private static BitmapSource GetFileIcon(string target, string cacheDir)
    {
        try
        {
            var key = CacheKey(target);
            var cached = Path.Combine(cacheDir, key + ".png");
            if (File.Exists(cached))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 160; // tiles draw at most ~130 px
                bmp.UriSource = new Uri(cached);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            var src = ShellIcon.Get(target, IconPixels) ?? LegacyIcon(target);
            if (src == null)
                return GetInitialTile(Path.GetFileNameWithoutExtension(target));
            try
            {
                Directory.CreateDirectory(cacheDir);
                using var fs = new FileStream(cached, FileMode.Create);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(src));
                enc.Save(fs);
            }
            catch { }
            return src;
        }
        catch
        {
            return GetInitialTile(Path.GetFileNameWithoutExtension(target));
        }
    }

    // Tiles draw icons at 70 x IconSize (116 px at the user's 1.65), so ask the
    // shell for its 256 px jumbo image instead of the 32 px associated icon.
    public const int IconPixels = 256;

    private static BitmapSource LegacyIcon(string target)
    {
        using var icon = Icon.ExtractAssociatedIcon(target);
        if (icon == null)
            return null;
        using var bmpIcon = icon.ToBitmap();
        var hbmp = bmpIcon.GetHbitmap();
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { ShellIcon.DeleteObject(hbmp); }
    }

    private static BitmapSource GetFolderIcon(ICItem folder, LegacyStore store,
        string cacheDir, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(folder.IconPath))
        {
            var direct = store.ResolveLinkTarget(folder.IconPath) ?? folder.IconPath;
            if (File.Exists(direct))
                return GetFileIcon(direct, cacheDir);
        }
        var kids = new List<(ICItem kid, string target)>();
        foreach (var k in folder.Items ?? new List<ICItem>())
        {
            ct.ThrowIfCancellationRequested();
            if (k.IsFolder)
                continue;
            var target = store.ResolveLinkTarget(k.Application);
            if (target != null && (File.Exists(target) || Directory.Exists(target)))
                kids.Add((k, target));
            if (kids.Count == 9)
                break;
        }
        if (kids.Count == 0)
            return GetInitialTile(folder.Name);
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                null, new Rect(0, 0, 128, 128), 28, 28);
            int i = 0;
            foreach (var (kid, target) in kids)
            {
                ct.ThrowIfCancellationRequested();
                var src = GetFileIcon(target, cacheDir);
                double x = 14 + (i % 3) * 34, y = 14 + (i / 3) * 34;
                ctx.DrawImage(src, new Rect(x, y, 32, 32));
                i++;
            }
        }
        var out_ = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
        out_.Render(visual);
        out_.Freeze();
        return out_;
    }

    private static string CacheKey(string target)
    {
        var mtime = File.Exists(target)
            ? File.GetLastWriteTimeUtc(target).Ticks.ToString()
            : (System.IO.Directory.Exists(target) ? "dir" : "missing");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("v3-256|" + target + "|" + mtime));
        return Convert.ToHexString(bytes)[..16];
    }
}
