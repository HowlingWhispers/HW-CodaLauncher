using System.IO;
using System.Security.Cryptography;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Displays an installed Nightly release only when its JAR matches the local
/// managed SHA-256 marker written after a verified GitHub download.
/// Does not claim the release is newest or that Minecraft loaded the mod.
/// </summary>
internal static class NightlyModIdentity
{
    internal static ModInfo Attach(ModInfo info, string gameRoot)
    {
        if (!info.Valid || !IsNightlyRoot(gameRoot)) return info;

        string marker;
        string tagFile;
        string prefix;
        if (info.Id == "coda_wolf" &&
            info.FileName.Equals("coda-wolf-0.1.0-dev.jar", StringComparison.OrdinalIgnoreCase))
        {
            marker = Path.Combine(gameRoot, "mods", ".howl-codawolf-managed.sha256");
            tagFile = Path.Combine(gameRoot, "mods", ".howl-codawolf-tag");
            prefix = "nightly-codawolf-";
        }
        else if (info.Id == "buildcraft_cml" &&
            info.FileName.Equals("buildcraft-cml-0.1.0-dev.jar", StringComparison.OrdinalIgnoreCase))
        {
            marker = Path.Combine(gameRoot, "mods", ".howl-buildcraft-managed.sha256");
            // BuildCraft is optional and can stay at an older release while
            // the required H.O.W.L. runtime updates independently.
            tagFile = Path.Combine(gameRoot, "mods", ".howl-buildcraft-tag");
            if (!File.Exists(tagFile))
                tagFile = Path.Combine(Path.GetDirectoryName(gameRoot)!, "loader", ".nightly-tag");
            prefix = "nightly-buildcraft-";
        }
        else return info;

        try
        {
            if (!File.Exists(marker) || !File.Exists(tagFile))
                return info with { ReleaseStatus = "Untracked" };

            string savedHash = File.ReadAllText(marker).Trim();
            string savedTag = File.ReadAllText(tagFile).Trim();
            if (savedHash.Length != 64 || !savedHash.All(Uri.IsHexDigit)
                || !savedTag.StartsWith(prefix, StringComparison.Ordinal)
                || savedTag.Length > 130 || savedTag.Length <= prefix.Length
                || !savedTag.All(c => char.IsAsciiLetterOrDigit(c)
                    || c is '-' or '_' or '.'))
                return info with { ReleaseStatus = "Untracked" };

            string jar = Path.Combine(gameRoot, "mods", info.FileName);
            using var stream = File.OpenRead(jar);
            string realHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!savedHash.Equals(realHash, StringComparison.OrdinalIgnoreCase))
                return info with { ReleaseStatus = "Modified" };

            return info with { ReleaseTag = savedTag, ReleaseStatus = "Verified" };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return info with { ReleaseStatus = "Untracked" };
        }
    }

    private static bool IsNightlyRoot(string root)
    {
        // Never mistake Stable or an arbitrary folder with copied marker files
        // for the isolated Nightly installation.
        try
        {
            string path = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            return Path.GetFileName(path).Equals("minecraft", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(Path.GetDirectoryName(path)),
                    "nightly", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }
}
