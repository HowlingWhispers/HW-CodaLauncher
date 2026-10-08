using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HowlingWhispers.CodaLauncher;

if (args.Contains("--live-release", StringComparer.Ordinal))
{
    string scratch = Path.Combine(Path.GetTempPath(), "codawolf-live-smoke-" + Guid.NewGuid().ToString("N"));
    try
    {
        using var installerHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        installerHttp.DefaultRequestHeaders.UserAgent.ParseAdd("CodaLauncher-Release-Smoke/0.1");
        var installer = new CodaWolfNightlyInstaller(installerHttp, scratch);
        string found = await installer.InstallLatestAsync(Console.WriteLine, CancellationToken.None);
        if (!found.StartsWith(CodaWolfNightlyInstaller.TagPrefix, StringComparison.Ordinal)
            || !installer.HasManagedInstall()
            || !File.Exists(Path.Combine(scratch, "mods", CodaWolfNightlyInstaller.ModJar)))
            throw new Exception("The published GitHub release was not discovered or installed.");
        Console.WriteLine("PASS: live GitHub release was automatically discovered, downloaded and installed: " + found);
    }
    finally
    {
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }
    return;
}

int count = 0;
void Check(bool expected, string why)
{
    count++;
    if (!expected) throw new Exception(why);
}
async Task ExpectFailure(Func<Task> action, string message)
{
    bool failed = false;
    try { await action(); }
    catch (IOException) { failed = true; }
    catch (InvalidDataException) { failed = true; }
    Check(failed, message);
}

byte[] MakeMod(string id = "coda_wolf")
{
    using var buffer = new MemoryStream();
    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
        var metadata = zip.CreateEntry("coda.mod.json");
        using (var writer = new StreamWriter(metadata.Open()))
            writer.Write(JsonSerializer.Serialize(new
            {
                schema = 1,
                id,
                name = "Coda Wolf Companion",
                version = "0.1.0-dev",
                minecraft = "26.4-snapshot-3",
                entrypoint = "dev.howlingwhispers.codawolf.CodaWolfMod",
                depends = Array.Empty<string>()
            }));
        using var cls = zip.CreateEntry("dev/howlingwhispers/codawolf/CodaWolfMod.class").Open();
        cls.Write([1, 2, 3]);
    }
    return buffer.ToArray();
}

string tag = "nightly-codawolf-20261008-test123456";
string older = "nightly-codawolf-20261007-older12345";
string baseUrl = "https://github.com/HowlingWhispers/HW-Mods/releases/download/";
string jarName = CodaWolfNightlyInstaller.ModJar;
byte[] bytes = MakeMod();
string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

object Release(string releaseTag, string published, bool checksum = true) =>
    new
    {
        tag_name = releaseTag,
        published_at = published,
        prerelease = true,
        draft = false,
        assets = (checksum ? new[] { jarName, jarName + ".sha256" } : new[] { jarName })
            .Select(n => new { name = n, browser_download_url = baseUrl + releaseTag + "/" + n })
            .ToArray()
    };

var releases = JsonSerializer.Serialize(new object[]
{
    Release(older, "2026-10-07T08:00:00Z"),
    Release("nightly-buildcraft-20261008-other", "2026-10-08T09:00:00Z"),
    Release(tag, "2026-10-08T08:00:00Z")
});
using (var parsed = JsonDocument.Parse(releases))
    Check(NightlyReleaseSelector.SelectNewest(parsed.RootElement,
        CodaWolfNightlyInstaller.TagPrefix, jarName, jarName + ".sha256")?.Tag == tag,
        "Only newest Coda Wolf releases selected, not BuildCraft.");

