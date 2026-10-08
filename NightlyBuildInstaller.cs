using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Explicit opt-in BuildCraft nightly. Downloads a verified public prerelease
/// from HW-Mods and installs it OUTSIDE both the stable loader and world folder.
/// A separate test profile is compulsory: no stable-world migration.
/// </summary>
internal sealed class NightlyBuildInstaller
{
    private const string ReleasesApi = "https://api.github.com/repos/HowlingWhispers/HW-Mods/releases?per_page=50";
    private const string TagPrefix = "nightly-buildcraft-";
    private const string Package = "HOWL-BuildCraft-Singleplayer-Playtest.zip";
    private const string Checksum = Package + ".sha256";
    private const string ModJar = "buildcraft-cml-0.1.0-dev.jar";
    private const string QuietPack = "hw-quiet-underground-1.0.0.zip";
    private const string ActiveQuietPack = "hw-quiet-underground.zip";
    private const int MaxArchiveBytes = 64 * 1024 * 1024;
    private static readonly HttpClient Http = NewHttp();

    private sealed record NightlyRelease(string Tag, Uri PackageUrl, Uri ChecksumUrl);

    public static string LoaderRoot => Path.Combine(AppPaths.InstallRoot, "nightly", "loader");
    public static string GameRoot => Path.Combine(AppPaths.InstallRoot, "nightly", "minecraft");

