using System.Globalization;
using System.Text.Json;

namespace WinLaunch.Enhanced.Services;

// A release version: its numbers, and the label of a preview such as "beta.1".
public sealed record AppVersion(Version Number, string Label) : IComparable<AppVersion>
{
    public bool IsPreview => Label != null;

    public int CompareTo(AppVersion other)
    {
        int byNumber = Number.CompareTo(other.Number);
        if (byNumber != 0)
            return byNumber;
        // At the same numbers, the final release outranks its previews.
        return (other.IsPreview ? 1 : 0).CompareTo(IsPreview ? 1 : 0);
    }

    public override string ToString() =>
        IsPreview ? $"{Number.Major}.{Number.Minor}.{Number.Build}-{Label}" : $"{Number.Major}.{Number.Minor}.{Number.Build}";
}

public sealed record ReleaseAsset(string Name, string Url, long Size, string Sha256);

public sealed record ReleaseInfo(AppVersion Version, string Tag, DateTime PublishedUtc, string Notes, string PageUrl,
    ReleaseAsset Installer);

// Rules for reading GitHub release lists. No I/O, so the update test can
// cover each case with canned responses.
public static class UpdateRules
{
    public const string ReleasesPage = "https://github.com/DavidHiFi/WinLaunch-Enhanced/releases";

    // "v2.4.1" is 2.4.1; "v2.5.0-beta.1" is 2.5.0 with the preview label "beta.1".
    public static bool TryParse(string text, out AppVersion version)
    {
        version = null;
        var s = (text ?? "").Trim().TrimStart('v', 'V');
        int plus = s.IndexOf('+');
        if (plus >= 0)
            s = s[..plus];
        string label = null;
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            label = s[(dash + 1)..];
            s = s[..dash];
            if (label.Length == 0)
                return false;
        }
        if (!Version.TryParse(s, out var number))
            return false;
        version = new AppVersion(new Version(number.Major, number.Minor, Math.Max(0, number.Build)), label);
        return true;
    }

    public static bool IsNewer(AppVersion candidate, AppVersion current) => candidate.CompareTo(current) > 0;

    // The highest release on the list. Drafts are never offered, and previews only when asked for.
    public static ReleaseInfo Newest(JsonElement releases, bool includePreviews)
    {
        ReleaseInfo best = null;
        if (releases.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var release in releases.EnumerateArray())
        {
            if (Flag(release, "draft"))
                continue;
            var tag = Text(release, "tag_name");
            if (!TryParse(tag, out var version))
                continue;
            if (!includePreviews && (version.IsPreview || Flag(release, "prerelease")))
                continue;
            if (best == null || version.CompareTo(best.Version) > 0)
                best = Read(release, tag, version);
        }
        return best;
    }

    // The digest GitHub lists is "sha256:" and 64 hex digits. Anything else is not usable.
    public static string ParseDigest(string digest)
    {
        const string prefix = "sha256:";
        if (digest == null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var hex = digest[prefix.Length..].Trim().ToLowerInvariant();
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex : null;
    }

    // Downloads come only from GitHub, over HTTPS.
    public static bool IsTrustedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;
        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static ReleaseInfo Read(JsonElement release, string tag, AppVersion version)
    {
        var published = DateTimeOffset.TryParse(Text(release, "published_at"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var when) ? when.UtcDateTime : DateTime.MinValue;
        return new ReleaseInfo(version, tag, published, Text(release, "body") ?? "",
            Text(release, "html_url") ?? ReleasesPage, FindInstaller(release, tag));
    }

    // The setup file is named after its version, the same way build.ps1 names it.
    private static ReleaseAsset FindInstaller(JsonElement release, string tag)
    {
        var name = $"WinLaunchEnhanced-{tag.TrimStart('v', 'V')}-Setup.exe";
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (Text(asset, "name") != name)
                continue;
            var url = Text(asset, "browser_download_url");
            if (url == null || !IsTrustedUrl(url))
                return null;
            long size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
            return new ReleaseAsset(name, url, size, ParseDigest(Text(asset, "digest")));
        }
        return null;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
