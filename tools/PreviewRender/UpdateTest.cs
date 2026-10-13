using System.Text.Json;
using WinLaunch.Enhanced.Services;

// Rule checks for the updater: version tags, newest-release picking, digest
// parsing and download trust. Canned release lists only, no network.
// Usage: PreviewRender --update-test
internal static class UpdateTest
{
    private static int Fails;

    public static int Run()
    {
        ParseCases();
        NewestCases();
        DigestCases();
        UrlCases();
        Console.WriteLine(Fails == 0 ? "UPDATE ALL PASS" : $"UPDATE FAILURES: {Fails}");
        return Fails == 0 ? 0 : 1;
    }

    private static void Check(string label, bool ok, string detail)
    {
        if (!ok) Fails++;
        Console.WriteLine($"UPDATE {(ok ? "PASS" : "FAIL")} {label}: {detail}");
    }

    private static void ParseCases()
    {
        Check("final tag", UpdateRules.TryParse("v2.4.1", out var v) && v.ToString() == "2.4.1" && !v.IsPreview, v?.ToString() ?? "null");
        Check("preview tag", UpdateRules.TryParse("2.5.0-beta.1", out var p) && p.IsPreview && p.ToString() == "2.5.0-beta.1", p?.ToString() ?? "null");
        Check("build metadata ignored", UpdateRules.TryParse("v2.5.0+abc123", out var m) && m.ToString() == "2.5.0", m?.ToString() ?? "null");
        Check("junk rejected", !UpdateRules.TryParse("nope", out _), "nope");
        Check("empty label rejected", !UpdateRules.TryParse("v2.5.0-", out _), "v2.5.0-");
        UpdateRules.TryParse("v2.4.1", out var newer);
        UpdateRules.TryParse("v2.4.0", out var older);
        UpdateRules.TryParse("2.5.0-beta.1", out var preview);
        UpdateRules.TryParse("v2.5.0", out var final);
        Check("newer beats older", UpdateRules.IsNewer(newer, older) && !UpdateRules.IsNewer(older, newer), "2.4.1 vs 2.4.0");
        Check("preview beats older final", UpdateRules.IsNewer(preview, newer), "2.5.0-beta.1 vs 2.4.1");
        Check("final beats its preview", UpdateRules.IsNewer(final, preview), "2.5.0 vs 2.5.0-beta.1");
    }

    private static void NewestCases()
    {
        var json = """
            [
              {"tag_name":"v2.5.0","draft":true,"prerelease":false,"published_at":"2026-10-14T00:00:00Z","body":"d","html_url":"https://github.com/DavidHiFi/WinLaunch-Enhanced/releases/tag/v2.5.0","assets":[]},
              {"tag_name":"v2.4.2-beta.1","draft":false,"prerelease":true,"published_at":"2026-10-13T00:00:00Z","body":"b","html_url":"https://github.com/DavidHiFi/WinLaunch-Enhanced/releases/tag/v2.4.2-beta.1","assets":[
                {"name":"WinLaunchEnhanced-2.4.2-beta.1-Setup.exe","browser_download_url":"https://github.com/DavidHiFi/WinLaunch-Enhanced/releases/download/v2.4.2-beta.1/WinLaunchEnhanced-2.4.2-beta.1-Setup.exe","size":100,"digest":"sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}]},
              {"tag_name":"v2.4.0","draft":false,"prerelease":false,"published_at":"2026-10-12T00:00:00Z","body":"o","html_url":"https://github.com/DavidHiFi/WinLaunch-Enhanced/releases/tag/v2.4.0","assets":[]},
              {"tag_name":"v2.4.1","draft":false,"prerelease":false,"published_at":"2026-10-13T00:00:00Z","body":"n","html_url":"https://github.com/DavidHiFi/WinLaunch-Enhanced/releases/tag/v2.4.1","assets":[
                {"name":"WinLaunchEnhanced-2.4.1-Setup.exe","browser_download_url":"https://github.com/DavidHiFi/WinLaunch-Enhanced/releases/download/v2.4.1/WinLaunchEnhanced-2.4.1-Setup.exe","size":2814003,"digest":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}
            ]
            """;
        using var doc = JsonDocument.Parse(json);
        var stable = UpdateRules.Newest(doc.RootElement, includePreviews: false);
        Check("draft skipped, stable newest picked",
            stable != null && stable.Version.ToString() == "2.4.1" && stable.Installer != null &&
            stable.Installer.Name == "WinLaunchEnhanced-2.4.1-Setup.exe" && stable.Installer.Size == 2814003 &&
            stable.Installer.Sha256 == new string('b', 64),
            stable?.Version.ToString() ?? "null");
        var withPreviews = UpdateRules.Newest(doc.RootElement, includePreviews: true);
        Check("preview picked when asked for",
            withPreviews != null && withPreviews.Version.ToString() == "2.4.2-beta.1" && withPreviews.Installer?.Sha256 == new string('a', 64),
            withPreviews?.Version.ToString() ?? "null");
        using var drafts = JsonDocument.Parse("""[{"tag_name":"v9.9.9","draft":true,"prerelease":false,"assets":[]}]""");
        Check("draft-only list offers nothing", UpdateRules.Newest(drafts.RootElement, false) == null, "draft v9.9.9");
        using var untrusted = JsonDocument.Parse("""
            [{"tag_name":"v2.4.1","draft":false,"prerelease":false,"published_at":"2026-10-13T00:00:00Z","body":"","html_url":"https://github.com/DavidHiFi/WinLaunch-Enhanced/releases/tag/v2.4.1","assets":[
              {"name":"WinLaunchEnhanced-2.4.1-Setup.exe","browser_download_url":"http://evil.example/setup.exe","size":1,"digest":"sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"}]}]
            """);
        var bad = UpdateRules.Newest(untrusted.RootElement, false);
        Check("untrusted download url rejected", bad != null && bad.Installer == null, bad?.Installer?.Url ?? "no installer");
    }

    private static void DigestCases()
    {
        Check("valid digest", UpdateRules.ParseDigest("sha256:" + new string('d', 64)) == new string('d', 64), "64 hex");
        Check("uppercase normalized", UpdateRules.ParseDigest("SHA256:" + new string('E', 64)) == new string('e', 64), "upper hex");
        Check("wrong scheme rejected", UpdateRules.ParseDigest("md5:" + new string('f', 32)) == null, "md5");
        Check("short digest rejected", UpdateRules.ParseDigest("sha256:abc") == null, "short");
        Check("missing digest rejected", UpdateRules.ParseDigest(null) == null, "null");
    }

    private static void UrlCases()
    {
        Check("github release url trusted",
            UpdateRules.IsTrustedUrl("https://github.com/DavidHiFi/WinLaunch-Enhanced/releases/download/v2.4.1/WinLaunchEnhanced-2.4.1-Setup.exe"), "github.com");
        Check("github objects url trusted",
            UpdateRules.IsTrustedUrl("https://objects.githubusercontent.com/abc/setup.exe"), "objects host");
        Check("plain http rejected", !UpdateRules.IsTrustedUrl("http://github.com/x/setup.exe"), "http");
        Check("stranger rejected", !UpdateRules.IsTrustedUrl("https://evil.example/setup.exe"), "evil.example");
    }
}
