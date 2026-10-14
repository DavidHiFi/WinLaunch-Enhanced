// Checks that WinLaunch data survives interrupted saves and damaged files.
// Usage: dotnet run -- <data folder to copy from>
// Works on throwaway copies only; the source folder is never written.
using System.Diagnostics;
using System.Xml.Serialization;
using WinLaunch.Legacy;

if (args.Length == 3 && args[0] is "writer" or "legacy-writer")
{
    // Child process: save the dataset in a tight loop until killed.
    var store = new LegacyStore(args[1]);
    var items = ReadRaw(args[2]); // a clean copy, never the file under test
    var ser = new XmlSerializer(typeof(List<ICItem>));
    while (true)
    {
        if (args[0] == "writer")
            store.SaveItems(items);
        else
        {
            // The pre-2.5.6 save: empties the file, then writes into it.
            using var fs = new FileStream(store.ItemsPath, FileMode.Create);
            ser.Serialize(fs, items);
        }
    }
}

var source = args[0];
int failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}");
    if (!ok) failures++;
}

string Fresh()
{
    var dir = Path.Combine(Environment.GetEnvironmentVariable("WL_TEST_DIR") ?? Path.GetTempPath(), "wl-safety-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(Path.Combine(dir, "ICBackup"));
    File.Copy(Path.Combine(source, "Items.xml"), Path.Combine(dir, "Items.xml"));
    File.Copy(Path.Combine(source, "Settings.xml"), Path.Combine(dir, "Settings.xml"));
    return dir;
}

var expected = ReadRaw(source).Count;
Console.WriteLine($"source items: {expected}");

// 1. Empty Items.xml with good backups: restored from the newest good one.
{
    var dir = Fresh();
    var s = new LegacyStore(dir);
    s.SaveItems(s.LoadItems());                       // creates one good backup
    File.WriteAllBytes(Path.Combine(dir, "ICBackup", "Items-2099-01-01-000000.xml"), Array.Empty<byte>()); // newest backup is empty too
    File.WriteAllBytes(s.ItemsPath, Array.Empty<byte>());
    var items = new LegacyStore(dir).LoadItems();
    var s2 = new LegacyStore(dir);
    s2.LoadItems();
    Check(items.Count == expected, $"empty Items.xml recovers {items.Count}/{expected} items, skipping an empty newer backup");
    Check(new FileInfo(s.ItemsPath).Length > 0 && s2.RecoveredItemsFrom == null, "Items.xml is rewritten so the next start loads normally");
    Check(Directory.GetFiles(Path.Combine(dir, "ICBackup"), "Unreadable-Items-*.xml").Length == 1, "the damaged file is kept aside");
}

// 2. Half-written Items.xml: recovered too.
{
    var dir = Fresh();
    var s = new LegacyStore(dir);
    s.SaveItems(s.LoadItems());
    var bytes = File.ReadAllBytes(s.ItemsPath);
    File.WriteAllBytes(s.ItemsPath, bytes[..(bytes.Length / 2)]);
    var t = new LegacyStore(dir);
    var items = t.LoadItems();
    Check(items.Count == expected && t.RecoveredItemsFrom != null, "truncated Items.xml recovers from backup");
}

// 2b. Newest readable backup is an empty board: the older one with apps wins.
{
    var dir = Fresh();
    var s = new LegacyStore(dir);
    var items = s.LoadItems();
    s.SaveItems(items);                               // backup with apps (older)
    Thread.Sleep(1100);
    s.SaveItems(new List<ICItem>());                  // backup with apps again
    Thread.Sleep(1100);
    s.SaveItems(items);                               // newest backup: the empty board
    File.WriteAllBytes(s.ItemsPath, Array.Empty<byte>());
    Check(new LegacyStore(dir).LoadItems().Count == expected, "an empty-board backup is skipped when an older one has apps");
}

// 3. No usable backup: the error still surfaces instead of an empty board.
{
    var dir = Fresh();
    File.WriteAllBytes(Path.Combine(dir, "Items.xml"), Array.Empty<byte>());
    bool threw = false;
    try { new LegacyStore(dir).LoadItems(); } catch { threw = true; }
    Check(threw, "with no good backup the load fails loudly (app shows the error, does not save)");
}

// 4. Saving over a damaged file never backs up the damage.
{
    var dir = Fresh();
    var s = new LegacyStore(dir);
    var items = s.LoadItems();
    File.WriteAllBytes(s.ItemsPath, Array.Empty<byte>());
    s.SaveItems(items);
    Check(Directory.GetFiles(Path.Combine(dir, "ICBackup"), "Items-*.xml").Length == 0, "an empty Items.xml is not copied into ICBackup");
}

// 5. Settings.xml damaged: last good copy is used.
{
    var dir = Fresh();
    var s = new LegacyStore(dir);
    var settings = s.LoadSettings();
    settings.Columns = 9;
    s.SaveSettings(settings);                         // writes Settings.xml.bak from the original
    s.SaveSettings(settings);                         // .bak now holds Columns = 9
    File.WriteAllBytes(s.SettingsPath, Array.Empty<byte>());
    Check(new LegacyStore(dir).LoadSettings().Columns == 9, "empty Settings.xml falls back to Settings.xml.bak");
}

// 6. Killed mid-save, many times: the file must always load.
int KillLoop(string mode, int rounds)
{
    var dir = Fresh();
    var exe = Environment.ProcessPath;
    var self = System.Reflection.Assembly.GetEntryAssembly().Location;
    var rng = new Random(42);
    int bad = 0;
    for (int i = 0; i < rounds; i++)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(exe) == "dotnet")
            psi.ArgumentList.Add(self);
        psi.ArgumentList.Add(mode);
        psi.ArgumentList.Add(dir);
        psi.ArgumentList.Add(source);
        using var p = Process.Start(psi);
        Thread.Sleep(rng.Next(150, 600));
        p.Kill();
        p.WaitForExit();
        try
        {
            var raw = new XmlSerializer(typeof(List<ICItem>));
            using var fs = File.OpenRead(Path.Combine(dir, "Items.xml"));
            if (((List<ICItem>)raw.Deserialize(fs)).Count != expected) bad++;
        }
        catch { bad++; }
    }
    return bad;
}

int legacyBad = KillLoop("legacy-writer", 40);
Console.WriteLine($"info  old in-place save: {legacyBad}/40 kills left Items.xml unreadable");
int newBad = KillLoop("writer", 100);
Check(newBad == 0, $"new save: {newBad}/100 kills left Items.xml unreadable");

Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

// Reads a dataset without LegacyStore, which would repair a damaged source.
static List<ICItem> ReadRaw(string dir)
{
    using var fs = File.OpenRead(Path.Combine(dir, "Items.xml"));
    return (List<ICItem>)new XmlSerializer(typeof(List<ICItem>)).Deserialize(fs);
}
