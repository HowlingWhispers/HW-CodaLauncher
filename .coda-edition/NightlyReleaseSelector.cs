using System.IO;
using System.Text.Json;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Selects the most recently PUBLISHED BuildCraft Nightly. GitHub's release
/// API lists prereleases by creation/tag ordering, not necessarily publish time.
/// Previous launchers used the first entry and repeatedly installed stale ZIPs.
/// Kept independent of IO so fixtures can cover actual historical API ordering.
/// </summary>
internal static class NightlyReleaseSelector
{
    internal sealed record Release(string Tag, Uri PackageUrl, Uri ChecksumUrl, DateTimeOffset Published);

    internal static Release? SelectNewest(JsonElement releases, string tagPrefix,
        string packageName, string checksumName)
    {
        if (releases.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Expected an array of GitHub releases.");

        Release? newest = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (!release.TryGetProperty("prerelease", out var prerelease)
                || prerelease.ValueKind != JsonValueKind.True
                || (release.TryGetProperty("draft", out var draft)
                    && draft.ValueKind == JsonValueKind.True))
                continue;

            string tag = release.TryGetProperty("tag_name", out var t)
                && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            if (!tag.StartsWith(tagPrefix, StringComparison.Ordinal)) continue;

            string published = release.TryGetProperty("published_at", out var p)
                && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
            if (!DateTimeOffset.TryParse(published, out var publishedAt)) continue;

            if (!release.TryGetProperty("assets", out var assets)
                || assets.ValueKind != JsonValueKind.Array) continue;
            Uri? packageUrl = null;
            Uri? checksumUrl = null;

            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var n)
                    && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                string? raw = asset.TryGetProperty("browser_download_url", out var u)
                    && u.ValueKind == JsonValueKind.String ? u.GetString() : null;

                if (!Uri.TryCreate(raw, UriKind.Absolute, out var url)
                    || url.Scheme != Uri.UriSchemeHttps
                    || !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                    || !url.AbsolutePath.StartsWith(
                        "/HowlingWhispers/HW-Mods/releases/download/",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                if (name == packageName) packageUrl = url;
                if (name == checksumName) checksumUrl = url;
            }

            if (packageUrl is null || checksumUrl is null) continue;
            // Always use published_at, never GitHub's list position or the
            // date prefix of a tag. Deterministic tie-break if needed.
            if (newest is null || publishedAt > newest.Published
                || (publishedAt == newest.Published
                    && string.CompareOrdinal(tag, newest.Tag) > 0))
                newest = new Release(tag, packageUrl, checksumUrl, publishedAt);
        }
        return newest;
    }
}
