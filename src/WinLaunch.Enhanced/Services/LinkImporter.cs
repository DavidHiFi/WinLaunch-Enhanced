using System.IO;
using WinLaunch.Legacy;

namespace WinLaunch.Enhanced.Services;

// Imports .lnk/.exe drops into the shared LinkCache with GUID names, exactly like
// the original app, so entries stay readable by both apps.
public static class LinkImporter
{
    public static ICItem Import(string path, LegacyStore store)
    {
        Directory.CreateDirectory(store.LinkCachePath);
        var id = Guid.NewGuid().ToString("D") + ".lnk";
        var dest = Path.Combine(store.LinkCachePath, id);
        if (Directory.Exists(path))
        {
            MakeShortcut(dest, path, "");
            return new ICItem
            {
                Name = new DirectoryInfo(path).Name,
                Application = id,
                GridIndex = -1,
            };
        }
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(path, dest, overwrite: true);
        }
        else
        {
            MakeShortcut(dest, path, Path.GetDirectoryName(path) ?? "");
        }
        return new ICItem
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Application = id,
            GridIndex = -1,
        };
    }

    // A website tile. The name is the host without a leading "www." so the tile
    // text and search stay readable; the URL itself is kept verbatim.
    public static ICItem ImportUrl(string url)
    {
        if (!LegacyStore.IsUrl(url))
            throw new ArgumentException($"Not a URL: \"{url}\".", nameof(url));
        Uri.TryCreate(url, UriKind.Absolute, out var u);
        var host = u?.Host ?? "";
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host.Substring(4);
        return new ICItem
        {
            Name = string.IsNullOrEmpty(host) ? url : host,
            Application = url,
            GridIndex = -1,
        };
    }

    private static void MakeShortcut(string dest, string target, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(shellType);
        try
        {
            dynamic shortcut = shell.CreateShortcut(dest);
            try
            {
                shortcut.TargetPath = target;
                shortcut.WorkingDirectory = workingDirectory;
                shortcut.Save();
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); }
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
}
