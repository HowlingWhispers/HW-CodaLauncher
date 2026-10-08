using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace HowlingWhispers.CodaLauncher;

internal static class AppPaths
{
    // Correct spelling for new installs. Existing misspelled roots are
    // retained to avoid separating a user's worlds, profiles and credentials.
    // A full data-root migration requires an explicit backup-first operation.
    private static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string LegacyRoot = Path.Combine(Roaming, ".howlingshispers");
    private static readonly string CanonicalRoot = Path.Combine(Roaming, ".howlingwhispers");
    public static string InstallRoot { get; } = Directory.Exists(LegacyRoot)
        ? LegacyRoot : CanonicalRoot;

    public static string MinecraftRoot => Path.Combine(InstallRoot, "minecraft");
    public static string LoaderRoot => Path.Combine(InstallRoot, "loader");
    public static string PacksRoot => Path.Combine(InstallRoot, "packs");
    public static string ResourcePacksRoot => Path.Combine(InstallRoot, "resourcepacks");
    public static string CmlBasePackRoot => Path.Combine(PacksRoot, "cml-base");
    public static string CmlBaseResourcesRoot => Path.Combine(ResourcePacksRoot, "cml-base-resources");
    public static string LogsRoot => Path.Combine(InstallRoot, "logs");
    public static string CmlBasePackMarker => Path.Combine(CmlBasePackRoot, ".installed-version");
    public static string CmlBasePackFingerprint => Path.Combine(CmlBasePackRoot, ".installed-fingerprint");
    public static string CmlBaseResourcesMarker => Path.Combine(CmlBaseResourcesRoot, ".installed-version");
    public static string CmlBaseResourcesSha256 => Path.Combine(CmlBaseResourcesRoot, ".installed-sha256");

    private static string LocalRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HowlingWhispers", "CodaLauncher");

    public static string SettingsFile => Path.Combine(InstallRoot, "launcher", "settings.json");
    public static string WebViewData => Path.Combine(LocalRoot, "WebView2");
}

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public LauncherSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return new LauncherSettings();
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(AppPaths.SettingsFile), Json) ?? new LauncherSettings();
        }
        catch { return new LauncherSettings(); }
    }

    public void Save(LauncherSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsFile)!);
        var temp = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, AppPaths.SettingsFile, true);
    }
}

internal static class LoaderLocator
{
    public static string? Resolve(string configured)
    {
        foreach (var candidate in Candidates(configured))
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (IsReady(full)) return full;
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            try { return Path.GetFullPath(configured); } catch { }
        }
        return null;
    }

    public static bool IsReady(string? dir)
        => !string.IsNullOrWhiteSpace(dir)
           && File.Exists(Path.Combine(dir, "CodaLoader.jar"))
           && File.Exists(Path.Combine(dir, "Launch-CodaLoader.bat"));

    private static IEnumerable<string> Candidates(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) yield return configured;
        yield return AppPaths.LoaderRoot;
        var env = Environment.GetEnvironmentVariable("CODALOADER_HOME");
        if (!string.IsNullOrWhiteSpace(env)) yield return env;
        yield return Path.Combine(Environment.CurrentDirectory, "CodaLoader");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CodaLoader");
        yield return Path.Combine(AppContext.BaseDirectory, "..", "CodaLoader");
        yield return Path.Combine(AppContext.BaseDirectory, "..", "HW-CodaLoader");
        yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "HW-CodaLoader");
    }
}

internal sealed class ModScanner
{
    public IReadOnlyList<ModInfo> Scan(string? loaderDirectory)
    {
        if (string.IsNullOrWhiteSpace(loaderDirectory)) return [];
        var mods = Path.Combine(loaderDirectory, "mods");
        if (!Directory.Exists(mods)) return [];
        return Directory.EnumerateFiles(mods, "*.jar").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Select(Read).ToList();
    }

    private static ModInfo Read(string jar)
    {
        var file = Path.GetFileName(jar);
        try
        {
            using var zip = ZipFile.OpenRead(jar);
            var entry = zip.GetEntry("coda.mod.json") ?? throw new InvalidDataException("coda.mod.json missing");
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            string S(string name, string fallback) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
            return new ModInfo(file, S("id", Path.GetFileNameWithoutExtension(jar)), S("name", Path.GetFileNameWithoutExtension(jar)), S("version", "?"), true, null);
        }
        catch (Exception ex)
        {
            return new ModInfo(file, Path.GetFileNameWithoutExtension(jar), file, "?", false, ex.Message);
        }
    }
}

