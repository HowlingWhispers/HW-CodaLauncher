using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace HowlingWhispers.CodaLauncher;

internal static class AppPaths
{
    public static string InstallRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ".howlingshispers");

    public static string MinecraftRoot => Path.Combine(InstallRoot, "minecraft");
    public static string LoaderRoot => Path.Combine(InstallRoot, "loader");
    public static string BasePackRoot => Path.Combine(InstallRoot, "cml-base");
    public static string LogsRoot => Path.Combine(InstallRoot, "logs");
    public static string BasePackMarker => Path.Combine(BasePackRoot, ".installed-version");

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
            while (_lines.Count > 500) _lines.Dequeue();
        }
        return line;
    }

    public string[] Snapshot()
    {
        lock (_gate) return _lines.ToArray();
    }
}

internal sealed class FeedService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public async Task<LauncherFeed> FetchAsync(string raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Offline("Launcher feed URL is not configured yet.");
        try
        {
            var endpoint = new Uri(new Uri(raw.TrimEnd('/') + "/"), "api/feed");
            using var response = await Http.GetAsync(endpoint, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var feed = await JsonSerializer.DeserializeAsync<LauncherFeed>(stream, Json, ct) ?? throw new InvalidDataException("Empty launcher feed.");
            feed.Online = true;
            return feed;
        }
        catch (Exception ex) { return Offline(ex.Message); }
    }

    private static LauncherFeed Offline(string reason) => new()
    {
        Online = false,
        Error = reason,
        News = [new NewsItem { Id = "offline", Date = DateTime.Now.ToString("yyyy-MM-dd"), Title = "CodaLauncher is running locally", Text = "Online news is unavailable, but PLAY, Mods, Settings and Logs still work." }]
    };
}

internal sealed class InstallService
{
    private const string ReleasesApi =
        "https://api.github.com/repos/HowlingWhispers/HW-CodaLoader/releases?per_page=20";
    private const string BasePackAssetName = "CML-BasePack-v1.zip";
    private const string BasePackVersion = "1";
    private const string BasePackSha256 =
        "13152d503929d55fd685dfaffbbd2b4df66a13619a907deff85097b10de66bf8";

    private static readonly HttpClient Http = CreateHttp();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public bool LoaderReady => LoaderLocator.IsReady(AppPaths.LoaderRoot);

    public bool BasePackReady(CmlBasePackInfo info)
    {
        if (!info.Required) return true;
        if (string.IsNullOrWhiteSpace(info.Version)) return false;
        if (!File.Exists(AppPaths.BasePackMarker)) return false;
        return string.Equals(
            File.ReadAllText(AppPaths.BasePackMarker).Trim(),
            info.Version,
            StringComparison.Ordinal);
    }

    public async Task InstallOrRepairAsync(
        LauncherFeed feed,
        Action<string> progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.InstallRoot);
        Directory.CreateDirectory(AppPaths.MinecraftRoot);
        Directory.CreateDirectory(AppPaths.LogsRoot);

        progress("Downloading current CodaLoader...");
        await InstallLoaderAsync(progress, ct);

        progress("Downloading mandatory CML base pack...");
        await InstallBasePackAsync(feed.BasePack, progress, ct);

        var bundledHello = Path.Combine(AppPaths.LoaderRoot, "run", "mods", "hello-coda.jar");
        var gameMods = Path.Combine(AppPaths.MinecraftRoot, "mods");
        Directory.CreateDirectory(gameMods);
        var helloTarget = Path.Combine(gameMods, "hello-coda.jar");
        if (File.Exists(bundledHello) && !File.Exists(helloTarget))
            File.Copy(bundledHello, helloTarget);

