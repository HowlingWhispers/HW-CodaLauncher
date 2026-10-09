using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Discover optional H.O.W.L. mod JAR releases from owner-controlled GitHub repos.
/// Never install an unknown release merely because it was discovered.
/// Executable mod code is installed only via an explicit player action.
/// </summary>
internal sealed record GitHubModRelease(
    string Id, string Name, string Version, string Tag, string AssetName,
    string DownloadUrl, string Repository, bool NightlyOnly = true);

internal sealed class GitHubModCatalog
{
    private static readonly string[] Repositories = ["HW-CodaLoader", "HW-Mods"];
    private static readonly Regex Jar = new(
        @"^(?<slug>[a-z][a-z0-9-]*)-(?<version>\d+\.\d+\.\d+(?:-[a-z0-9.-]+)?)\.jar$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "coda-wolf", "hw-essentials", "howl-api", "buildcraft-cml", "hello-coda",
        // Loader/SDK bootstrap artifacts belong to the launcher update pipeline,
        // never the optional gameplay-mod shelf.
        "codaloader", "coda-loader", "hw-codaloader", "howl-loader",
        "howl-sdk", "codaloader-bootstrap", "cml-loader"
    };
    // HW-Mods is the normal discovery source. BuildCraft Lite was released
    // directly from HW-CodaLoader before a dedicated mods publisher existed;
    // keep that one historical release installable until it is migrated.
    // Never interpret arbitrary loader release assets as gameplay mods.
    internal static bool IsOptionalGameplayMod(string slug, string repository) =>
        slug.Length > 0 && !Excluded.Contains(slug) &&
        !slug.EndsWith("-bootstrap", StringComparison.OrdinalIgnoreCase) &&
        (repository == "HW-Mods" ||
         (repository == "HW-CodaLoader" && slug == "buildcraft-lite"));

    private static readonly HttpClient SharedHttp = MakeClient();
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<GitHubModRelease> _cached = [];
    private DateTimeOffset _expires;

    internal GitHubModCatalog(HttpClient? http = null) => _http = http ?? SharedHttp;
    internal IReadOnlyList<GitHubModRelease> Cached => _cached;

    private static HttpClient MakeClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CodaLauncher-HOWL-ModCatalog/1.0");
        return client;
    }

    internal async Task<IReadOnlyList<GitHubModRelease>> DiscoverAsync(CancellationToken ct,
        bool force = false)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!force && DateTimeOffset.UtcNow < _expires) return _cached;
            var all = new List<GitHubModRelease>();
            int available = 0;
            foreach (var repo in Repositories)
            {
                try
                {
                    string endpoint = $"https://api.github.com/repos/HowlingWhispers/{repo}/releases?per_page=40";
                    using var response = await _http.GetAsync(endpoint, ct);
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync(ct);
                    all.AddRange(ParseReleases(json, repo));
                    available++;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested &&
                    ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
                {
                    // One unavailable repo must not erase the other repo's releases.
                    System.Diagnostics.Debug.WriteLine("GitHub mod index unavailable: " + ex.Message);
                }
            }
            if (available > 0)
            {
                _cached = all.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(x => VersionValue(x.Version))
                        .ThenByDescending(x => x.Tag, StringComparer.Ordinal).First())
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                _expires = DateTimeOffset.UtcNow.AddMinutes(10);
            }
            else _expires = DateTimeOffset.UtcNow.AddMinutes(1);
            return _cached;
        }
        finally { _lock.Release(); }
    }

    internal static IReadOnlyList<GitHubModRelease> ParseReleases(string json, string repo)
    {
        if (!Repositories.Contains(repo, StringComparer.Ordinal))
            throw new ArgumentException("Untrusted mod source repository", nameof(repo));
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected GitHub release array");
        var result = new List<GitHubModRelease>();
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            if (!release.TryGetProperty("tag_name", out var tagNode)) continue;
            string tag = tagNode.GetString() ?? "";
            if (tag.Length is < 1 or > 140 || tag.Contains('/')) continue;
            if (!release.TryGetProperty("assets", out var assets)
                || assets.ValueKind != JsonValueKind.Array) continue;
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameNode)
                    || !asset.TryGetProperty("browser_download_url", out var urlNode)) continue;
                string filename = nameNode.GetString() ?? "";
                var match = Jar.Match(filename);
                if (!match.Success) continue;
                string slug = match.Groups["slug"].Value.ToLowerInvariant();
                if (!IsOptionalGameplayMod(slug, repo)) continue;
                string url = urlNode.GetString() ?? "";
                if (!TrustedUrl(url, repo, tag, filename)) continue;
                string version = match.Groups["version"].Value;
                string display = string.Join(" ", slug.Split('-')
                    .Select(s => char.ToUpperInvariant(s[0]) + s[1..]));
                result.Add(new GitHubModRelease("github:" + slug, display, version, tag,
                    filename, url, repo));
            }
        }
        return result;
    }

    private static Version VersionValue(string text)
    {
        string core = text.Split('-', 2)[0];
        return Version.TryParse(core, out var parsed) ? parsed : new Version(0, 0);
    }

    internal static bool TrustedUrl(string url, string repo, string tag, string filename)
    {
        if (!Repositories.Contains(repo, StringComparer.Ordinal)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host != "github.com" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return false;
        return uri.AbsolutePath.Equals(
            $"/HowlingWhispers/{repo}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(filename)}",
            StringComparison.Ordinal);
    }
}
