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
    public string IconCachePath => Path.Combine(Directory, "IconCache");
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

    // Last good Settings.xml, kept beside it on every save.
    public string SettingsBackupPath => SettingsPath + ".bak";

    // Set when LoadItems found Items.xml unreadable and restored a backup:
    // the backup file it used. Null after a normal load.
    public string RecoveredItemsFrom { get; private set; }

    public List<ICItem> LoadItems()
    {
        RecoveredItemsFrom = null;
        if (!File.Exists(ItemsPath))
            return new List<ICItem>();
        try
        {
            return ReadXml<List<ICItem>>(ItemsPath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or XmlException)
        {
            // Empty or damaged file. Restore the newest backup that reads
            // cleanly and has apps in it (an empty board only if no backup
            // has any); keep the damaged file aside instead of deleting it.
            FileInfo chosen = null;
            List<ICItem> items = null;
            foreach (var backup in ItemBackupsNewestFirst())
            {
                List<ICItem> read;
                try { read = ReadXml<List<ICItem>>(backup.FullName); }
                catch { continue; }
                if (chosen == null || (items.Count == 0 && read.Count > 0))
                {
                    chosen = backup;
                    items = read;
                }
                if (items.Count > 0)
                    break;
            }
            if (chosen == null)
                throw;
            var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            try { File.Copy(ItemsPath, Path.Combine(BackupDir, $"Unreadable-Items-{stamp}.xml"), overwrite: true); }
            catch { }
            // If the repaired file cannot be written now (read-only, held open),
            // the apps still load; the next save writes it.
            try { SafeFile.Write(ItemsPath, s => { using var src = File.OpenRead(chosen.FullName); src.CopyTo(s); }); }
            catch { }
            RecoveredItemsFrom = chosen.FullName;
            return items;
        }
    }

    public LegacySettings LoadSettings()
    {
        foreach (var path in new[] { SettingsPath, SettingsBackupPath })
        {
            if (!File.Exists(path))
                continue;
            try { return ReadXml<LegacySettings>(path); }
            catch { }
        }
        return new LegacySettings();
    }

    // Saves with a pre-write backup of the previous file, pruned to the 10
    // newest. Only a readable file is backed up, so a damaged one can never
    // push the good copies out.
    public void SaveItems(List<ICItem> items)
    {
        // Under the write lock, so a background save and a direct save cannot
        // collide on the same per-second backup name.
        lock (SafeFile.Sync)
        {
            System.IO.Directory.CreateDirectory(BackupDir);
            if (IsReadable<List<ICItem>>(ItemsPath))
            {
                var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
                try
                {
                    File.Copy(ItemsPath, Path.Combine(BackupDir, $"Items-{stamp}.xml"), overwrite: true);
                    foreach (var old in ItemBackupsNewestFirst().Skip(10))
                    {
                        try { old.Delete(); } catch { }
                    }
                }
                catch { } // a missed backup must not cost the save itself
            }
            SafeFile.Write(ItemsPath, s => WriteXml(s, items));
        }
    }

    public void SaveSettings(LegacySettings settings)
    {
        lock (SafeFile.Sync)
        {
            if (IsReadable<LegacySettings>(SettingsPath))
            {
                try { File.Copy(SettingsPath, SettingsBackupPath, overwrite: true); }
                catch { }
            }
            SafeFile.Write(SettingsPath, s => WriteXml(s, settings));
        }
    }

    // Newest first by when the copied file was last written (File.Copy keeps
    // that time), so the order survives clock changes such as the
    // daylight-saving hour; the name breaks ties.
    private IEnumerable<FileInfo> ItemBackupsNewestFirst() =>
        System.IO.Directory.Exists(BackupDir)
            ? new DirectoryInfo(BackupDir).GetFiles("Items-*.xml")
                .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name)
            : Enumerable.Empty<FileInfo>();

    private static T ReadXml<T>(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = XmlReader.Create(fs);
        return (T)new XmlSerializer(typeof(T)).Deserialize(reader);
    }

    private static bool IsReadable<T>(string path)
    {
        if (!File.Exists(path))
            return false;
        try { ReadXml<T>(path); return true; }
        catch { return false; }
    }

    private static void WriteXml<T>(Stream s, T value)
    {
        using var writer = XmlWriter.Create(s, new XmlWriterSettings { Indent = false });
        new XmlSerializer(typeof(T)).Serialize(writer, value);
    }

    // An absolute URI with a real scheme (https, mailto, steam, ...). File and
    // bare drive paths are excluded so they keep going down the .lnk path.
    public static bool IsUrl(string s)
    {
        if (string.IsNullOrEmpty(s))
            return false;
        return Uri.TryCreate(s, UriKind.Absolute, out var u) && !u.IsFile && u.Scheme.Length > 1;
    }

    // The cached copy of a LinkCache entry: LinkCache\<name>.lnk, with ".lnk"
    // appended when the stored name has no extension.
    public string CachedLinkPath(string application)
    {
        if (string.IsNullOrEmpty(application))
            return null;
        var lnk = Path.Combine(LinkCachePath, application);
        if (!lnk.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            lnk += ".lnk";
        return lnk;
    }

    // True when the entry has a cached .lnk on disk. Packaged-app shortcuts
    // (shell:AppsFolder) resolve to an empty target, so this is what tells a
    // healthy app tile from a missing one.
    public bool IsCachedLink(string application)
    {
        var lnk = CachedLinkPath(application);
        return lnk != null && File.Exists(lnk);
    }

    // True when a resolved target lives inside the Windows Installer cache
    // (%WINDIR%\Installer\{ProductCode}\...). Those paths come from MSI
    // "advertised" shortcuts: the file there is a stub the Installer service
    // resolves at launch, so starting it directly fails with
    // ERROR_BAD_EXE_FORMAT ("not a valid application for this OS platform").
    // The cached .lnk itself must be launched and let the shell resolve it.
    public static bool IsAdvertisedTarget(string target)
    {
        if (string.IsNullOrEmpty(target))
            return false;
        var windir = Environment.GetEnvironmentVariable("WINDIR");
        if (string.IsNullOrEmpty(windir))
            windir = Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "Windows");
        var cache = Path.Combine(windir, "Installer");
        if (target.StartsWith(cache + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith(cache + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;
        // Fallback for unusual layouts: an \Installer\{GUID}\ path is the
        // advertised-shortcut signature.
        return target.IndexOf(@"\Installer\{", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // True when the entry must be launched via its cached .lnk: packaged apps
    // (shell:AppsFolder, empty target) and MSI advertised shortcuts
    // (Installer cache target). Both are shell-owned; the resolved path is
    // not directly runnable.
    public bool IsShellLaunchLink(string application, string resolvedTarget)
    {
        return IsCachedLink(application) &&
            (string.IsNullOrEmpty(resolvedTarget) || IsAdvertisedTarget(resolvedTarget));
    }

    // Resolves a LinkCache entry (a GUID-named .lnk) to its real target.
    // Returns null when the entry is missing or unresolvable; callers treat
    // the tile as broken and offer removal, never a silent dead control.
    // Empty (not null) means a shortcut the shell owns, such as a packaged
    // app pinned out of shell:AppsFolder: TargetPath is empty there, and the
    // .lnk itself is what gets launched and drawn.
    [SupportedOSPlatform("windows")]
    public string ResolveLinkTarget(string application)
    {
        if (string.IsNullOrEmpty(application))
            return null;
        if (IsUrl(application))
            return application;
        if (Path.IsPathRooted(application) && (File.Exists(application) || System.IO.Directory.Exists(application)))
            return application;
        var lnk = CachedLinkPath(application);
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
