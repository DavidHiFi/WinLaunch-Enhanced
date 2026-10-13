using System.IO;
using System.IO.Compression;

namespace WinLaunch.Enhanced.Services;

// A .WLbackup file is a zip of the top-level files in the data folder: Items.xml,
// Settings.xml, enhanced.json, backdrop.mode, theme.mode. Icon and link caches are
// rebuilt on demand and stay out of the file.
public static class BackupService
{
    public const string Extension = ".WLbackup";

    public static void Export(string dataDir, string zipPath)
    {
        var tmp = zipPath + ".tmp";
        if (File.Exists(tmp))
            File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(dataDir))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    continue;
                zip.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
            }
        }
        File.Move(tmp, zipPath, overwrite: true);
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
            entry.ExtractToFile(Path.Combine(dataDir, entry.Name), overwrite: true);
        }
    }
}
