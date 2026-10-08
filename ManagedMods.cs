using System.IO;
using System.Security.Cryptography;

namespace HowlingWhispers.CodaLauncher;

internal static class ManagedMods
{
    private const string FileName = "hw-essentials.jar";

    public static bool IsCurrent(string loaderRoot, string gameRoot)
    {
        var bundled = Path.Combine(loaderRoot, "run", "mods", FileName);
        var installed = Path.Combine(gameRoot, "mods", FileName);
        return File.Exists(bundled) && File.Exists(installed)
            && Hash(bundled) == Hash(installed);
    }

    public static void Install(string loaderRoot, string gameRoot, Action<string> progress)
    {
        var bundled = Path.Combine(loaderRoot, "run", "mods", FileName);
        if (!File.Exists(bundled))
            throw new IOException("The CodaLoader bundle is missing HW Essentials. Repair the loader installation.");

        var mods = Path.Combine(gameRoot, "mods");
        var target = Path.Combine(mods, FileName);
        // Same ownership marker used by CodaLoader, so either installer can update its own JAR.
        var marker = Path.Combine(gameRoot, "config", "codaloader-managed", "hw-essentials.sha256");
        var expected = Hash(bundled);
        var actual = File.Exists(target) ? Hash(target) : null;
        if (actual != null && actual != expected
            && (!File.Exists(marker) || File.ReadAllText(marker).Trim() != actual))
            throw new IOException("HW Essentials was installed or changed manually. Move minecraft/mods/hw-essentials.jar aside before updating; your homes and settings are preserved.");

        Directory.CreateDirectory(mods);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        if (actual != expected)
        {
            progress(actual == null ? "Installing HW Essentials in your Minecraft profile..."
                : "Updating HW Essentials in your Minecraft profile...");
            var staging = Path.Combine(mods, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(bundled, staging);
                if (Hash(staging) != expected) throw new IOException("HW Essentials copy verification failed.");
                File.Move(staging, target, true);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
        File.WriteAllText(marker, expected);
        progress("HW Essentials is ready. Homes and return trips filed.");
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
