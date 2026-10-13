using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace WinLaunch.Enhanced.Services;

public enum UpdateStatus { Idle, Checking, UpToDate, Available, Downloading, Failed }

// Asks GitHub for the newest release. An installed copy can also download the
// release's setup file, which runs only after its SHA-256 matches GitHub's
// digest. WinLaunch never installs an update without the user's click.
public sealed class UpdateService : IDisposable
{
    public const string ReleasesApi = "https://api.github.com/repos/DavidHiFi/WinLaunch-Enhanced/releases?per_page=20";

    private readonly EnhancedPrefs _prefs;
    private readonly HttpClient _http;
    private CancellationTokenSource _download;

    public UpdateService(EnhancedPrefs prefs)
    {
        _prefs = prefs;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"WinLaunchEnhanced/{Current}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static AppVersion Current { get; } = ReadCurrent();

    // Inno Setup leaves its uninstaller beside the app, so this tells an installed copy from a portable one.
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public UpdateStatus Status { get; private set; } = UpdateStatus.Idle;
    public ReleaseInfo Available { get; private set; }
    public string Message { get; private set; }
    public double Progress { get; private set; }
    public DateTime? LastChecked => _prefs.LastUpdateCheck;
    public bool Busy => Status is UpdateStatus.Checking or UpdateStatus.Downloading;

    public event Action Changed;

    public async Task CheckAsync()
    {
        if (Busy)
            return;
        Set(UpdateStatus.Checking);
        try
        {
            using var response = await _http.GetAsync(ReleasesApi);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var newest = UpdateRules.Newest(json.RootElement, _prefs.IncludePreReleases);
            _prefs.LastUpdateCheck = DateTime.UtcNow;
            _prefs.Save();
            Available = newest != null && UpdateRules.IsNewer(newest.Version, Current) ? newest : null;
            Message = null;
            Set(Available != null ? UpdateStatus.Available : UpdateStatus.UpToDate);
        }
        catch (Exception ex)
        {
            Message = Describe(ex);
            Set(UpdateStatus.Failed);
        }
    }

    // Downloads the setup file for the available release and starts it. WinLaunch
    // then closes so the installer can replace its files, and the installer starts it again.
    public async Task InstallAsync()
    {
        var release = Available;
        if (Busy || release?.Installer is not { } asset || !IsInstalled)
            return;
        if (asset.Sha256 == null)
        {
            Message = "This release has no checksum, so the download cannot be verified. Use the release page instead.";
            Set(UpdateStatus.Available);
            return;
        }
        _download = new CancellationTokenSource();
        Progress = 0;
        Message = null;
        Set(UpdateStatus.Downloading);
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "WinLaunchEnhanced-Update");
            var setup = await UpdateDownloader.FetchAsync(_http, asset, folder, fraction =>
            {
                Progress = fraction;
                Changed?.Invoke();
            }, _download.Token);
            Process.Start(new ProcessStartInfo(setup, "/SILENT /NORESTART") { UseShellExecute = true });
            // The installer replaces our files, so exit; it starts the app again itself.
            System.Windows.Application.Current?.Dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Normal);
        }
        catch (OperationCanceledException)
        {
            Message = "Download cancelled.";
        }
        catch (Exception ex)
        {
            Message = Describe(ex);
        }
        finally
        {
            _download.Dispose();
            _download = null;
            if (Status == UpdateStatus.Downloading)
                Set(UpdateStatus.Available);
        }
    }

    public void CancelDownload() => _download?.Cancel();

    public void Dispose()
    {
        _download?.Cancel();
        _http.Dispose();
    }

    private void Set(UpdateStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }

    private static AppVersion ReadCurrent()
    {
        var assembly = typeof(UpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational != null && UpdateRules.TryParse(informational, out var version))
            return version;
        var number = assembly.GetName().Version ?? new Version(0, 0, 0);
        return new AppVersion(new Version(number.Major, number.Minor, number.Build), null);
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden or (HttpStatusCode)429 } =>
            "GitHub is limiting checks for now. Try again later.",
        HttpRequestException => "Could not reach GitHub. Check your internet connection.",
        TaskCanceledException => "GitHub did not answer in time.",
        JsonException => "GitHub sent a release list that WinLaunch could not read.",
        _ => ex.Message,
    };
}
