using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HowlingWhispers.CodaLauncher;

/// <summary>Automatic ownership-preserving updates for declared first-party mods only.</summary>
internal sealed class OfficialModsInstaller
{
    internal const string ManifestUrl = "https://raw.githubusercontent.com/HowlingWhispers/HW-Mods/main/mods/official-mods.json";
    internal sealed record Requirement(string Slug, string ModId, string Name, string Channel,
        string MinimumLoader, string RequiredLoaderClass);
    private readonly HttpClient _http;
    private readonly GitHubModCatalog _catalog;
    private readonly GitHubModStore _store;
    private IReadOnlyList<Requirement> _requirements = [new("howl-sorting-pipes", "howl_sorting_pipes", "H.O.W.L. Sorting Pipes", "nightly", "0.0.38", "dev/howlingwhispers/codaloader/api/CodaItemFilter.class")];
    internal OfficialModsInstaller(HttpClient? http = null)
    {
        _http = http ?? SharedHttp;
        _catalog = new GitHubModCatalog(_http);
        _store = new GitHubModStore(_http);
    }
    private static readonly HttpClient SharedHttp = Client();
    private static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CodaLauncher-Official-Mods/1.0");
        return client;
    }
    internal bool IsRequired(string id, string channel) => _requirements.Any(r =>
        "github:" + r.Slug == id && Applies(r, channel));
    private static bool Applies(Requirement r, string channel) => r.Channel == "both" || r.Channel == channel;

    internal static IReadOnlyList<Requirement> ParseManifest(string text)
    {
        if (text.Length > 65536) throw new InvalidDataException("Official mod manifest too large");
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        if (root.GetProperty("schema").GetInt32() != 1 || root.GetProperty("minecraft").GetString() != "26.4-snapshot-3")
            throw new InvalidDataException("Official mod manifest compatibility mismatch");
        var result = new List<Requirement>();
        foreach (var node in root.GetProperty("mods").EnumerateArray())
        {
            var r = new Requirement(node.GetProperty("slug").GetString() ?? "", node.GetProperty("modId").GetString() ?? "",
                node.GetProperty("name").GetString() ?? "", node.GetProperty("channel").GetString() ?? "",
                node.GetProperty("minimumLoader").GetString() ?? "", node.GetProperty("requiredLoaderClass").GetString() ?? "");
            if (!Regex.IsMatch(r.Slug, "^[a-z][a-z0-9-]{0,62}$") || !GitHubModCatalog.IsOptionalGameplayMod(r.Slug, "HW-Mods")
                || r.ModId != r.Slug.Replace('-', '_') || r.Name.Length is < 1 or > 120
                || r.Channel is not ("stable" or "nightly" or "both") || !Version.TryParse(r.MinimumLoader, out _)
                || !Regex.IsMatch(r.RequiredLoaderClass, "^dev/howlingwhispers/codaloader/[A-Za-z0-9_/$]+\\.class$")
                || result.Any(x => x.Slug == r.Slug) || result.Count >= 32)
                throw new InvalidDataException("Invalid or duplicate official mod requirement");
            result.Add(r);
        }
        return result;
    }

    internal static void VerifyLoader(string loaderJar, Requirement requirement)
    {
        using var zip = ZipFile.OpenRead(loaderJar);
        var metadata = zip.GetEntry("codaloader/runtime-version.json");
        if (metadata is null || metadata.Length is < 1 or > 4096 || zip.GetEntry(requirement.RequiredLoaderClass) is null)
            throw new InvalidDataException(requirement.Name + " requires H.O.W.L. " + requirement.MinimumLoader + "+. Update the loader first.");
        using var input = metadata.Open(); using var json = JsonDocument.Parse(input);
        if (json.RootElement.GetProperty("minecraft").GetString() != "26.4-snapshot-3"
            || !Version.TryParse(json.RootElement.GetProperty("version").GetString(), out var version)
            || version < Version.Parse(requirement.MinimumLoader))
            throw new InvalidDataException(requirement.Name + " requires H.O.W.L. " + requirement.MinimumLoader + "+.");
    }

    internal async Task EnsureAsync(string gameRoot, string loaderJar, string channel,
        Action<string> report, CancellationToken ct)
    {
        if (channel is not ("stable" or "nightly")) throw new ArgumentException("Unknown mod channel");
        string cache = Path.Combine(gameRoot, "config", "howl-required-mods.json");
        string text;
        try { text = await DownloadTextAsync(ManifestUrl, 65536, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
        {
            if (!File.Exists(cache)) {
                if (!_requirements.Any(r => Applies(r, channel))) {
                    report("Official mod catalog unavailable; no additional required mods are declared for this channel.");
                    return;
                }
                throw new IOException("Required mod catalog unavailable; retry online.", ex);
            }
            _requirements = ParseManifest(await File.ReadAllTextAsync(cache, ct));
            foreach (var r in _requirements.Where(r => Applies(r, channel)))
            {
                VerifyLoader(loaderJar, r);
                if (!VerifiedReceipt(gameRoot, r)) throw new IOException("Required " + r.Name + " unavailable or modified; retry online.");
            }
            report("Required mod update check unavailable; using checksum-verified installed mods. Updates not confirmed.");
            return;
        }
        _requirements = ParseManifest(text);
        foreach (var r in _requirements.Where(r => Applies(r, channel)))
        {
            VerifyLoader(loaderJar, r);
            var available = await _catalog.DiscoverAsync(ct, force: true);
            var release = available.FirstOrDefault(x => x.Repository == "HW-Mods" && x.Id == "github:" + r.Slug);
            if (release is null)
            {
                if (!VerifiedReceipt(gameRoot, r)) throw new IOException("Required " + r.Name + " release is unavailable.");
                report(r.Name + " release check unavailable; installed checksum-verified mod retained. Update not confirmed.");
                continue;
            }
            try
            {
                string checksum = await DownloadTextAsync(release.DownloadUrl + ".sha256", 512, ct);
                string hash = checksum.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid published " + r.Name + " SHA-256");
                await _store.InstallAsync(release with { ExpectedSha256 = hash, Required = true }, gameRoot, report, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
            {
                if (!VerifiedReceipt(gameRoot, r)) throw new IOException("Required " + r.Name + " download unavailable; retry online.", ex);
                report(r.Name + " update download unavailable; using checksum-verified installed mod. Update not confirmed.");
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        string staged = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(staged, text, ct); File.Move(staged, cache, true); }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }
    private static bool VerifiedReceipt(string gameRoot, Requirement r)
    {
        var key = new GitHubModRelease("github:" + r.Slug, r.Name, "", "", "", "", "HW-Mods");
        var receipt = GitHubModStore.ReadReceipt(gameRoot, key);
        return receipt?.ModId == r.ModId && GitHubModStore.IsManaged(gameRoot, key);
    }
    private async Task<string> DownloadTextAsync(string url, int maximum, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Oversized official mod metadata");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream(); byte[] buffer = new byte[4096];
        for (int count; (count = await input.ReadAsync(buffer, ct)) > 0;)
        { if (output.Length + count > maximum) throw new InvalidDataException("Oversized official mod metadata"); output.Write(buffer, 0, count); }
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }
}
