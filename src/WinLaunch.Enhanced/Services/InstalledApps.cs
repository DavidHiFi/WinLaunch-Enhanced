using System.IO;

namespace WinLaunch.Enhanced.Services;

// Ports the original app's "add default apps" pass: top-level Start Menu
// shortcuts become tiles, and sub-folders holding two or more shortcuts become
// folder tiles. Names already present (or found in an earlier root) are
// skipped; the caller imports whatever this returns.
public static class InstalledApps
{
    public static (List<string> Files, List<(string Dir, List<string> Files)> Folders) Scan(ISet<string> existingNames)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existingNames != null)
            foreach (var name in existingNames)
                if (!string.IsNullOrWhiteSpace(name))
                    known.Add(name.Trim());

        var files = new List<string>();
        var folders = new List<(string Dir, List<string> Files)>();

        foreach (var root in Roots())
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var file in SafeLinks(root))
                AddFile(file, known, files);

            foreach (var dir in SafeDirectories(root))
            {
                var links = new List<string>();
                foreach (var link in SafeLinks(dir))
                    if (!known.Contains(Path.GetFileNameWithoutExtension(link)))
                        links.Add(link);

                var folderName = new DirectoryInfo(dir).Name;
                if (links.Count >= 2 && !known.Contains(folderName))
                {
                    known.Add(folderName);
                    foreach (var link in links)
                        known.Add(Path.GetFileNameWithoutExtension(link));
                    folders.Add((dir, links));
                }
                else
                {
                    foreach (var link in links)
                        AddFile(link, known, files);
                }
            }
        }

        files.Sort((a, b) => string.Compare(
            Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
        folders.Sort((a, b) => string.Compare(
            new DirectoryInfo(a.Dir).Name, new DirectoryInfo(b.Dir).Name, StringComparison.OrdinalIgnoreCase));
        return (files, folders);
    }

    private static IEnumerable<string> Roots()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
    }

    private static void AddFile(string file, HashSet<string> known, List<string> files)
    {
        if (known.Add(Path.GetFileNameWithoutExtension(file)))
            files.Add(file);
    }

    private static IEnumerable<string> SafeLinks(string dir)
    {
        try { return Directory.GetFiles(dir).Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)).ToList(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        catch (IOException) { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeDirectories(string root)
    {
        try { return Directory.GetDirectories(root); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        catch (IOException) { return Array.Empty<string>(); }
    }
}
