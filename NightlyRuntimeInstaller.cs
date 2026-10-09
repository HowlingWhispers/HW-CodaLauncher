using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Only H.O.W.L. runtime releases may install or update CodaLoader.jar.
/// Optional BuildCraft nightlies are never a trusted source of loader code.
/// Separate Nightly profile, checksum-pinned archive, and no world/mod changes.
/// </summary>
internal static class NightlyRuntimeInstaller
{
    private const string ReleasesUrl =
        "https://api.github.com/repos/HowlingWhispers/HW-CodaLoader/releases?per_page=20";
    private static readonly HttpClient Http = CreateClient();
    private const int MaxBundle = 96 * 1024 * 1024;

    internal static string VersionMarker => Path.Combine(NightlyBuildInstaller.LoaderRoot,
        ".nightly-loader-version");
    internal static string? InstalledVersion =>
        File.Exists(VersionMarker) ? File.ReadAllText(VersionMarker).Trim() : null;

    private sealed record RuntimeRelease(string Version, Uri Zip, Uri Manifest, string ZipName);

    internal static async Task<string> EnsureLatestAsync(Action<string> report, CancellationToken ct)
    {
        try
        {
            var release = await FindReleaseAsync(ct);
            var loader = Path.Combine(NightlyBuildInstaller.LoaderRoot, "CodaLoader.jar");
            string marker = Path.Combine(NightlyBuildInstaller.LoaderRoot, ".nightly-loader-sha256");
            if (File.Exists(loader) && File.Exists(marker)
                && InstalledVersion == release.Version
                && HashFile(loader).Equals(File.ReadAllText(marker).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                report("H.O.W.L. Nightly runtime v" + release.Version + " verified and current.");
                return NightlyBuildInstaller.LoaderRoot;
            }

            report("Updating H.O.W.L. Nightly runtime to v" + release.Version +
                " from official H.O.W.L. releases (mods unchanged)...");
            byte[] manifestBytes = await DownloadAsync(release.Manifest, 8192, ct);
            using var manifest = JsonDocument.Parse(manifestBytes);
            var metadata = manifest.RootElement;
            string expected = metadata.GetProperty("sha256").GetString() ?? "";
            string version = metadata.GetProperty("version").GetString() ?? "";
            string name = metadata.GetProperty("bundle").GetString() ?? "";
            if (version != release.Version || name != release.ZipName
                || expected.Length != 64 || !expected.All(Uri.IsHexDigit))
                throw new InvalidDataException("H.O.W.L. release manifest identity invalid.");
            byte[] zip = await DownloadAsync(release.Zip, MaxBundle, ct);
            if (!HashBytes(zip).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Official H.O.W.L. Nightly bundle checksum mismatch.");

            string folder = NightlyBuildInstaller.LoaderRoot;
            Directory.CreateDirectory(folder);
            string staging = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using var input = new MemoryStream(zip);
                using var archive = new ZipArchive(input, ZipArchiveMode.Read);
                var entry = archive.GetEntry("CodaLoader.jar")
                    ?? throw new InvalidDataException("Official release missing CodaLoader.jar");
                if (entry.Length <= 0 || entry.Length > MaxBundle)
                    throw new InvalidDataException("Invalid official H.O.W.L. loader size");
                using (var source = entry.Open())
                using (var output = File.Create(staging))
                    await source.CopyToAsync(output, ct);
                // Inspect the candidate JAR before replacing a known-good
                // runtime. A version-specific class is mandatory in every
                // genuine H.O.W.L. bundle.
                using (var loaderZip = ZipFile.OpenRead(staging))
                {
                    if (loaderZip.GetEntry(
                        "dev/howlingwhispers/codaloader/core/CodaTarget.class") == null
                        || loaderZip.GetEntry(
                        "dev/howlingwhispers/codaloader/bootstrap/CodaAgent.class") == null)
                        throw new InvalidDataException("Official loader JAR missing required classes");
                }
                string existing = Path.Combine(folder, "CodaLoader.jar");
                // Preserve the previous version for a manual rollback.
                if (File.Exists(existing))
                {
                    var backups = Path.Combine(folder, "previous-loader-code");
                    Directory.CreateDirectory(backups);
                    File.Copy(existing, Path.Combine(backups,
                        DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") +
                        "-" + Guid.NewGuid().ToString("N") + ".jar"));
                }
                File.Move(staging, existing, true);
                File.WriteAllText(Path.Combine(folder, ".nightly-loader-sha256"), HashFile(existing));
                File.WriteAllText(VersionMarker, release.Version);
                // Compatibility with older Nightly integrity checks; no
                // relation to the separate BuildCraft add-on release tag.
                File.WriteAllText(Path.Combine(folder, ".nightly-tag"), "v" + release.Version);
                report("H.O.W.L. Nightly v" + release.Version +
                    " installed and SHA-256 verified. Optional mods unchanged.");
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
            return folder;
        }
        catch (Exception ex) when (ex is HttpRequestException
               || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            if (!NightlyBuildInstaller.Installed)
                throw new IOException("Official H.O.W.L. release unavailable and no verified " +
                    "Nightly runtime is installed. Stable and saves untouched.", ex);
            report("H.O.W.L. release check unavailable (" + ex.GetType().Name
                + "). Using verified local Nightly v" + (InstalledVersion ?? "unknown-legacy")
                + ". Update NOT confirmed; retry online.");
            return NightlyBuildInstaller.LoaderRoot;
        }
    }

    private static async Task<RuntimeRelease> FindReleaseAsync(CancellationToken ct)
    {
        var data = await DownloadAsync(new Uri(ReleasesUrl), 512 * 1024, ct);
        using var document = JsonDocument.Parse(data);
        RuntimeRelease? best = null;
        Version? highest = null;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.GetProperty("draft").GetBoolean()) continue;
            string tag = item.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var parsed)
                || parsed.Build >= 0 || parsed.Revision >= 0) continue;
            string name = "CodaLoader-" + tag + "-win64.zip";
            Uri? zip = null, manifest = null;
            foreach (var asset in item.GetProperty("assets").EnumerateArray())
            {
                var assetName = asset.GetProperty("name").GetString() ?? "";
                var link = asset.GetProperty("browser_download_url").GetString();
                if (link is null || !Uri.TryCreate(link, UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps
                    || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (assetName == name) zip = uri;
                if (assetName == "update-manifest.json") manifest = uri;
            }
            if (zip is not null && manifest is not null && (highest is null || parsed > highest))
            {
                highest = parsed;
                best = new RuntimeRelease(tag[1..],zip,manifest,name);
            }
        }
        return best ?? throw new IOException("No versioned official H.O.W.L. runtime release available.");
    }

    private static async Task<byte[]> DownloadAsync(Uri url, int max, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > max)
            throw new InvalidDataException("H.O.W.L. release exceeds download size limit");
        await using var incoming = await response.Content.ReadAsStreamAsync(ct);
        using var result = new MemoryStream();
        byte[] buffer = new byte[65536];
        for (int n; (n = await incoming.ReadAsync(buffer,ct)) > 0;)
        {
            if (result.Length + n > max) throw new InvalidDataException("Oversized H.O.W.L. release");
            result.Write(buffer,0,n);
        }
        return result.ToArray();
    }

    private static string HashBytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string HashFile(string name)
    {
        using var stream = File.OpenRead(name);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private static HttpClient CreateClient()
    {
        var http = new HttpClient {Timeout=TimeSpan.FromMinutes(3)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CodaLauncher-HOWL-NightlyRuntime/0.1");
        return http;
    }
}