    public static bool Installed
    {
        get
        {
            try
            {
                string modFile = Path.Combine(GameRoot, "mods", ModJar);
                string marker = Path.Combine(GameRoot, "mods", ".howl-buildcraft-managed.sha256");
                string worldgen = Path.Combine(GameRoot, "config", "codaloader", "worldgen");
                string quiet = Path.Combine(worldgen, ActiveQuietPack);
                string quietMarker = quiet + ".sha256";
                return File.Exists(quiet) && File.Exists(quietMarker)
                    && HashFile(quiet).Equals(File.ReadAllText(quietMarker).Trim()
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0],
                        StringComparison.OrdinalIgnoreCase)
                    && File.Exists(Path.Combine(LoaderRoot, "CodaLoader.jar"))
                    && File.Exists(Path.Combine(LoaderRoot, ".nightly-tag"))
                    && File.Exists(modFile) && File.Exists(marker)
                    && HashFile(modFile).Equals(File.ReadAllText(marker).Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    public async Task<string> InstallLatestAsync(Action<string> report, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(report);
        var release = await GetReleaseAsync(cancellation);
        if (Installed && File.ReadAllText(Path.Combine(LoaderRoot, ".nightly-tag")).Trim() == release.Tag)
        {
            report($"Nightly {release.Tag} is already installed in its isolated test profile.");
            return LoaderRoot;
        }

        // Stage code on the same filesystem as its destination so atomic
        // directory rename works on Windows and does not touch Stable.
        string working = Path.Combine(AppPaths.InstallRoot, "nightly", ".staging",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(working);
        string staging = Path.Combine(working, "loader");
        try
        {
            report($"Downloading experimental BuildCraft {release.Tag}...");
            byte[] checksumBytes = await DownloadAsync(release.ChecksumUrl, 4096, cancellation);
            string checksumText = System.Text.Encoding.UTF8.GetString(checksumBytes).Trim();
            string expected = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
                throw new InvalidDataException("Nightly checksum file has an invalid SHA-256.");

            byte[] package = await DownloadAsync(release.PackageUrl, MaxArchiveBytes, cancellation);
            string actual = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Nightly download failed SHA-256 verification. Stable installation unchanged.");

            Directory.CreateDirectory(staging);
            string nested = Path.Combine(working, Package);
            await File.WriteAllBytesAsync(nested, package, cancellation);
            ZipFile.ExtractToDirectory(nested, staging, overwriteFiles: false);

            string loaderJar = Path.Combine(staging, "CodaLoader.jar");
            // New nightlies provide a transient package payload. For migration,
            // older pre-refactor ZIPs may still carry run/mods, but that
            // directory is never installed under the active loader.
            string bundledMod = Path.Combine(staging, "payload", ModJar);
            if (!File.Exists(bundledMod))
                bundledMod = Path.Combine(staging, "run", "mods", ModJar);
            if (!File.Exists(loaderJar) || !File.Exists(bundledMod))
                throw new InvalidDataException("Nightly package does not contain the loader and BuildCraft test mod.");
            using (var jar = ZipFile.OpenRead(bundledMod))
                if (jar.GetEntry("coda.mod.json") is null
                    || jar.GetEntry("dev/howlingwhispers/buildcraft/BuildCraftGlassPipeDemo.class") is null)
                    throw new InvalidDataException("Downloaded BuildCraft nightly has no playable-test entrypoint.");

            // Quiet Underground is a world datapack, NOT a mod JAR. Nightly
            // supplies it as a verified new-world creation preset.
            string bundledQuiet = Path.Combine(staging, "payload", QuietPack);
            string bundledQuietHash = bundledQuiet + ".sha256";
            if (!File.Exists(bundledQuiet) || !File.Exists(bundledQuietHash))
                throw new InvalidDataException("This Nightly predates Quiet Underground world creation support.");
            string quietHashText = File.ReadAllText(bundledQuietHash);
            string quietHash = quietHashText.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (quietHash.Length != 64 || !quietHash.All(Uri.IsHexDigit)
                || !HashFile(bundledQuiet).Equals(quietHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Quiet Underground datapack checksum failed.");

            // First migrate any older nightly installation. Validate
            // ownership only AFTER migration, so a user-modified legacy
            // BuildCraft JAR cannot be silently replaced by the new nightly.
            ManagedMods.MigrateLegacy(LoaderRoot, GameRoot, report);
            // Only manage the explicitly owned nightly game mod; do not touch
            // stable mods, installed Minecraft or a user-modified nightly mod.
            string modFolder = Path.Combine(GameRoot, "mods");
            string targetMod = Path.Combine(modFolder, ModJar);
            string ownerMarker = Path.Combine(modFolder, ".howl-buildcraft-managed.sha256");
            string newHash = HashFile(bundledMod);
            if (File.Exists(targetMod))
            {
                string existingHash = HashFile(targetMod);
                if (existingHash != newHash &&
                    (!File.Exists(ownerMarker) || !File.ReadAllText(ownerMarker).Trim()
                        .Equals(existingHash, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("Nightly BuildCraft JAR was modified manually. Your file was preserved; move it aside before updating.");
            }

            // Refuse overwriting a user-edited Quiet Underground preset.
            string worldgen = Path.Combine(GameRoot, "config", "codaloader", "worldgen");
            string targetQuiet = Path.Combine(worldgen, ActiveQuietPack);
            string quietOwner = targetQuiet + ".sha256";
            if (File.Exists(targetQuiet))
            {
                string currentHash = HashFile(targetQuiet);
                if (!currentHash.Equals(quietHash, StringComparison.OrdinalIgnoreCase)
                    && (!File.Exists(quietOwner)
                        || !File.ReadAllText(quietOwner).Trim().Equals(currentHash,
                            StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("Quiet Underground pack was edited. Your original was preserved.");
            }

            Directory.CreateDirectory(LoaderRoot);
            Directory.CreateDirectory(modFolder);
            Directory.CreateDirectory(worldgen);

            // Nightly is a distinct game profile, NOT a second mod scanner
            // under its loader. Preserve and migrate older nightly run/mods.
            // Stage the two owned files without deleting the loader folder.
            // Older loader/run/mods, configuration and user files are retained.
            string jarTemp = Path.Combine(LoaderRoot, "." + Guid.NewGuid().ToString("N") + ".tmp");
            string modTemp = Path.Combine(modFolder, "." + Guid.NewGuid().ToString("N") + ".tmp");
            string quietTemp = Path.Combine(worldgen, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(loaderJar, jarTemp);
                if (HashFile(jarTemp) != HashFile(loaderJar))
                    throw new IOException("Nightly loader verification failed.");
                File.Copy(bundledMod, modTemp);
                if (HashFile(modTemp) != newHash)
                    throw new IOException("Nightly BuildCraft mod verification failed.");
                File.Copy(bundledQuiet, quietTemp);
                if (!HashFile(quietTemp).Equals(quietHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Nightly Quiet Underground staging verification failed.");

                // Keep a copy of the old owned loader binary rather than
                // destroying unknown files on each nightly update.
                string oldLoader = Path.Combine(LoaderRoot, "CodaLoader.jar");
                if (File.Exists(oldLoader))
                {
                    string backups = Path.Combine(LoaderRoot, "previous-loader-code");
                    Directory.CreateDirectory(backups);
                    File.Copy(oldLoader, Path.Combine(backups,
                        DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-"
                            + Guid.NewGuid().ToString("N") + ".jar"));
                }
                File.Move(jarTemp, oldLoader, overwrite: true);
                File.Move(modTemp, targetMod, overwrite: true);
                File.Move(quietTemp, targetQuiet, overwrite: true);
                File.WriteAllText(quietOwner, quietHash);
                File.WriteAllText(ownerMarker, newHash);
                File.WriteAllText(Path.Combine(LoaderRoot, ".nightly-tag"), release.Tag);
                ManagedMods.ArchiveLegacy(LoaderRoot, report);
                report($"Nightly {release.Tag} installed into one active game mods folder. Stable untouched.");
            }
            finally
            {
                if (File.Exists(jarTemp)) File.Delete(jarTemp);
                if (File.Exists(modTemp)) File.Delete(modTemp);
                if (File.Exists(quietTemp)) File.Delete(quietTemp);
            }

            return LoaderRoot;
        }
        finally
        {
            try { if (Directory.Exists(working)) Directory.Delete(working, recursive: true); }
            catch { /* A locked temp file will be cleaned up by the OS. */ }
        }
    }

    private static async Task<NightlyRelease> GetReleaseAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync(ReleasesApi, ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (!release.TryGetProperty("prerelease", out var prerelease) || !prerelease.GetBoolean()) continue;
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith(TagPrefix, StringComparison.Ordinal)) continue;
            Uri? zip = null;
            Uri? sha = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                string? link = asset.GetProperty("browser_download_url").GetString();
                if (!Uri.TryCreate(link, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
                    || !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (name == Package) zip = url;
                else if (name == Checksum) sha = url;
            }
            if (zip != null && sha != null) return new NightlyRelease(tag, zip, sha);
        }
        throw new InvalidOperationException("No verified public BuildCraft nightly release is published yet. Stable has not been modified.");
    }

    private static async Task<byte[]> DownloadAsync(Uri url, int max, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > max)
            throw new InvalidDataException("Nightly file exceeds safety size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            int read = await input.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (output.Length + read > max)
                throw new InvalidDataException("Nightly file exceeds safety size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string HashFile(string filename)
    {
        using var file = File.OpenRead(filename);
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }

    private static HttpClient NewHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CodaLauncher-Nightly/0.1");
        return client;
    }
}
