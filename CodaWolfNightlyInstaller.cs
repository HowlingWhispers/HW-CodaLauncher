using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Experimental Coda Wolf mod channel, separate from the existing BuildCraft Nightly.
/// Reads only immutable, SHA-256-pinned HW-Mods GitHub prereleases.
/// Never replaces H.O.W.L., BuildCraft, world saves or other player mods.
/// </summary>
internal sealed class CodaWolfNightlyInstaller
{
    internal const string TagPrefix = "nightly-codawolf-";
    internal const string ModJar = "coda-wolf-0.1.0-dev.jar";
    internal const string ChecksumFile = ModJar + ".sha256";
    private const string ReleasesApi =
        "https://api.github.com/repos/HowlingWhispers/HW-Mods/releases?per_page=100&page=";
    private const int MaxJarBytes = 4 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly string _gameRoot;

    internal CodaWolfNightlyInstaller(HttpClient? http = null, string? gameRoot = null)
    {
        _http = http ?? MakeHttpClient();
        _gameRoot = gameRoot ?? NightlyBuildInstaller.GameRoot;
    }

    private string ModsFolder => Path.Combine(_gameRoot, "mods");
    private string JarPath => Path.Combine(ModsFolder, ModJar);
    private string HashMarker => Path.Combine(ModsFolder, ".howl-codawolf-managed.sha256");
    private string TagMarker => Path.Combine(ModsFolder, ".howl-codawolf-tag");

