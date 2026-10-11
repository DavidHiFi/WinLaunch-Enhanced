using System.Xml;
using System.Xml.Serialization;
using System.Runtime.Versioning;

namespace WinLaunch.Legacy;

// Same locations the original app uses, so both apps share one dataset:
//   %APPDATA%\WinLaunch\Items.xml, Settings.xml, CurrentTheme, LinkCache\, ICBackup\
// Portable mode (files beside the exe) is honored the same way.
public sealed class LegacyStore
{
    public string Directory { get; }
    public string ItemsPath => Path.Combine(Directory, "Items.xml");
    public string SettingsPath => Path.Combine(Directory, "Settings.xml");
    public string LinkCachePath => Path.Combine(Directory, "LinkCache");
    public string BackupDir => Path.Combine(Directory, "ICBackup");

    public bool IsPortable { get; }

    public LegacyStore(string overrideDir = null)
    {
        if (overrideDir != null)
        {
            Directory = overrideDir;
            IsPortable = true;
        }
        else if (File.Exists(Path.Combine(AppContext.BaseDirectory, "Items.xml")))
        {
            Directory = AppContext.BaseDirectory;
            IsPortable = true;
        }
        else
        {
            Directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinLaunch");
            IsPortable = false;
        }
        System.IO.Directory.CreateDirectory(Directory);
    }

    public List<ICItem> LoadItems()
    {
        if (!File.Exists(ItemsPath))
            return new List<ICItem>();
        using var fs = new FileStream(ItemsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = XmlReader.Create(fs);
        var ser = new XmlSerializer(typeof(List<ICItem>));
        return (List<ICItem>)ser.Deserialize(reader);
    }

    public LegacySettings LoadSettings()
    {
        if (!File.Exists(SettingsPath))
            return new LegacySettings();
        try
        {
            using var fs = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = XmlReader.Create(fs);
            var ser = new XmlSerializer(typeof(LegacySettings));
            return (LegacySettings)ser.Deserialize(reader);
        }
        catch
        {
            return new LegacySettings();
        }
    }

    // Saves with a pre-write backup of the previous file, pruned to the 10 newest.
    public void SaveItems(List<ICItem> items)
    {
        System.IO.Directory.CreateDirectory(BackupDir);
        if (File.Exists(ItemsPath))
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            File.Copy(ItemsPath, Path.Combine(BackupDir, $"Items-{stamp}.xml"), overwrite: true);
            foreach (var old in new DirectoryInfo(BackupDir).GetFiles("Items-*.xml")
                         .OrderByDescending(f => f.Name).Skip(10))
            {
                try { old.Delete(); } catch { }
            }
        }
        using var fs = new FileStream(ItemsPath, FileMode.Create);
        using var writer = XmlWriter.Create(fs, new XmlWriterSettings { Indent = false });
        new XmlSerializer(typeof(List<ICItem>)).Serialize(writer, items);
    }

    public void SaveSettings(LegacySettings settings)
    {
        using var fs = new FileStream(SettingsPath, FileMode.Create);
        using var writer = XmlWriter.Create(fs, new XmlWriterSettings { Indent = false });
        new XmlSerializer(typeof(LegacySettings)).Serialize(writer, settings);
    }

    // Resolves a LinkCache entry (a GUID-named .lnk) to its real target.
    // Returns null when the entry is missing or unresolvable; callers treat
    // the tile as broken and offer removal, never a silent dead control.
    [SupportedOSPlatform("windows")]
    public string ResolveLinkTarget(string application)
    {
        if (string.IsNullOrEmpty(application))
            return null;
        if (Path.IsPathRooted(application) && (File.Exists(application) || System.IO.Directory.Exists(application)))
            return application;
        var lnk = Path.Combine(LinkCachePath, application);
        if (!lnk.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            lnk += ".lnk";
        if (!File.Exists(lnk))
            return null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            try
            {
                dynamic shortcut = shell.CreateShortcut(lnk);
                try { return (string)shortcut.TargetPath; }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); }
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }
        catch
        {
            return null;
        }
    }
}
