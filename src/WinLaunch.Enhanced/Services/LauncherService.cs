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
        if (LegacyStore.IsUrl(target))
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            return;
        }
        // Shell-owned shortcuts (.lnk in the LinkCache) are launched through
        // the .lnk itself: packaged apps (shell:AppsFolder, empty target) and
        // MSI "advertised" shortcuts (a %WINDIR%\Installer\{GUID}\ stub that
        // is not directly executable). Resolving and starting the stub fails
        // with ERROR_BAD_EXE_FORMAT, so never put it in ProcessStartInfo.
        if (store.IsShellLaunchLink(item.Application, target))
        {
            LaunchCachedLink(store, item, asAdmin);
            return;
        }
        if (target == null)
            throw new FileNotFoundException($"Target not found for \"{item.Name}\".", item.Application);
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
        try
        {
            Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception ex)
            when (store.IsCachedLink(item.Application) &&
                (ex.NativeErrorCode == 193 || ex.NativeErrorCode == 2 || ex.NativeErrorCode == 3))
        {
            // The resolved target looked runnable but the OS rejected it
            // (193 = bad EXE format, 2/3 = missing file): fall back to the
            // cached .lnk and let the shell resolve it, instead of showing a
            // cryptic "not a valid application" error.
            LaunchCachedLink(store, item, asAdmin);
        }
    }

    private static void LaunchCachedLink(LegacyStore store, ICItem item, bool asAdmin)
    {
        var lnkPsi = new ProcessStartInfo
        {
            FileName = store.CachedLinkPath(item.Application),
            Arguments = item.Arguments ?? "",
            UseShellExecute = true,
        };
        if (item.RunAsAdmin || asAdmin)
            lnkPsi.Verb = "runas";
        // No WorkingDirectory override: the shortcut carries its own.
        Process.Start(lnkPsi);
    }
}
