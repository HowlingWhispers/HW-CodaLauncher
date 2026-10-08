using System.IO;
using System.Security.Cryptography;

namespace HowlingWhispers.CodaLauncher;

// Called only after the downloaded resource archive passes its pinned SHA-256.
// Resource packs contain branding/music, not the loader's JAR and BAT files.
internal static class ResourcePackInstaller
{
    internal static void Install(string source, string target)
    {
        if (!Directory.Exists(Path.Combine(source, "branding"))
            || !Directory.Exists(Path.Combine(source, "music", "default")))
            throw new InvalidDataException("HOWL Base Resources is missing required branding or default music.");

        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
            throw new InvalidDataException("HOWL Base Resources contains no files.");

        Directory.CreateDirectory(target);
        // Keep previous versions outside the active pack. User-added files
        // absent from the incoming archive stay in the active directory.
        var backup = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!,
            "previous-resource-pack-code", DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
        foreach (var incoming in files)
        {
            var relative = Path.GetRelativePath(source, incoming);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                if (Hash(destination) == Hash(incoming)) continue;
                var saved = Path.Combine(backup, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Copy(destination, saved, overwrite: false);
            }
            var pending = Path.Combine(Path.GetDirectoryName(destination)!, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(incoming, pending, overwrite: false);
                if (Hash(pending) != Hash(incoming))
                    throw new IOException("Resource pack installation integrity check failed: " + relative);
                File.Move(pending, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(pending)) File.Delete(pending);
            }
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