    internal bool HasManagedInstall()
    {
        try
        {
            if (!File.Exists(JarPath) || !File.Exists(HashMarker) || !File.Exists(TagMarker)
                || !HashFile(JarPath).Equals(File.ReadAllText(HashMarker).Trim(), StringComparison.OrdinalIgnoreCase)
                || !File.ReadAllText(TagMarker).Trim().StartsWith(TagPrefix, StringComparison.Ordinal))
                return false;
            VerifyJar(JarPath);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (InvalidDataException) { return false; }
    }

    /// <summary>
    /// Explicit uninstall of this one SHA-256-owned optional companion.
    /// Minecraft saves, ownership data and every other mod remain untouched.
    /// </summary>
    internal void UninstallManaged(Action<string> report)
    {
        if (!File.Exists(JarPath))
        {
            report("Coda Wolf is already uninstalled.");
            return;
        }
        if (!HasManagedInstall())
            throw new IOException("Coda Wolf JAR is modified or not launcher-managed. "
                + "Your file is preserved; review it using Open Mods Folder.");
        File.Delete(JarPath);
        if (File.Exists(HashMarker)) File.Delete(HashMarker);
        if (File.Exists(TagMarker)) File.Delete(TagMarker);
        report("Coda Wolf Companion uninstalled. Saved worlds and companion data preserved.");
    }

    internal async Task<string> InstallLatestAsync(Action<string> report, CancellationToken cancellation,
        bool allowCachedFallback = true)
    {
        ArgumentNullException.ThrowIfNull(report);
        try
        {
            return await InstallOnlineAsync(report, cancellation);
        }
        catch (Exception error) when (error is HttpRequestException
                || (error is OperationCanceledException && !cancellation.IsCancellationRequested))
        {
            if (!allowCachedFallback)
                throw new IOException("GitHub cannot be reached, so the optional Coda Wolf install/update "
                    + "could not be checked. Existing files are preserved.", error);
            if (!HasManagedInstall())
                throw new IOException("GitHub is unreachable and Coda Wolf has no verified local installation. " +
                    "Retry when your GitHub connection works. Existing world saves and mods are untouched.", error);
            string installedTag = File.ReadAllText(TagMarker).Trim();
            report("GitHub Coda Wolf update check unavailable (" + error.GetType().Name + "). " +
                "Using checksum-verified local Coda Wolf " + installedTag +
                ". New updates will be checked next time.");
            return installedTag;
        }
    }

    private async Task<string> InstallOnlineAsync(Action<string> report, CancellationToken cancellation)
    {
        var release = await FindNewestAsync(cancellation);
        // Never trust a version name without verifying its released checksum.
        byte[] checksum = await DownloadAsync(release.ChecksumUrl, 4096, cancellation);
        string[] tokens = Encoding.UTF8.GetString(checksum).Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string expected = tokens.FirstOrDefault() ?? "";
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new InvalidDataException("Coda Wolf release SHA-256 is invalid.");

        Directory.CreateDirectory(ModsFolder);
        foreach (string existing in Directory.EnumerateFiles(ModsFolder, "*.jar", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(existing).Equals(ModJar, StringComparison.OrdinalIgnoreCase)) continue;
            // Detect a same-ID mod in an alternate JAR filename: never introduce
            // duplicates that would abort H.O.W.L. startup.
            using var jar = ZipFile.OpenRead(existing);
            var info = jar.GetEntry("coda.mod.json");
            if (info == null) continue;
            using var stream = info.Open();
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("id", out var id) && id.GetString() == "coda_wolf")
                throw new IOException("Another Coda Wolf JAR exists (" + Path.GetFileName(existing)
                    + "). Both files were preserved. Remove the old mod before updating.");
        }

        string? actual = File.Exists(JarPath) ? HashFile(JarPath) : null;
        string? owned = File.Exists(HashMarker) ? File.ReadAllText(HashMarker).Trim() : null;
        if (actual != null && !actual.Equals(expected, StringComparison.OrdinalIgnoreCase)
            && (owned == null || !owned.Equals(actual, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Coda Wolf mod was changed manually. Existing JAR preserved; remove or move it before updating.");

        if (actual != null && actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(HashMarker, expected);
            File.WriteAllText(TagMarker, release.Tag);
            report("Coda Wolf " + release.Tag + " already installed and SHA-256 verified.");
            return release.Tag;
        }

        report("Downloading Coda Wolf " + release.Tag + " from the GitHub mod release...");
        byte[] bytes = await DownloadAsync(release.PackageUrl, MaxJarBytes, cancellation);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Coda Wolf download checksum mismatch. Existing mod preserved.");

        string staged = Path.Combine(ModsFolder, "." + Guid.NewGuid().ToString("N") + ".coda-wolf.tmp");
        try
        {
            await File.WriteAllBytesAsync(staged, bytes, cancellation);
            VerifyJar(staged);
            if (!HashFile(staged).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Staged Coda Wolf JAR changed during verification.");
            // Install atomically on the same filesystem. Other mods aren't touched.
            File.Move(staged, JarPath, overwrite: true);
            File.WriteAllText(HashMarker, expected);
            File.WriteAllText(TagMarker, release.Tag);
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }
        report("Coda Wolf " + release.Tag + " installed in active Nightly mods. Use a disposable world.");
        return release.Tag;
    }

    private async Task<NightlyReleaseSelector.Release> FindNewestAsync(CancellationToken ct)
    {
        // GitHub's unauthenticated REST allowance is shared across launchers,
        // machines and NATs. A 403 must not strand users on stale Coda JARs.
        try { return await FindFromApiAsync(ct); }
        catch (HttpRequestException apiError) when (apiError.StatusCode is System.Net.HttpStatusCode.Forbidden
                or System.Net.HttpStatusCode.TooManyRequests)
        {
            try
            {
                byte[] bytes = await DownloadAsync(new Uri(NightlyAtomReleaseReader.FeedUrl),
                    512 * 1024, ct);
                var fallback = NightlyAtomReleaseReader.SelectNewest(
                    Encoding.UTF8.GetString(bytes), TagPrefix, ModJar, ChecksumFile);
                if (fallback is not null) return fallback;
            }
            catch (Exception fallbackError) when (fallbackError is HttpRequestException
                    or InvalidDataException or System.Xml.XmlException)
            {
                // Preserve the real GitHub REST failure for installed-cache fallback.
            }
            throw;
        }
    }

    private async Task<NightlyReleaseSelector.Release> FindFromApiAsync(CancellationToken ct)
    {
        // HW-Mods also publishes BuildCraft nightlies, so do not assume our
        // release is on page 1 as that repository grows.
        for (int page = 1; page <= 10; page++)
        {
            byte[] bytes = await DownloadAsync(new Uri(ReleasesApi + page), 512 * 1024, ct);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Invalid GitHub release list.");
            var candidate = NightlyReleaseSelector.SelectNewest(
                document.RootElement, TagPrefix, ModJar, ChecksumFile);
            if (candidate != null) return candidate;
            if (document.RootElement.GetArrayLength() < 100) break;
        }
        throw new InvalidOperationException("No published Coda Wolf Nightly is available in HW-Mods yet.");
    }

    private static void VerifyJar(string path)
    {
        using var jar = ZipFile.OpenRead(path);
        var info = jar.GetEntry("coda.mod.json")
            ?? throw new InvalidDataException("Coda Wolf JAR has no mod metadata.");
        if (jar.GetEntry("dev/howlingwhispers/codawolf/CodaWolfMod.class") == null)
            throw new InvalidDataException("Coda Wolf JAR has no entrypoint.");
        using var stream = info.Open();
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        static bool Has(JsonElement obj, string name, string value)
            => obj.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String
                && e.GetString() == value;
        if (!Has(root, "id", "coda_wolf")
            || !Has(root, "name", "Coda Wolf Companion")
            || !root.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.String
            || !IsVersion(version.GetString())
            || !Has(root, "minecraft", "26.4-snapshot-3")
            || !Has(root, "entrypoint", "dev.howlingwhispers.codawolf.CodaWolfMod"))
            throw new InvalidDataException("Unexpected Coda Wolf metadata; installation refused.");
    }

    // GitHub release tag and asset SHA-256 identify the exact binary. The
    // manifest version is an independently incremented human-readable semver,
    // not a fixed 0.1.0-dev string that blocks future compatible releases.
    private static bool IsVersion(string? value) =>
        value is { Length: >= 5 and <= 48 } &&
        System.Text.RegularExpressions.Regex.IsMatch(value,
            @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?$");

    private async Task<byte[]> DownloadAsync(Uri url, int limit, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit)
            throw new InvalidDataException("Coda Wolf release response exceeds size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int count = await input.ReadAsync(buffer, ct);
            if (count == 0) break;
            if (output.Length + count > limit)
                throw new InvalidDataException("Coda Wolf release response exceeds size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static HttpClient MakeHttpClient()
    {
        var result = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        result.DefaultRequestHeaders.UserAgent.ParseAdd("CodaLauncher-CodaWolf/0.1");
        return result;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
