using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace HowlingWhispers.CodaLauncher;

internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HowlingWhispers", "CodaLauncher");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string WebViewData => Path.Combine(Root, "WebView2");
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
        Directory.CreateDirectory(AppPaths.Root);
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
        var mods = Path.Combine(loaderDirectory, "run", "mods");
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
        var bat = Path.Combine(loaderDirectory, "Launch-CodaLoader.bat");
        if (!File.Exists(bat)) throw new FileNotFoundException("Launch-CodaLoader.bat was not found.", bat);

        var info = new ProcessStartInfo("cmd.exe")
        {
            WorkingDirectory = loaderDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("/d");
        info.ArgumentList.Add("/c");
        info.ArgumentList.Add("call");
        info.ArgumentList.Add(bat);
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
