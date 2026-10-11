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
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(path, dest, overwrite: true);
        }
        else
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            try
            {
                dynamic shortcut = shell.CreateShortcut(dest);
                try
                {
                    shortcut.TargetPath = path;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(path) ?? "";
                    shortcut.Save();
                }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); }
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }
        return new ICItem
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Application = id,
            GridIndex = -1,
        };
    }
}
