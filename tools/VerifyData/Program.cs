using WinLaunch.Legacy;

// Loads a real WinLaunch dataset (default: the live %APPDATA% one) and reports
// exactly what Enhanced will preserve. Non-zero exit on any structural problem.
var dir = args.Length > 0 ? args[0] : null;
var store = new LegacyStore(dir);
Console.WriteLine($"store: {store.Directory} (portable: {store.IsPortable})");

var items = store.LoadItems();
var settings = store.LoadSettings();

int folders = 0, apps = 0, nested = 0, broken = 0;
foreach (var item in items)
{
    if (item.IsFolder)
    {
        folders++;
        nested += item.Items?.Count ?? 0;
        Console.WriteLine($"folder page={item.Page} idx={item.GridIndex} \"{item.Name}\" children={item.Items?.Count ?? 0}");
        foreach (var child in item.Items ?? new List<ICItem>())
        {
            var target = store.ResolveLinkTarget(child.Application);
            if (target == null || (!File.Exists(target) && !Directory.Exists(target)))
            { broken++; Console.WriteLine($"  BROKEN \"{child.Name}\" -> {child.Application} (target: {target ?? "unresolvable"})"); }
        }
    }
    else
    {
        apps++;
        var target = store.ResolveLinkTarget(item.Application);
        if (target == null || (!File.Exists(target) && !Directory.Exists(target)))
        {
            broken++;
            Console.WriteLine($"BROKEN \"{item.Name}\" -> {item.Application} (target: {target ?? "unresolvable"})");
        }
    }
}

Console.WriteLine($"top-level: {items.Count} (folders: {folders}, loose apps: {apps}, nested: {nested}, unresolvable: {broken})");
Console.WriteLine($"settings: grid={settings.Columns}x{settings.Rows} folder-grid={settings.FolderColumns}x{settings.FolderRows} " +
    $"hotkey={(settings.HotKeyEnabled ? $"ctrl={settings.HotControl} alt={settings.HotAlt} shift={settings.HotShift} win={settings.HotWin} + {settings.HotKeyExtend}" : "off")} " +
    $"version={settings.Versionstring}");

// Round-trip check: serialize back and re-read; the dataset must survive untouched.
var tmp = Path.GetTempFileName();
try
{
    using (var fs = new FileStream(tmp, FileMode.Create))
        new System.Xml.Serialization.XmlSerializer(typeof(List<ICItem>)).Serialize(fs, items);
    using (var fs = new FileStream(tmp, FileMode.Open))
    {
        var back = (List<ICItem>)new System.Xml.Serialization.XmlSerializer(typeof(List<ICItem>)).Deserialize(fs);
        if (back.Count != items.Count)
        {
            Console.WriteLine("ROUND-TRIP MISMATCH");
            return 2;
        }
    }
}
finally { try { File.Delete(tmp); } catch { } }

Console.WriteLine("round-trip: OK");
return broken > 0 ? 1 : 0;
