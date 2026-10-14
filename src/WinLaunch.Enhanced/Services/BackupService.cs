using System.IO;
using System.IO.Compression;
using WinLaunch.Legacy;

namespace WinLaunch.Enhanced.Services;

// A .WLbackup file is a zip of the top-level files in the data folder: Items.xml,
// Settings.xml, enhanced.json, backdrop.mode, theme.mode. Icon and link caches are
// rebuilt on demand and stay out of the file.
public static class BackupService
{
    public const string Extension = ".WLbackup";

    public static void Export(string dataDir, string zipPath)
    {
        SafeFile.Write(zipPath, s =>
        {
            using var zip = new ZipArchive(s, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var file in Directory.EnumerateFiles(dataDir))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    continue;
                zip.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
            }
        });
    }

    // Checks the file first, saves the current setup to safetyDir, then unpacks over the data folder.
    public static void Import(string zipPath, string dataDir, string safetyDir)
    {
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            if (zip.GetEntry("Items.xml") == null)
                throw new InvalidDataException("This file has no Items.xml, so it is not a WinLaunch backup.");
            foreach (var entry in zip.Entries)
                if (entry.FullName.Contains('/') || entry.FullName.Contains('\\') || entry.FullName.Contains(".."))
                    throw new InvalidDataException("This backup has a file outside its own folder, so it was not imported.");
        }
        Directory.CreateDirectory(safetyDir);
        Export(dataDir, Path.Combine(safetyDir, $"before-import-{DateTime.Now:yyyyMMdd-HHmmss}{Extension}"));
        using var source = ZipFile.OpenRead(zipPath);
        foreach (var entry in source.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            SafeFile.Write(Path.Combine(dataDir, entry.Name), s => { using var e = entry.Open(); e.CopyTo(s); });
        }
    }
}
