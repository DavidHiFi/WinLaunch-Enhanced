using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WinLaunch.Enhanced.Services;

// Large icons through IShellItemImageFactory, the API Explorer uses for its
// "extra large" view. Returns null when the shell has nothing, so callers can
// fall back to the associated icon.
internal static class ShellIcon
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    private const int SIIGBF_BIGGERSIZEOK = 0x1;
    private const int SIIGBF_ICONONLY = 0x4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(IntPtr hObject);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string iconFile, int index, uint flags,
        out IntPtr large, out IntPtr small, uint size);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    private static readonly System.Text.RegularExpressions.Regex MscIcon = new(
        @"<Icon\s+Index=""(-?\d+)""\s+File=""([^""]+)""",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    // Management consoles (.msc) name their icon inside the file, as an index
    // into a system DLL. The image factory ignores that and returns a blank
    // page, so read the reference and extract the icon at full size.
    public static BitmapSource FromMsc(string path, int pixels)
    {
        if (!path.EndsWith(".msc", StringComparison.OrdinalIgnoreCase))
            return null;
        IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
        try
        {
            string head;
            using (var reader = new System.IO.StreamReader(path))
            {
                var buf = new char[64 * 1024];
                head = new string(buf, 0, reader.ReadBlock(buf, 0, buf.Length));
            }
            var m = MscIcon.Match(head);
            if (!m.Success)
                return null;
            var file = Environment.ExpandEnvironmentVariables(m.Groups[2].Value);
            int index = int.Parse(m.Groups[1].Value);
            if (SHDefExtractIcon(file, index, 0, out large, out small, (uint)pixels) != 0 || large == IntPtr.Zero)
                return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            bgra.CopyPixels(px, stride, 0);
            var full = BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, px, stride);
            var result = TrimPadding(full, px, w, h, stride);
            result.Freeze();
            return result;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (large != IntPtr.Zero) DestroyIcon(large);
            if (small != IntPtr.Zero) DestroyIcon(small);
        }
    }

    public static BitmapSource Get(string path, int pixels)
    {
        IShellItemImageFactory factory = null;
        IntPtr hbmp = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero,
                typeof(IShellItemImageFactory).GUID, out factory);
            if (factory.GetImage(new SIZE { cx = pixels, cy = pixels },
                    SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hbmp) != 0 || hbmp == IntPtr.Zero)
                return null;
            var src = FromDib(hbmp) ?? Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero,
                Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hbmp != IntPtr.Zero)
                DeleteObject(hbmp);
            if (factory != null)
                Marshal.ReleaseComObject(factory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
        public uint biSize; public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
        public uint dsBitfields0, dsBitfields1, dsBitfields2;
        public IntPtr dshSection; public uint dsOffset;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int size, out DIBSECTION ds);

    // The shell returns a premultiplied 32 bpp DIB section. Reading its bits
    // directly keeps the alpha channel that CreateBitmapSourceFromHBitmap drops
    // (which is what paints black corners around icons).
    private static BitmapSource FromDib(IntPtr hbmp)
    {
        if (GetObject(hbmp, Marshal.SizeOf<DIBSECTION>(), out var ds) == 0 ||
            ds.bmBits == IntPtr.Zero || ds.bmBitsPixel != 32)
            return null;
        int w = ds.bmWidth, h = Math.Abs(ds.bmHeight), stride = ds.bmWidthBytes;
        var px = new byte[stride * h];
        Marshal.Copy(ds.bmBits, px, 0, px.Length);
        bool anyAlpha = false;
        for (int i = 3; i < px.Length; i += 4)
            if (px[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha)
            for (int i = 3; i < px.Length; i += 4) px[i] = 0xFF;
        if (ds.biHeight > 0)
        {
            // Bottom-up DIB: flip rows.
            var flipped = new byte[px.Length];
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(px, y * stride, flipped, (h - 1 - y) * stride, stride);
            px = flipped;
        }
        var full = BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, px, stride);
        return TrimPadding(full, px, w, h, stride);
    }

    // Apps that ship only 32 or 48 px icons come back as a small glyph in the
    // middle of an empty 256 px canvas, sometimes inside a thin frame the
    // shell draws at the canvas edge. Crop those to their content so every
    // tile shows its icon at the same visual size. The edge band is skipped
    // when measuring, so the frame never counts as content.
    private static BitmapSource TrimPadding(BitmapSource src, byte[] px, int w, int h, int stride)
    {
        int edge = Math.Max(2, w / 32);
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = edge; y < h - edge; y++)
            for (int x = edge; x < w - edge; x++)
                if (px[y * stride + x * 4 + 3] > 16)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0)
            return src;
        int side = Math.Max(maxX - minX + 1, maxY - minY + 1);
        if (side >= w * 0.6)
            return src;
        side = Math.Min(w, (int)Math.Ceiling(side * 1.06) + 2);
        int cx = (minX + maxX + 1) / 2, cy = (minY + maxY + 1) / 2;
        int left = Math.Clamp(cx - side / 2, 0, w - side);
        int top = Math.Clamp(cy - side / 2, 0, h - side);
        var crop = new CroppedBitmap(src, new Int32Rect(left, top, side, side));
        crop.Freeze();
        return crop;
    }
}