internal sealed class LogBuffer
{
    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();

    public string Add(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (_gate)
        {
            _lines.Enqueue(line);
            while (_lines.Count > 10000) _lines.Dequeue();
        }
        return line;
    }

    public string[] Snapshot()
    {
        lock (_gate) return _lines.ToArray();
    }
}

internal sealed record CodaLoaderReleaseInfo(
    string Version,
    string BundleUrl,
    string BundleName);

internal sealed record ManagedInstallStatus(
    bool LoaderInstalled,
    bool LoaderCurrent,
    string? InstalledLoaderVersion,
    string LatestLoaderVersion,
    bool ResourceInstalled,
    bool ResourceCurrent,
    bool PackInstalled,
    bool PackCurrent)
{
    public bool Installed => LoaderInstalled && ResourceInstalled && PackInstalled;
    public bool Current => LoaderCurrent && ResourceCurrent && PackCurrent;
    public bool UpdatesAvailable => Installed && !Current;
}

internal sealed class InstallService
{
    private const string ReleasesApi =
        "https://api.github.com/repos/HowlingWhispers/HW-CodaLoader/releases?per_page=20";
    private const string CmlBasePackVersion = "1";
    private const string CmlBaseResourcesAssetName = "CML-Base-Resources-v1.zip";
    private const string CmlBaseResourcesVersion = "1";
    private const string CmlBaseResourcesSha256 =
        "031f3b05d3efaf9b40436fedfee64cecfd92cf8d259edc3b637a633905231838";

    private static readonly HttpClient Http = CreateHttp();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public bool LoaderReady => LoaderLocator.IsReady(AppPaths.LoaderRoot);

    public bool CmlBaseResourcesReady(string version, string sha256)
    {
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(sha256)) return false;
        if (!File.Exists(AppPaths.CmlBaseResourcesMarker)
            || !File.Exists(AppPaths.CmlBaseResourcesSha256))
            return false;

        var installedVersion = File.ReadAllText(AppPaths.CmlBaseResourcesMarker).Trim();
        var installedSha = File.ReadAllText(AppPaths.CmlBaseResourcesSha256).Trim();

