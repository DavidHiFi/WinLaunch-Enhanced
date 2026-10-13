using System.Diagnostics;
using System.IO;
using WinLaunch.Legacy;

namespace WinLaunch.Enhanced.Services;

// Launches tiles. Errors name the file involved and the reason; callers show them
// in the error state, never as a silent no-op.
public static class LauncherService
{
    public static void Launch(ICItem item, LegacyStore store, bool asAdmin = false)
    {
        var target = store.ResolveLinkTarget(item.Application);
        // Packaged-app shortcuts (shell:AppsFolder) resolve to an empty target;
        // like the original, start the cached .lnk itself and let the shell
        // resolve the app.
        if (string.IsNullOrEmpty(target) && store.IsCachedLink(item.Application))
        {
            var lnkPsi = new ProcessStartInfo
            {
                FileName = store.CachedLinkPath(item.Application),
                Arguments = item.Arguments ?? "",
                UseShellExecute = true,
            };
            if (item.RunAsAdmin || asAdmin)
                lnkPsi.Verb = "runas";
            Process.Start(lnkPsi);
            return;
        }
        if (target == null)
            throw new FileNotFoundException($"Target not found for \"{item.Name}\".", item.Application);
        if (LegacyStore.IsUrl(target))
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            return;
        }
        var psi = new ProcessStartInfo
        {
            FileName = target,
            Arguments = item.Arguments ?? "",
            UseShellExecute = true,
        };
        try
        {
            if (item.RunAsAdmin || asAdmin)
                psi.Verb = "runas";
            if (File.Exists(target))
                psi.WorkingDirectory = Path.GetDirectoryName(target);
        }
        catch { }
        Process.Start(psi);
    }
}
