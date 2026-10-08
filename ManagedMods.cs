using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// The active GAME profile's mods folder is authoritative. Loader JARs contain
/// a signed/verified distribution seed; legacy loader/run/mods is migration
/// input only, never a second scanning or update location.
/// </summary>
internal static class ManagedMods
{
    private const string FileName = "hw-essentials.jar";
    private const string EmbeddedName = "codaloader/mods/hw-essentials.jar";

    public static bool IsCurrent(string loaderRoot, string gameRoot)
    {
        var target = Path.Combine(gameRoot, "mods", FileName);
        if (!File.Exists(target)) return false;
        try { return Hash(target) == Hash(BundledEssentials(loaderRoot)); }
        catch (IOException) { return false; }
        catch (InvalidDataException) { return false; }
    }

    /// <summary>
    /// Copy legacy third-party JARs to the active profile without deleting,
    /// modifying, or silently overwriting either source or destination.
    /// The bundled HW Essentials is installed separately with ownership checks.
    /// </summary>
    public static void MigrateLegacy(string loaderRoot, string gameRoot, Action<string> progress)
    {
        var legacy = Path.Combine(loaderRoot, "run", "mods");
        if (!Directory.Exists(legacy)) return;
        var destination = Path.Combine(gameRoot, "mods");
        if (Path.GetFullPath(legacy).Equals(Path.GetFullPath(destination),
                StringComparison.OrdinalIgnoreCase)) return;
        // Refuse reparse points: they can redirect outside the managed profile.
        if ((File.GetAttributes(legacy) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Legacy mods directory is a link. Review it manually: " + legacy);
        foreach (var file in Directory.EnumerateFiles(legacy, "*.jar", SearchOption.TopDirectoryOnly))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                progress("Skipped linked legacy mod: " + Path.GetFileName(file));
                continue;
            }
            var name = Path.GetFileName(file);
            if (name.Equals(FileName, StringComparison.OrdinalIgnoreCase))
            {
                progress("Legacy HW Essentials seed retained in place; the active profile receives the verified bundled version.");
                continue;
            }
            Directory.CreateDirectory(destination);
            var target = Path.Combine(destination, name);
            if (File.Exists(target))
            {
                progress(Hash(file) == Hash(target)
                    ? "Legacy mod already present: " + name
                    : "MOD CONFLICT: " + name + " differs between folders; both originals preserved. Review manually.");
                continue;
            }
            // Stage a verified copy, then commit with no-clobber semantics.
            // Only our own temporary file is removed if a copy fails.
            string staged = Path.Combine(destination, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(file, staged);
                if (Hash(staged) != Hash(file))
                    throw new IOException("Migrated mod checksum differs: " + name);
                File.Move(staged, target, overwrite: false);
                progress("Copied legacy mod to active Minecraft/mods: " + name + " (old copy preserved).");
            }
            catch (IOException) when (File.Exists(target))
            {
                progress("MOD CONFLICT: " + name + " appeared during migration; existing file preserved.");
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
        progress("Legacy loader/run/mods was not deleted. You may review it after confirming the new profile works.");
    }

    /// <summary>
    /// Archive the entire legacy folder after a successful active-profile
    /// install. No contents are deleted, even non-JAR user files and conflicts.
    /// This removes the misleading loader/run/mods location from active view.
    /// </summary>
    public static void ArchiveLegacy(string loaderRoot, Action<string> progress)
    {
        string legacy = Path.Combine(loaderRoot, "run", "mods");
        if (!Directory.Exists(legacy)) return;
        // Older bundles relied on this directory as their ONLY source of
        // Essentials. Do not archive it until a self-contained loader exists.
        string loaderJar = Path.Combine(loaderRoot, "CodaLoader.jar");
        if (!File.Exists(loaderJar))
        {
            progress("Legacy mods retained: loader JAR is missing.");
            return;
        }
        try
        {
            using var zip = ZipFile.OpenRead(loaderJar);
            if (zip.GetEntry(EmbeddedName) == null)
            {
                progress("Legacy mods retained: this loader still relies on the old bundled seed.");
                return;
            }
        }
        catch (InvalidDataException)
        {
            progress("Legacy mods retained: loader JAR could not be inspected.");
            return;
        }
        string backup = Path.Combine(Path.GetDirectoryName(loaderRoot)
            ?? throw new IOException("Unknown loader parent directory"),
            "legacy-loader-mods-backup",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-"
                + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        Directory.Move(legacy, backup);
        progress("Old loader/run/mods archived without deleting user files: " + backup);
    }

    public static void Install(string loaderRoot, string gameRoot, Action<string> progress)
    {
        MigrateLegacy(loaderRoot, gameRoot, progress);
        var bytes = BundledEssentials(loaderRoot);
        var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var mods = Path.Combine(gameRoot, "mods");
        var target = Path.Combine(mods, FileName);
        // Same ownership marker used by H.O.W.L.'s in-game bundled installer.
        var marker = Path.Combine(gameRoot, "config", "codaloader-managed", "hw-essentials.sha256");
        var actual = File.Exists(target) ? Hash(target) : null;
        if (actual != null && actual != expected
            && (!File.Exists(marker) || !File.ReadAllText(marker).Trim()
                .Equals(actual, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("HW Essentials in the active mods folder was changed manually. Both versions were preserved. Move the modified file aside before updating.");

        Directory.CreateDirectory(mods);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        if (actual != expected)
        {
            progress(actual == null ? "Installing HW Essentials in active Minecraft/mods..."
                : "Updating owned HW Essentials in active Minecraft/mods...");
            var staging = Path.Combine(mods, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(staging, bytes);
                if (Hash(staging) != expected) throw new IOException("HW Essentials staging verification failed.");
                File.Move(staging, target, true);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
        File.WriteAllText(marker, expected);
        progress("HW Essentials is ready in the active Minecraft mods folder.");
    }

    private static byte[] BundledEssentials(string loaderRoot)
    {
        var jar = Path.Combine(loaderRoot, "CodaLoader.jar");
        if (File.Exists(jar))
        {
            using var zip = ZipFile.OpenRead(jar);
            var entry = zip.GetEntry(EmbeddedName);
            if (entry != null)
            {
                using var data = entry.Open();
                using var memory = new MemoryStream();
                data.CopyTo(memory);
                return memory.ToArray();
            }
        }
        // Upgrade compatibility for already installed older loader bundles.
        var legacy = Path.Combine(loaderRoot, "run", "mods", FileName);
        if (File.Exists(legacy)) return File.ReadAllBytes(legacy);
        throw new IOException("H.O.W.L. is missing bundled HW Essentials. Repair the loader installation.");
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }

    private static string Hash(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