        return string.Equals(installedVersion, version, StringComparison.Ordinal)
            && string.Equals(installedSha, sha256, StringComparison.OrdinalIgnoreCase);
    }

    public bool CmlBasePackReady(
        string packVersion,
        ResourcePackCatalogInfo resource)
    {
        if (string.IsNullOrWhiteSpace(packVersion)) return false;
        if (!File.Exists(AppPaths.CmlBasePackMarker)
            || !File.Exists(AppPaths.CmlBasePackFingerprint))
            return false;

        var installedVersion = File.ReadAllText(AppPaths.CmlBasePackMarker).Trim();
        var installedFingerprint = File.ReadAllText(AppPaths.CmlBasePackFingerprint).Trim();
        var expectedFingerprint = PackFingerprint(packVersion, resource);

        return string.Equals(installedVersion, packVersion, StringComparison.Ordinal)
            && string.Equals(installedFingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase)
            && CmlBaseResourcesReady(resource.Version, resource.Sha256);
    }

    public async Task<ManagedInstallStatus> CheckManagedStateAsync(
        LauncherFeed feed,
        CancellationToken ct)
    {
        var latestLoader = await GetLatestLoaderReleaseAsync(ct);
        var installedLoaderVersion = ReadInstalledLoaderVersion();
        var loaderInstalled = LoaderReady;
        var loaderCurrent = loaderInstalled
            && string.Equals(
                installedLoaderVersion,
                latestLoader.Version,
                StringComparison.OrdinalIgnoreCase)
            && ManagedMods.IsCurrent(AppPaths.LoaderRoot, AppPaths.MinecraftRoot);

        var resource = ResolveCmlBaseResources(feed);
        var resourceInstalled = File.Exists(AppPaths.CmlBaseResourcesMarker);
        var resourceCurrent = CmlBaseResourcesReady(resource.Version, resource.Sha256);

        var pack = feed.Packs.FirstOrDefault(item =>
            item.Id.Equals("cml-base", StringComparison.OrdinalIgnoreCase));
        var packVersion = string.IsNullOrWhiteSpace(pack?.Version)
            ? CmlBasePackVersion
            : pack!.Version;
        var packInstalled = File.Exists(AppPaths.CmlBasePackMarker);
        var packCurrent = CmlBasePackReady(packVersion, resource);

        return new ManagedInstallStatus(
            loaderInstalled,
            loaderCurrent,
            installedLoaderVersion,
            latestLoader.Version,
            resourceInstalled,
            resourceCurrent,
            packInstalled,
            packCurrent);
    }

    public async Task InstallOrRepairAsync(
        LauncherFeed feed,
        Action<string> progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.InstallRoot);
        Directory.CreateDirectory(AppPaths.MinecraftRoot);
        Directory.CreateDirectory(AppPaths.PacksRoot);
        Directory.CreateDirectory(AppPaths.ResourcePacksRoot);
        Directory.CreateDirectory(AppPaths.LogsRoot);

        var latestLoader = await GetLatestLoaderReleaseAsync(ct);
        var installedLoaderVersion = ReadInstalledLoaderVersion();

        // Historical release ZIPs installed starter mods under loader/run/mods.
        // This folder was NOT scanned by the Minecraft agent when launched by
        // CodaLauncher. Copy user JARs to the actual game folder first.
        ManagedMods.MigrateLegacy(AppPaths.LoaderRoot, AppPaths.MinecraftRoot, progress);

        if (!LoaderReady
            || !string.Equals(
                installedLoaderVersion,
                latestLoader.Version,
                StringComparison.OrdinalIgnoreCase))
        {
            progress(installedLoaderVersion is null
                ? $"Installing H.O.W.L. {latestLoader.Version}..."
                : $"Updating H.O.W.L. {installedLoaderVersion} -> {latestLoader.Version}...");
            await InstallLoaderAsync(latestLoader, progress, ct);
            var confirmedVersion = ReadInstalledLoaderVersion();
            if (!string.Equals(confirmedVersion, latestLoader.Version, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"H.O.W.L. installation verification failed: expected {latestLoader.Version}, found {confirmedVersion ?? "unknown"}.");
            progress($"H.O.W.L. update completed and verified: installed {confirmedVersion}; latest release {latestLoader.Version}.");
        }
        else
        {
            progress($"H.O.W.L. checked: installed {installedLoaderVersion}; latest release {latestLoader.Version}; no update needed.");
        }

        var resource = ResolveCmlBaseResources(feed);
        progress("Resolving HOWL Base dependencies...");
        if (!CmlBaseResourcesReady(resource.Version, resource.Sha256))
        {
            progress("Updating required resource pack: HOWL Base Resources...");
            await InstallCmlBaseResourcesAsync(resource, progress, ct);
        }
        else
        {
            progress("HOWL Base Resources is current.");
        }

        var pack = feed.Packs.FirstOrDefault(item =>
            item.Id.Equals("cml-base", StringComparison.OrdinalIgnoreCase));
        var packVersion = string.IsNullOrWhiteSpace(pack?.Version)
            ? CmlBasePackVersion
            : pack!.Version;

        Directory.CreateDirectory(AppPaths.CmlBasePackRoot);
        File.WriteAllText(AppPaths.CmlBasePackMarker, packVersion);
        File.WriteAllText(AppPaths.CmlBasePackFingerprint, PackFingerprint(packVersion, resource));
        progress("HOWL Base is current.");

        // The example Hello Coda is no longer installed by default.
        // User-supplied copies are handled by no-clobber legacy migration.
        ManagedMods.Install(AppPaths.LoaderRoot, AppPaths.MinecraftRoot, progress);
        // Do not remove any legacy content. Once every active mod is in place,
        // move the old duplicate folder into a named preserved backup.
        ManagedMods.ArchiveLegacy(AppPaths.LoaderRoot, progress);
        progress("Install ready.");
    }

    public async Task InstallCmlBaseResourcesOnlyAsync(
        LauncherFeed feed,
        Action<string> progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.InstallRoot);
        Directory.CreateDirectory(AppPaths.ResourcePacksRoot);

        var resource = ResolveCmlBaseResources(feed);
        if (CmlBaseResourcesReady(resource.Version, resource.Sha256))
        {
            progress("HOWL Base Resources is already current.");
            return;
        }

        progress("Updating resource pack: HOWL Base Resources...");
        await InstallCmlBaseResourcesAsync(resource, progress, ct);
        progress("CML Base Resources ready.");
    }

    private async Task<CodaLoaderReleaseInfo> GetLatestLoaderReleaseAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync(ReleasesApi, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;

            var tag = release.GetProperty("tag_name").GetString() ?? "";
            var version = tag.StartsWith('v') ? tag[1..] : tag;

            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var candidate = asset.GetProperty("name").GetString() ?? "";
                if (!candidate.StartsWith("CodaLoader-v", StringComparison.OrdinalIgnoreCase)
                    || !candidate.EndsWith("-win64.zip", StringComparison.OrdinalIgnoreCase))
                    continue;

                var url = asset.GetProperty("browser_download_url").GetString();
                if (!string.IsNullOrWhiteSpace(url))
                    return new CodaLoaderReleaseInfo(version, url, candidate);
            }
        }

        throw new InvalidOperationException(
            "No current HW-CodaLoader release with a Windows bundle is available.");
    }

    private static string? ReadInstalledLoaderVersion()
    {
        if (!LoaderLocator.IsReady(AppPaths.LoaderRoot)) return null;

        try
        {
            var jar = Path.Combine(AppPaths.LoaderRoot, "CodaLoader.jar");
            var info = new ProcessStartInfo("java")
            {
                WorkingDirectory = AppPaths.LoaderRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add("-jar");
            info.ArgumentList.Add(jar);
            info.ArgumentList.Add("--version");

            using var process = Process.Start(info);
            if (process is null) return null;
            if (!process.WaitForExit(10_000))
            {
                try { process.Kill(true); } catch { }
                return null;
            }

            if (process.ExitCode != 0) return null;
            var version = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch
        {
            return null;
        }
    }

    private async Task InstallLoaderAsync(
        CodaLoaderReleaseInfo release,
        Action<string> progress,
        CancellationToken ct)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CodaLauncher", Guid.NewGuid().ToString("N"));
        var zip = Path.Combine(tempRoot, release.BundleName);
        var staging = Path.Combine(tempRoot, "loader");
        Directory.CreateDirectory(tempRoot);

        try
        {
            progress($"Downloading H.O.W.L. {release.Version} from HW-CodaLoader Releases...");
            await DownloadAsync(new Uri(release.BundleUrl), zip, ct, progress);
            ZipFile.ExtractToDirectory(zip, staging, true);

            progress($"Installing H.O.W.L. {release.Version}...");
            ReplaceDirectory(staging, AppPaths.LoaderRoot);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static string PackFingerprint(
        string packVersion,
        ResourcePackCatalogInfo resource)
    {
        var canonical = string.Join("|",
            packVersion,
            resource.Id,
            resource.Version,
            resource.Sha256.ToLowerInvariant());

        return Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static ResourcePackCatalogInfo ResolveCmlBaseResources(LauncherFeed feed)
    {
        var catalog = feed.ResourcePacks.FirstOrDefault(item =>
            item.Id.Equals("cml-base-resources", StringComparison.OrdinalIgnoreCase));

        if (catalog is not null)
        {
            if (string.IsNullOrWhiteSpace(catalog.Version))
                catalog.Version = CmlBaseResourcesVersion;
            if (string.IsNullOrWhiteSpace(catalog.Sha256))
                catalog.Sha256 = CmlBaseResourcesSha256;
            return catalog;
        }

        return new ResourcePackCatalogInfo
        {
            Id = "cml-base-resources",
            Name = "HOWL Base Resources",
            Version = CmlBaseResourcesVersion,
            Required = true,
            Description = "Official HOWL title branding, panorama scenes, menu music, splashes and shared presentation assets.",
            Url = string.IsNullOrWhiteSpace(feed.BasePack.Url)
                ? "/assets/CML-Base-Resources-v1.zip"
                : feed.BasePack.Url,
            Sha256 = CmlBaseResourcesSha256,
            RequiredBy = ["cml-base"]
        };
    }

    private async Task InstallCmlBaseResourcesAsync(
        ResourcePackCatalogInfo resource,
        Action<string> progress,
        CancellationToken ct)
    {
        var version = string.IsNullOrWhiteSpace(resource.Version)
            ? CmlBaseResourcesVersion
            : resource.Version;
        var expectedSha = string.IsNullOrWhiteSpace(resource.Sha256)
            ? CmlBaseResourcesSha256
            : resource.Sha256;

        progress($"Searching HW-CodaLoader Releases for {CmlBaseResourcesAssetName}...");
        Uri? uri = await FindReleaseAssetAsync(CmlBaseResourcesAssetName, ct, progress);

        if (uri is null
            && CurrentFeedBase is not null
            && !string.IsNullOrWhiteSpace(resource.Url))
        {
            uri = Uri.TryCreate(resource.Url, UriKind.Absolute, out var absolute)
                ? absolute
                : new Uri(CurrentFeedBase, resource.Url.TrimStart('/'));
            progress("CML Base Resources not attached to HW-CodaLoader release; trying launcher feed fallback...");
            progress($"Resolved fallback URL: {uri}");
        }

        if (uri is null)
        {
            throw new InvalidOperationException(
                "CML-Base-Resources-v1.zip is not available from HW-CodaLoader Releases or the launcher feed.");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "CodaLauncher", Guid.NewGuid().ToString("N"));
        var zip = Path.Combine(tempRoot, CmlBaseResourcesAssetName);
        var staging = Path.Combine(tempRoot, "resourcepack");
        Directory.CreateDirectory(tempRoot);

        try
        {
            progress($"Downloading from: {uri}");
            progress($"Temporary download target: {zip}");
            await DownloadAsync(uri, zip, ct, progress);

            var size = new FileInfo(zip).Length;
            progress($"Download complete: {size:N0} bytes.");

            var actual = Sha256(zip);
            progress($"SHA-256 actual: {actual}");
            progress($"SHA-256 expected: {expectedSha}");
            if (!actual.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"CML Base Resources failed SHA-256 verification. Expected {expectedSha}, got {actual}.");

            progress($"Extracting ZIP to: {staging}");
            ZipFile.ExtractToDirectory(zip, staging, true);
            progress("ZIP extraction completed.");

            var topLevel = Directory.Exists(staging)
                ? string.Join(", ", Directory.EnumerateFileSystemEntries(staging)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name)))
                : "(staging directory missing)";
            progress($"Extracted top-level entries: {topLevel}");

            if (!Directory.Exists(Path.Combine(staging, "branding"))
                || !Directory.Exists(Path.Combine(staging, "music", "default")))
                throw new InvalidDataException(
                    "HOWL Base Resources is missing required branding or default music.");

            progress($"Installing resource pack to: {AppPaths.CmlBaseResourcesRoot}");
            ReplaceDirectory(staging, AppPaths.CmlBaseResourcesRoot);
            File.WriteAllText(AppPaths.CmlBaseResourcesMarker, version);
            File.WriteAllText(AppPaths.CmlBaseResourcesSha256, expectedSha);
            progress($"Installed resource state: version {version}, SHA-256 {expectedSha}");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task<Uri?> FindReleaseAssetAsync(
        string assetName,
        CancellationToken ct,
        Action<string>? progress = null)
    {
        progress?.Invoke($"Release API: {ReleasesApi}");
        using var response = await Http.GetAsync(ReleasesApi, ct);
        progress?.Invoke($"Release API HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var releaseCount = 0;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            releaseCount++;
            var tag = release.TryGetProperty("tag_name", out var tagValue)
                ? tagValue.GetString() ?? "(untagged)"
                : "(untagged)";

            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var candidate = asset.GetProperty("name").GetString() ?? "";
                if (!candidate.Equals(assetName, StringComparison.OrdinalIgnoreCase)) continue;

                progress?.Invoke($"Found {assetName} on release {tag}.");
                var url = asset.GetProperty("browser_download_url").GetString();
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri;
            }
        }

        progress?.Invoke($"Checked {releaseCount} non-draft release(s); {assetName} was not found.");
        return null;
    }

    public Uri? CurrentFeedBase { get; set; }

    private static async Task DownloadAsync(
        Uri uri,
        string target,
        CancellationToken ct,
        Action<string>? progress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        progress?.Invoke($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        if (response.Content.Headers.ContentLength is long length)
            progress?.Invoke($"Server Content-Length: {length:N0} bytes.");
        progress?.Invoke($"Content-Type: {response.Content.Headers.ContentType?.ToString() ?? "(none)"}");
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(target);
        await input.CopyToAsync(output, ct);
    }

    private static string Sha256(string file)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static void ReplaceDirectory(string source, string target)
    {
        // Distribution updates own precisely two files. Replacing the whole
        // loader directory previously DELETED loader/run/mods, which may hold
        // user JARs or unresolved conflicts. Preserve all unknown files.
        string[] owned = ["CodaLoader.jar", "Launch-CodaLoader.bat"];
        foreach (string name in owned)
            if (!File.Exists(Path.Combine(source, name)))
                throw new IOException("Loader bundle is missing managed file " + name);

        Directory.CreateDirectory(target);
        string backup = Path.Combine(target, "previous-loader-code",
            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        foreach (string name in owned)
        {
            string original = Path.Combine(target, name);
            string incoming = Path.Combine(source, name);
            string pending = Path.Combine(target, "." + Guid.NewGuid().ToString("N") + ".tmp");
            if (File.Exists(original))
                File.Copy(original, Path.Combine(backup, name));
            try
            {
                File.Copy(incoming, pending);
                if (Sha256(pending) != Sha256(incoming))
                    throw new IOException("Loader update integrity check failed: " + name);
                File.Move(pending, original, overwrite: true);
            }
            finally
            {
                if (File.Exists(pending)) File.Delete(pending);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch { }
    }

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CodaLauncher/0.2");
        return client;
    }
}

internal sealed record LauncherUpdateInfo(
    string Version,
    string BundleUrl,
    string Sha256,
    string BundleName,
    string ReleaseUrl);

internal sealed record LauncherUpdateProgress(
    string Stage,
    string Message,
    int? Percent = null);

internal sealed record PreparedLauncherUpdate(
    string Version,
    string StagingDirectory,
    string InstallRoot);

internal static class SelfUpdater
{
    private const string ReleasesApi =
        "https://api.github.com/repos/HowlingWhispers/HW-CodaLauncher/releases?per_page=10";
    private static readonly HttpClient Http = CreateHttp();

    public static async Task<LauncherUpdateInfo?> CheckAsync(string currentVersion, CancellationToken ct)
    {
        using var response = await Http.GetAsync(ReleasesApi, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;

            var tag = release.GetProperty("tag_name").GetString() ?? "";
            var version = tag.StartsWith('v') ? tag[1..] : tag;
            var releaseUrl = release.TryGetProperty("html_url", out var html)
                ? html.GetString() ?? "https://github.com/HowlingWhispers/HW-CodaLauncher/releases"
                : "https://github.com/HowlingWhispers/HW-CodaLauncher/releases";
            if (CompareVersions(version, currentVersion) <= 0) return null;

            string? bundleUrl = null;
            string? manifestUrl = null;
            string? bundleName = null;

            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var url = asset.GetProperty("browser_download_url").GetString();
                if (name.StartsWith("CodaLauncher-v", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith("-win64.zip", StringComparison.OrdinalIgnoreCase))
                {
                    bundleName = name;
                    bundleUrl = url;
                }
                else if (name.Equals("launcher-update.json", StringComparison.OrdinalIgnoreCase))
                {
                    manifestUrl = url;
                }
            }

            if (bundleUrl is null || manifestUrl is null || bundleName is null) return null;

            using var manifestResponse = await Http.GetAsync(manifestUrl, ct);
            manifestResponse.EnsureSuccessStatusCode();
            var manifestText = await manifestResponse.Content.ReadAsStringAsync(ct);
            using var manifest = JsonDocument.Parse(manifestText);
            var sha = manifest.RootElement.GetProperty("sha256").GetString() ?? "";
            var manifestVersion = manifest.RootElement.GetProperty("version").GetString() ?? "";

            if (!manifestVersion.Equals(version, StringComparison.OrdinalIgnoreCase) || sha.Length != 64)
                throw new InvalidDataException("Launcher update manifest does not match the release.");

            return new LauncherUpdateInfo(version, bundleUrl, sha, bundleName, releaseUrl);
        }

        return null;
    }

    public static async Task<PreparedLauncherUpdate> DownloadAndStageAsync(
        LauncherUpdateInfo update,
        Action<LauncherUpdateProgress> progress,
        CancellationToken ct)
    {
        var installRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var tempRoot = Path.Combine(Path.GetTempPath(), "CodaLauncherUpdate", Guid.NewGuid().ToString("N"));
        var zip = Path.Combine(tempRoot, update.BundleName);
        var staging = Path.Combine(tempRoot, "staging");
        Directory.CreateDirectory(tempRoot);

        progress(new LauncherUpdateProgress(
            "download",
            $"Downloading CodaLauncher {update.Version}...",
            0));

        using (var response = await Http.GetAsync(update.BundleUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;

            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(zip);

            var buffer = new byte[128 * 1024];
            long copied = 0;
            var lastPercent = -1;

            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0) break;

                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                copied += read;

                if (total is > 0)
                {
                    var percent = Math.Clamp((int)(copied * 100L / total.Value), 0, 100);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress(new LauncherUpdateProgress(
                            "download",
                            $"Downloading CodaLauncher {update.Version}... {percent}%",
                            percent));
                    }
                }
            }
        }

        progress(new LauncherUpdateProgress(
            "verify",
            "Download complete. Verifying the official copy...",
            100));

        var actual = Sha256(zip);
        if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Launcher update failed SHA-256 verification. Expected {update.Sha256}, got {actual}.");

        progress(new LauncherUpdateProgress(
            "stage",
            "Official copy verified. Staging the update...",
            100));

        ZipFile.ExtractToDirectory(zip, staging, true);
        var stagedExe = Path.Combine(staging, "CodaLauncher.exe");
        if (!File.Exists(stagedExe))
            throw new InvalidDataException("Staged launcher update does not contain CodaLauncher.exe.");

        progress(new LauncherUpdateProgress(
            "ready",
            "Update ready. Reboot CodaLauncher when you're ready.",
            100));

        return new PreparedLauncherUpdate(update.Version, staging, installRoot);
    }

    public static void StartApplyAndRestart(PreparedLauncherUpdate update)
    {
        var stagedExe = Path.Combine(update.StagingDirectory, "CodaLauncher.exe");
        if (!File.Exists(stagedExe))
            throw new InvalidDataException("Staged launcher update is no longer available.");

        var info = new ProcessStartInfo(stagedExe)
        {
            WorkingDirectory = update.StagingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("--apply-update");
        info.ArgumentList.Add(Environment.ProcessId.ToString());
        info.ArgumentList.Add(update.StagingDirectory);
        info.ArgumentList.Add(update.InstallRoot);

        var updaterProcess = Process.Start(info);
        if (updaterProcess is null)
            throw new InvalidOperationException("Could not start staged CodaLauncher updater.");
    }

    public static bool TryRunApplyMode(string[] args)
    {
        if (args.Length < 4 || !args[0].Equals("--apply-update", StringComparison.Ordinal))
            return false;

        if (!int.TryParse(args[1], out var parentPid))
            return true;

        var staging = Path.GetFullPath(args[2]);
        var target = Path.GetFullPath(args[3]);

        try
        {
            try
            {
                using var parent = Process.GetProcessById(parentPid);
                parent.WaitForExit(60_000);
            }
            catch { }

            Directory.CreateDirectory(target);
            foreach (var source in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(staging, source);
                var destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                if (destination.Equals(
                    Path.Combine(target, Path.GetFileName(Environment.ProcessPath ?? "CodaLauncher.exe")),
                    StringComparison.OrdinalIgnoreCase))
                {
                    // The updater itself is running from staging, not target, so replacement is safe.
                }

                File.Copy(source, destination, true);
            }

            var installedExe = Path.Combine(target, "CodaLauncher.exe");
            Process.Start(new ProcessStartInfo(installedExe)
            {
                WorkingDirectory = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "CodaLauncher-update-error.txt"),
                    ex.ToString());
            }
            catch { }
        }

        return true;
    }

    private static string Sha256(string file)
    {
        using var sha = SHA256.Create();
        using var input = File.OpenRead(file);
        return Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant();
    }

    private static int CompareVersions(string left, string right)
    {
        static int[] Parts(string raw)
        {
            var core = raw.Split('-', 2)[0];
            return core.Split('.')
                .Select(x => int.TryParse(x, out var n) ? n : 0)
                .Concat([0, 0, 0])
                .Take(3)
                .ToArray();
        }

        var a = Parts(left);
        var b = Parts(right);
        for (var i = 0; i < 3; i++)
        {
            var cmp = a[i].CompareTo(b[i]);
            if (cmp != 0) return cmp;
        }
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CodaLauncher-SelfUpdater/0.2");
        return client;
    }
}

internal sealed class LauncherService
{
    private readonly LogBuffer _logs;
    private readonly Action<string> _sendLine;
    private string? _redactedToken;

    public event Action<int>? SessionStarted;
    public event Action<int, int>? SessionExited;

    public LauncherService(LogBuffer logs, Action<string> sendLine)
    {
        _logs = logs;
        _sendLine = sendLine;
    }

    public int Launch(string loaderDirectory, GameIdentity identity, string? isolatedGameRoot = null)
    {
        if (string.IsNullOrWhiteSpace(identity.Uuid) || string.IsNullOrWhiteSpace(identity.PlayerName) ||
            (!identity.Offline && (string.IsNullOrWhiteSpace(identity.AccessToken) || identity.AccessToken == "0"))
            || (identity.LocalOnly && (!identity.Offline || identity.AccessToken != "0" || identity.ClientId != "")))
            throw new InvalidOperationException("A verified Minecraft identity is required.");
        var jar = Path.Combine(loaderDirectory, "CodaLoader.jar");
        if (!File.Exists(jar)) throw new FileNotFoundException("CodaLoader.jar was not found.", jar);

        var info = new ProcessStartInfo("java")
        {
            WorkingDirectory = loaderDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-jar");
        info.ArgumentList.Add(jar);
        info.ArgumentList.Add("--root");
        info.ArgumentList.Add(isolatedGameRoot ?? AppPaths.MinecraftRoot);
        info.ArgumentList.Add("--base-pack");
        info.ArgumentList.Add(AppPaths.CmlBaseResourcesRoot);
        _redactedToken = identity.Offline ? null : identity.AccessToken;
        info.Environment["CODA_PLAYER_NAME"] = identity.PlayerName;
        info.Environment["CODA_PLAYER_UUID"] = identity.Uuid;
        info.Environment["CODA_ACCESS_TOKEN"] = identity.AccessToken;
        info.Environment["CODA_PLAY_MODE"] = identity.LocalOnly ? "local" : identity.Offline ? "offline" : "online";
        info.Environment["CODA_AUTH_CLIENT_ID"] = identity.ClientId;
        // Older CodaLoader (0.0.24) already supports only local CodaPlayer.
        // Permit it only for explicit Local Test Mode, never as a stand-in for
        // a verified Microsoft account. New loaders validate the explicit mode.
        using (var jarArchive = ZipFile.OpenRead(jar))
            if (jarArchive.GetEntry("dev/howlingwhispers/codaloader/bootstrap/LaunchIdentity.class") is null)
            {
                if (!identity.LocalOnly)
                    throw new InvalidOperationException("This CodaLoader does not support verified accounts. Update it before playing.");
                Forward("Using legacy local-only CodaPlayer support in the installed loader.", false);
            }
        info.Environment["CODA_NO_PAUSE"] = "1";
        info.Environment["CODA_LAUNCHED_BY"] = "CodaLauncher";

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Forward(e.Data, false);
        process.ErrorDataReceived += (_, e) => Forward(e.Data, true);
        process.Exited += (_, _) =>
        {
            var exitCode = -1;
            try { exitCode = process.ExitCode; } catch { }

            Forward($"CodaLoader process {process.Id} exited with code {exitCode}.", false);
            SessionExited?.Invoke(process.Id, exitCode);
            process.Dispose();
        };

        if (!process.Start()) throw new InvalidOperationException("Could not start CodaLoader. Minecraft requires Java 25 or newer on PATH.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        Forward($"CodaLoader started from {loaderDirectory}.", false);
        SessionStarted?.Invoke(process.Id);
        return process.Id;
    }

    private void Forward(string? data, bool error)
    {
        if (string.IsNullOrWhiteSpace(data)) return;
        if (!string.IsNullOrEmpty(_redactedToken)) data = data.Replace(_redactedToken, "[redacted]", StringComparison.Ordinal);
        var line = _logs.Add((error ? "[CodaLoader:err] " : "[CodaLoader] ") + data);
        _sendLine(line);
    }
}

