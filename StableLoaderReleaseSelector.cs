using System.IO;
using System.Text.Json;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// H.O.W.L. Stable accepts only public non-prerelease GitHub releases.
/// Nightly has its own independent selector. No automatic experimental upgrade.
/// Sort by semantic version rather than GitHub API list order.
*/
internal static class StableLoaderReleaseSelector
{
    internal sealed record Release(string Version, string BundleUrl, string BundleName);

    internal static Release? SelectNewest(JsonElement releases)
    {
        if (releases.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Expected H.O.W.L. releases array.");
        Release? newest = null;
        Version? best = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                continue;
            if (!release.TryGetProperty("prerelease", out var pre)
                    || pre.ValueKind != JsonValueKind.False)
                continue;
            string tag = release.TryGetProperty("tag_name", out var t)
                    && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            if (!tag.StartsWith("v", StringComparison.Ordinal)
                    || !Version.TryParse(tag[1..], out var parsed)
                    || parsed.Build < 0 || parsed.Revision >= 0
                    || tag[1..] != parsed.ToString(3))
                continue;
            string expected = "CodaLoader-" + tag + "-win64.zip";
            if (!release.TryGetProperty("assets", out var assets)
                    || assets.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var asset in assets.EnumerateArray())
            {
                string filename = asset.TryGetProperty("name", out var n)
                    && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                if (!filename.Equals(expected, StringComparison.Ordinal)) continue;
                string? raw = asset.TryGetProperty("browser_download_url", out var urlProp)
                    && urlProp.ValueKind == JsonValueKind.String ? urlProp.GetString() : null;
                if (!Uri.TryCreate(raw, UriKind.Absolute, out var url)
                        || url.Scheme != Uri.UriSchemeHttps
                        || !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                        || !url.AbsolutePath.Equals(
                           "/HowlingWhispers/HW-CodaLoader/releases/download/" + tag + "/" + expected,
                           StringComparison.OrdinalIgnoreCase))
                    continue;
                if (best is null || parsed.CompareTo(best) > 0)
                {
                    best = parsed;
                    newest = new Release(parsed.ToString(3), url.ToString(), filename);
                }
            }
        }
        return newest;
    }
}