bool offline = false, timeout = false, badChecksum = false;
using var handler = new FakeHandler((url) =>
{
    if (offline) throw new HttpRequestException("Simulated api.github.com:443 timeout");
    if (timeout) throw new TaskCanceledException("Simulated GitHub request timeout");
    if (url.Host == "api.github.com") return Encoding.UTF8.GetBytes(releases);
    if (url.AbsolutePath.EndsWith(".sha256"))
        return Encoding.UTF8.GetBytes((badChecksum ? new string('0', 64) : sha) + "  " + jarName + "\n");
    if (url.AbsolutePath.EndsWith(".jar")) return bytes;
    throw new InvalidOperationException("Unexpected remote " + url);
});
using var http = new HttpClient(handler);
var root = Path.Combine(Path.GetTempPath(), "test-codawolf-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var installer = new CodaWolfNightlyInstaller(http, root);
    var output = new List<string>();
    Check(await installer.InstallLatestAsync(output.Add, CancellationToken.None) == tag,
        "Latest published Coda Wolf installed.");
    string path = Path.Combine(root, "mods", jarName);
    Check(File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes),
        "Correct mod JAR copied into active Nightly mods.");
    Check(installer.HasManagedInstall(), "Installed file is managed and checksum verified.");
    Check(File.ReadAllText(Path.Combine(root, "mods", ".howl-codawolf-tag")) == tag,
        "Installed release tag recorded.");
    int downloads = handler.JarDownloads;
    await installer.InstallLatestAsync(output.Add, CancellationToken.None);
    Check(handler.JarDownloads == downloads, "Same release never re-downloads JAR.");

    offline = true;
    Check(await installer.InstallLatestAsync(output.Add, CancellationToken.None) == tag,
        "Installed checksum-verified Coda Wolf survives GitHub API outage.");
    Check(output.Any(m => m.Contains("Using checksum-verified local Coda Wolf")),
        "Offline fallback explains cached build and pending updates.");
    var fresh = new CodaWolfNightlyInstaller(http, Path.Combine(root, "fresh"));
    await ExpectFailure(async () => { await fresh.InstallLatestAsync(output.Add, CancellationToken.None); },
        "Fresh profile without verified Coda Wolf refuses offline installation.");
    timeout = true; offline = false;
    Check(await installer.InstallLatestAsync(output.Add, CancellationToken.None) == tag,
        "Installed verified Coda Wolf survives a GitHub request timeout.");
    timeout = false;
    badChecksum = true;
    await ExpectFailure(async () => { await installer.InstallLatestAsync(output.Add, CancellationToken.None); },
        "Corrupt remote SHA-256 must never trigger offline fallback.");
    badChecksum = false;

    File.WriteAllBytes(path, MakeMod("tampered_id"));
    await ExpectFailure(async () => { await installer.InstallLatestAsync(output.Add, CancellationToken.None); },
        "Manually changed Coda Wolf JAR is not overwritten.");
    Check(File.ReadAllBytes(path).SequenceEqual(MakeMod("tampered_id")),
        "User modification preserved.");
    offline = true;
    await ExpectFailure(async () => { await installer.InstallLatestAsync(output.Add, CancellationToken.None); },
        "Tampered local Coda Wolf must never count as verified offline fallback.");
    offline = false;

    File.Delete(path);
    File.Delete(Path.Combine(root, "mods", ".howl-codawolf-managed.sha256"));
    string duplicate = Path.Combine(root, "mods", "old-wolf.jar");
    File.WriteAllBytes(duplicate, MakeMod());
    await ExpectFailure(async () => { await installer.InstallLatestAsync(output.Add, CancellationToken.None); },
        "Same mod ID in another JAR filename blocks duplicate.");
    Check(!File.Exists(path) && File.Exists(duplicate), "Existing duplicate untouched.");
}
finally { Directory.Delete(root, recursive: true); }

Console.WriteLine("PASS: " + count + " Coda Wolf GitHub discovery, SHA-256, install and duplicate-protection assertions.");

sealed class FakeHandler(Func<Uri, byte[]> route) : HttpMessageHandler
{
    public int JarDownloads { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing test URL");
        if (uri.AbsolutePath.EndsWith(".jar")) JarDownloads++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(route(uri))
        });
    }
}
