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

    /// <summary>File presence and checksum only, not proof Minecraft enabled terrain.</summary>
    public static bool QuietInstalled
    {
        get
        {
            try
            {
                string worldgen = Path.Combine(GameRoot, "config", "codaloader", "worldgen");
                string quiet = Path.Combine(worldgen, ActiveQuietPack);
                string marker = quiet + ".sha256";
                return File.Exists(quiet) && File.Exists(marker)
                    && HashFile(quiet).Equals(File.ReadAllText(marker).Trim(),
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    /// <summary>Required Nightly runtime only. Optional mods and worldgen are independent.</summary>
    private static string LoaderHashMarker => Path.Combine(LoaderRoot, ".nightly-loader-sha256");
    public static bool Installed
    {
        get
        {
            try
            {
                string loader = Path.Combine(LoaderRoot, "CodaLoader.jar");
                return File.Exists(loader) && File.Exists(LoaderHashMarker)
                    && File.Exists(Path.Combine(LoaderRoot, ".nightly-tag"))
                    && HashFile(loader).Equals(File.ReadAllText(LoaderHashMarker).Trim(),
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    public static bool BuildCraftInstalled => IsManagedBuildCraft();
    private static string ModFolder => Path.Combine(GameRoot, "mods");
    private static string OwnedBuildCraftHash => Path.Combine(ModFolder, ".howl-buildcraft-managed.sha256");
    private static string OwnedBuildCraftTag => Path.Combine(ModFolder, ".howl-buildcraft-tag");
    private static string BuildCraftJar => Path.Combine(ModFolder, ModJar);

    private static bool IsManagedBuildCraft()
    {
        try
        {
            return File.Exists(BuildCraftJar) && File.Exists(OwnedBuildCraftHash)
                && HashFile(BuildCraftJar).Equals(File.ReadAllText(OwnedBuildCraftHash).Trim(),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool HasCurrentBuildCraft(string tag) =>
        IsManagedBuildCraft() && File.Exists(OwnedBuildCraftTag)
        && File.ReadAllText(OwnedBuildCraftTag).Trim() == tag;

    /// <summary>
    /// Removes ONLY the managed optional BuildCraft mod, not its loader, world
    /// saves, resources, Quiet Underground settings or any user-edited JAR.
    /// </summary>
    public static void UninstallBuildCraft(Action<string> report)
    {
        if (!File.Exists(BuildCraftJar))
        {
            report("BuildCraft is already absent. No files changed.");
            return;
        }
        if (!IsManagedBuildCraft())
            throw new IOException("BuildCraft has no matching managed SHA-256; preserving the user-owned JAR.");
        File.Delete(BuildCraftJar);
        if (File.Exists(OwnedBuildCraftHash)) File.Delete(OwnedBuildCraftHash);
        if (File.Exists(OwnedBuildCraftTag)) File.Delete(OwnedBuildCraftTag);
        report("Optional BuildCraft uninstalled. H.O.W.L., saves and resource packs untouched.");
    }

    public async Task<string> InstallLatestAsync(Action<string> report, CancellationToken cancellation,
        bool installBuildCraft = false, bool installQuiet = false)
    {
        ArgumentNullException.ThrowIfNull(report);
        try
        {
            return await InstallOnlineAsync(report, cancellation, installBuildCraft, installQuiet);
        }
        catch (Exception error) when (error is HttpRequestException
                || (error is OperationCanceledException && !cancellation.IsCancellationRequested))
        {
            if (installBuildCraft || installQuiet)
                throw new IOException("Cannot check/download optional mod updates while GitHub is unreachable. "
                    + "Your installed mods and saves were preserved.", error);
            if (!Installed)
                throw new IOException("GitHub is unreachable and no checksum-verified H.O.W.L. Nightly runtime is available. "
                    + "Check your connection, then retry Play. Your worlds and mods were not deleted.", error);
            var installedTag = File.ReadAllText(Path.Combine(LoaderRoot, ".nightly-tag")).Trim();
            report("GitHub update check unavailable (" + error.GetType().Name + "). " +
                "Using previously installed BuildCraft " + installedTag +
                " (optional mods were not changed). New updates are pending.");
            return LoaderRoot;
        }
    }

    private async Task<string> InstallOnlineAsync(Action<string> report, CancellationToken cancellation,
        bool installBuildCraft, bool installQuiet)
    {
        var release = await GetReleaseAsync(cancellation);
        if (Installed && File.ReadAllText(Path.Combine(LoaderRoot, ".nightly-tag")).Trim() == release.Tag
            && (!installBuildCraft || HasCurrentBuildCraft(release.Tag))
            && (!installQuiet || QuietInstalled))
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
            if (!File.Exists(loaderJar))
                throw new InvalidDataException("Nightly bundle does not contain the required H.O.W.L. runtime.");
            if (installBuildCraft)
            {
                if (!File.Exists(bundledMod))
                    throw new InvalidDataException("BuildCraft add-on is missing from this Nightly release.");
                using var jar = ZipFile.OpenRead(bundledMod);
                if (jar.GetEntry("coda.mod.json") is null
                    || jar.GetEntry("dev/howlingwhispers/buildcraft/BuildCraftGlassPipeDemo.class") is null)
                    throw new InvalidDataException("BuildCraft add-on is missing its mod entrypoint.");
            }

            // Quiet Underground is a world datapack, NOT a mod JAR. Nightly
            // supplies it as a verified new-world creation preset.
            string bundledQuiet = Path.Combine(staging, "payload", QuietPack);
            string bundledQuietHash = bundledQuiet + ".sha256";
            string quietHash = "";
            if (installQuiet)
            {
                if (!File.Exists(bundledQuiet) || !File.Exists(bundledQuietHash))
                    throw new InvalidDataException("This Nightly does not include the optional Quiet Underground pack.");
                string quietHashText = File.ReadAllText(bundledQuietHash);
                quietHash = quietHashText.Split((char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (quietHash.Length != 64 || !quietHash.All(Uri.IsHexDigit)
                    || !HashFile(bundledQuiet).Equals(quietHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Quiet Underground datapack checksum failed.");
            }

            // Required runtime updates never touch installed optional mods.
            // Manual INSTALL/UPDATE validates ownership before replacing a JAR.
            string modFolder = ModFolder;
            string targetMod = BuildCraftJar;
            string ownerMarker = OwnedBuildCraftHash;
            string newHash = "";
            if (installBuildCraft)
            {
                newHash = HashFile(bundledMod);
                if (File.Exists(targetMod))
                {
                    string existingHash = HashFile(targetMod);
                    if (existingHash != newHash &&
                        (!File.Exists(ownerMarker) || !File.ReadAllText(ownerMarker).Trim()
                            .Equals(existingHash, StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("BuildCraft JAR was edited. Your copy is preserved.");
                }
            }

            string worldgen = Path.Combine(GameRoot, "config", "codaloader", "worldgen");
            string targetQuiet = Path.Combine(worldgen, ActiveQuietPack);
            string quietOwner = targetQuiet + ".sha256";
            if (installQuiet && File.Exists(targetQuiet))
            {
                string currentHash = HashFile(targetQuiet);
                if (!currentHash.Equals(quietHash, StringComparison.OrdinalIgnoreCase)
                    && (!File.Exists(quietOwner)
                        || !File.ReadAllText(quietOwner).Trim().Equals(currentHash,
                            StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("Quiet Underground was edited; your copy was preserved.");
            }

            Directory.CreateDirectory(LoaderRoot);
            Directory.CreateDirectory(modFolder);
            Directory.CreateDirectory(worldgen);

            // Preserve legacy ownership identity before advancing the loader
            // tag. Older BuildCraft releases stored only the loader tag.
            if (File.Exists(BuildCraftJar) && IsManagedBuildCraft()
                && !File.Exists(OwnedBuildCraftTag)
                && File.Exists(Path.Combine(LoaderRoot, ".nightly-tag")))
            {
                File.WriteAllText(OwnedBuildCraftTag,
                    File.ReadAllText(Path.Combine(LoaderRoot, ".nightly-tag")).Trim());
            }

            string jarTemp = Path.Combine(LoaderRoot, "." + Guid.NewGuid().ToString("N") + ".tmp");
            string modTemp = Path.Combine(modFolder, "." + Guid.NewGuid().ToString("N") + ".tmp");
            string quietTemp = Path.Combine(worldgen, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(loaderJar, jarTemp);
                if (HashFile(jarTemp) != HashFile(loaderJar))
                    throw new IOException("Nightly loader verification failed.");
                if (installBuildCraft)
                {
                    File.Copy(bundledMod, modTemp);
                    if (HashFile(modTemp) != newHash)
                        throw new IOException("BuildCraft staging verification failed.");
                }
                if (installQuiet)
                {
                    File.Copy(bundledQuiet, quietTemp);
                    if (!HashFile(quietTemp).Equals(quietHash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Quiet Underground staging verification failed.");
                }

                // Back up the previous owned runtime, never player worlds.
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
                File.WriteAllText(LoaderHashMarker, HashFile(oldLoader));
                if (installBuildCraft)
                {
                    File.Move(modTemp, targetMod, overwrite: true);
                    File.WriteAllText(ownerMarker, newHash);
                    File.WriteAllText(OwnedBuildCraftTag, release.Tag);
                }
                if (installQuiet)
                {
                    File.Move(quietTemp, targetQuiet, overwrite: true);
                    File.WriteAllText(quietOwner, quietHash);
                }
                File.WriteAllText(Path.Combine(LoaderRoot, ".nightly-tag"), release.Tag);
                report("Required H.O.W.L. Nightly " + release.Tag +
                    " installed. Optional mods " + (installBuildCraft ? "BuildCraft installed." : "unchanged.") +
                    (installQuiet ? " Quiet Underground installed." : ""));
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
        var selected = await ResolveReleaseAsync(Http, ct);
        if (selected is null)
            throw new InvalidOperationException(
                "No valid public BuildCraft Nightly release is published yet. Stable remains unchanged.");
        return new NightlyRelease(selected.Tag, selected.PackageUrl, selected.ChecksumUrl);
    }

    internal static async Task<NightlyReleaseSelector.Release?> ResolveReleaseAsync(
        HttpClient client, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(ReleasesApi, ct);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return NightlyReleaseSelector.SelectNewest(document.RootElement, TagPrefix, Package, Checksum);
        }
        catch (HttpRequestException apiError) when (apiError.StatusCode is System.Net.HttpStatusCode.Forbidden
                or System.Net.HttpStatusCode.TooManyRequests)
        {
            try
            {
                using var response = await client.GetAsync(NightlyAtomReleaseReader.FeedUrl, ct);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 512 * 1024)
                    throw new InvalidDataException("GitHub release feed exceeds size limit.");
                string xml = await response.Content.ReadAsStringAsync(ct);
                if (xml.Length > 512 * 1024)
                    throw new InvalidDataException("GitHub release feed exceeds size limit.");
                var selected = NightlyAtomReleaseReader.SelectNewest(xml, TagPrefix, Package, Checksum);
                if (selected is not null) return selected;
            }
            catch (Exception fallbackError) when (fallbackError is HttpRequestException
                    or InvalidDataException or System.Xml.XmlException)
            {
                // Retain the original API error so a previously verified
                // Nightly installation can be reused safely.
            }
            throw;
        }
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