        progress("Install ready.");
    }

    private async Task InstallLoaderAsync(Action<string> progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(ReleasesApi, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        JsonElement? selected = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            selected = release;
            break;
        }
        if (selected is null)
            throw new InvalidOperationException("No CodaLoader release is available.");

        string? url = null;
        string? name = null;
        foreach (var asset in selected.Value.GetProperty("assets").EnumerateArray())
        {
            var candidate = asset.GetProperty("name").GetString() ?? "";
            if (!candidate.StartsWith("CodaLoader-v", StringComparison.OrdinalIgnoreCase)
                || !candidate.EndsWith("-win64.zip", StringComparison.OrdinalIgnoreCase))
                continue;
            name = candidate;
            url = asset.GetProperty("browser_download_url").GetString();
            break;
        }

        if (url is null || name is null)
            throw new InvalidOperationException("The newest CodaLoader release has no Windows bundle.");

        var tempRoot = Path.Combine(Path.GetTempPath(), "CodaLauncher", Guid.NewGuid().ToString("N"));
        var zip = Path.Combine(tempRoot, name);
        var staging = Path.Combine(tempRoot, "loader");
        Directory.CreateDirectory(tempRoot);

        try
        {
            await DownloadAsync(new Uri(url), zip, ct);
            ZipFile.ExtractToDirectory(zip, staging, true);

            progress("Installing CodaLoader...");
            ReplaceDirectory(staging, AppPaths.LoaderRoot);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private async Task InstallBasePackAsync(
        CmlBasePackInfo feedInfo,
        Action<string> progress,
        CancellationToken ct)
    {
        var version = string.IsNullOrWhiteSpace(feedInfo.Version) ? BasePackVersion : feedInfo.Version;
        var expectedSha = string.IsNullOrWhiteSpace(feedInfo.Sha256) ? BasePackSha256 : feedInfo.Sha256;

        Uri? uri = await FindReleaseAssetAsync(BasePackAssetName, ct);

        if (uri is null
            && CurrentFeedBase is not null
            && !string.IsNullOrWhiteSpace(feedInfo.Url))
        {
            uri = Uri.TryCreate(feedInfo.Url, UriKind.Absolute, out var absolute)
                ? absolute
                : new Uri(CurrentFeedBase, feedInfo.Url.TrimStart('/'));
            progress("CML base pack not attached to HW-CodaLoader release; trying launcher feed fallback...");
        }

        if (uri is null)
        {
            throw new InvalidOperationException(
                "CML-BasePack-v1.zip is not attached to any current HW-CodaLoader GitHub Release. "
                + "Attach that asset to HW-CodaLoader Releases before installing.");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "CodaLauncher", Guid.NewGuid().ToString("N"));
        var zip = Path.Combine(tempRoot, BasePackAssetName);
        var staging = Path.Combine(tempRoot, "base");
        Directory.CreateDirectory(tempRoot);

        try
        {
            await DownloadAsync(uri, zip, ct);

            var actual = Sha256(zip);
            if (!actual.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"CML base pack failed SHA-256 verification. Expected {expectedSha}, got {actual}.");

            ZipFile.ExtractToDirectory(zip, staging, true);
            if (!Directory.Exists(Path.Combine(staging, "branding"))
                || !Directory.Exists(Path.Combine(staging, "music", "default")))
                throw new InvalidDataException("CML base pack is missing branding or default music.");

            progress("Installing CML base pack...");
            ReplaceDirectory(staging, AppPaths.BasePackRoot);
            File.WriteAllText(AppPaths.BasePackMarker, version);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task<Uri?> FindReleaseAssetAsync(string assetName, CancellationToken ct)
    {
        using var response = await Http.GetAsync(ReleasesApi, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;

            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var candidate = asset.GetProperty("name").GetString() ?? "";
                if (!candidate.Equals(assetName, StringComparison.OrdinalIgnoreCase)) continue;

                var url = asset.GetProperty("browser_download_url").GetString();
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri;
            }
        }

        return null;
    }

    public Uri? CurrentFeedBase { get; set; }

    private static async Task DownloadAsync(Uri uri, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
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
        var backup = target + ".old";
        TryDeleteDirectory(backup);
        if (Directory.Exists(target)) Directory.Move(target, backup);
        try
        {
            Directory.Move(source, target);
            TryDeleteDirectory(backup);
        }
        catch
        {
            TryDeleteDirectory(target);
            if (Directory.Exists(backup)) Directory.Move(backup, target);
            throw;
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

    public static async Task StageAndRestartAsync(
        LauncherUpdateInfo update,
        Action<string> progress,
        CancellationToken ct)
    {
        var installRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var tempRoot = Path.Combine(Path.GetTempPath(), "CodaLauncherUpdate", Guid.NewGuid().ToString("N"));
        var zip = Path.Combine(tempRoot, update.BundleName);
        var staging = Path.Combine(tempRoot, "staging");
        Directory.CreateDirectory(tempRoot);

        progress($"Downloading CodaLauncher {update.Version}...");
        using (var response = await Http.GetAsync(update.BundleUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(zip);
            await input.CopyToAsync(output, ct);
        }

        var actual = Sha256(zip);
        if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Launcher update failed SHA-256 verification. Expected {update.Sha256}, got {actual}.");

        ZipFile.ExtractToDirectory(zip, staging, true);
        var stagedExe = Path.Combine(staging, "CodaLauncher.exe");
        if (!File.Exists(stagedExe))
            throw new InvalidDataException("Staged launcher update does not contain CodaLauncher.exe.");

        progress("Applying launcher update...");
        var info = new ProcessStartInfo(stagedExe)
        {
            WorkingDirectory = staging,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("--apply-update");
        info.ArgumentList.Add(Environment.ProcessId.ToString());
        info.ArgumentList.Add(staging);
        info.ArgumentList.Add(installRoot);
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

    public LauncherService(LogBuffer logs, Action<string> sendLine)
    {
        _logs = logs;
        _sendLine = sendLine;
    }

    public int Launch(string loaderDirectory)
    {
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
        info.ArgumentList.Add(AppPaths.MinecraftRoot);
        info.ArgumentList.Add("--base-pack");
        info.ArgumentList.Add(AppPaths.BasePackRoot);
        info.Environment["CODA_NO_PAUSE"] = "1";
        info.Environment["CODA_LAUNCHED_BY"] = "CodaLauncher";

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Forward(e.Data, false);
        process.ErrorDataReceived += (_, e) => Forward(e.Data, true);
        process.Exited += (_, _) => Forward($"CodaLoader process {process.Id} exited.", false);

        if (!process.Start()) throw new InvalidOperationException("Windows did not start CodaLoader.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        Forward($"CodaLoader started from {loaderDirectory}.", false);
        return process.Id;
    }

    private void Forward(string? data, bool error)
    {
        if (string.IsNullOrWhiteSpace(data)) return;
        var line = _logs.Add((error ? "[CodaLoader:err] " : "[CodaLoader] ") + data);
        _sendLine(line);
    }
}
