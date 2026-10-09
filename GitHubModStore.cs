using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Installs an explicitly selected, validated optional JAR from the trusted
/// HowlingWhispers GitHub release catalog into the active Minecraft profile.
/// Tracks local ownership by SHA-256. Never alters untracked/modified mod JARs.
/// </summary>
internal sealed class GitHubModStore
{
    private const int MaximumJarBytes = 24 * 1024 * 1024;
    private static readonly HttpClient Http = NewClient();
    internal sealed record Receipt(string Slug, string ModId, string FileName,
        string Version, string Tag, string Hash);

    private static HttpClient NewClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CodaLauncher-HOWL-ModInstaller/1.0");
        return client;
    }

    private static string Slug(GitHubModRelease entry)
    {
        if (!entry.Id.StartsWith("github:", StringComparison.Ordinal)
            || entry.Id.Length > 70
            || !entry.Id[7..].All(c => char.IsAsciiLetterLower(c)
                || char.IsAsciiDigit(c) || c == '-'))
            throw new InvalidDataException("Invalid H.O.W.L. mod catalog key");
        return entry.Id[7..];
    }

    private static string ModsRoot(string gameRoot) => Path.Combine(gameRoot, "mods");
    private static string ReceiptPath(string gameRoot, string slug) =>
        Path.Combine(ModsRoot(gameRoot), ".howl-github-mods", slug + ".json");

    internal static Receipt? ReadReceipt(string gameRoot, GitHubModRelease entry)
    {
        try
        {
            string slug = Slug(entry);
            string path = ReceiptPath(gameRoot, slug);
            if (!File.Exists(path)) return null;
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path));
            if (receipt == null || receipt.Slug != slug
                || receipt.FileName != Path.GetFileName(receipt.FileName)
                || !receipt.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                || receipt.Hash.Length != 64 || !receipt.Hash.All(Uri.IsHexDigit))
                return null;
            return receipt;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
            or ArgumentException)
        {
            return null;
        }
    }

    internal static bool IsManaged(string gameRoot, GitHubModRelease entry)
    {
        var receipt = ReadReceipt(gameRoot, entry);
        return receipt != null && File.Exists(Path.Combine(ModsRoot(gameRoot), receipt.FileName))
            && Hash(Path.Combine(ModsRoot(gameRoot), receipt.FileName))
                .Equals(receipt.Hash, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsPresent(string gameRoot, GitHubModRelease entry,
        IReadOnlyList<ModInfo> scanned)
    {
        var receipt = ReadReceipt(gameRoot, entry);
        return receipt is not null && scanned.Any(m => m.FileName == receipt.FileName)
            || scanned.Any(m => m.FileName.Equals(entry.AssetName, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool HasUpdate(string gameRoot, GitHubModRelease entry) =>
        ReadReceipt(gameRoot, entry) is { } receipt &&
        (!receipt.Tag.Equals(entry.Tag, StringComparison.Ordinal)
         || !receipt.Version.Equals(entry.Version, StringComparison.Ordinal));

    internal async Task InstallAsync(GitHubModRelease entry, string gameRoot,
        Action<string> report, CancellationToken ct)
    {
        string slug = Slug(entry);
        if (!GitHubModCatalog.TrustedUrl(entry.DownloadUrl, entry.Repository,
                entry.Tag, entry.AssetName)
            || !entry.AssetName.EndsWith(".jar", StringComparison.Ordinal)
            || entry.AssetName != Path.GetFileName(entry.AssetName))
            throw new InvalidDataException("Refusing untrusted GitHub mod asset");
        string mods = ModsRoot(gameRoot);
        Directory.CreateDirectory(mods);
        var old = ReadReceipt(gameRoot, entry);
        if (old != null && !IsManaged(gameRoot, entry))
            throw new IOException("The installed mod was modified. Existing JAR preserved.");
        if (old == null && File.Exists(Path.Combine(mods, entry.AssetName)))
            throw new IOException("A manually installed JAR has this name. Refusing to overwrite it.");

        if (old != null && !HasUpdate(gameRoot, entry))
        {
            report(entry.Name + " already current and SHA-256 verified.");
            return;
        }

        string staged = Path.Combine(mods, "." + Guid.NewGuid().ToString("N") + ".tmp");
        string receiptTemp = Path.Combine(mods, "." + Guid.NewGuid().ToString("N") + ".json.tmp");
        try
        {
            report("Downloading " + entry.Name + " " + entry.Version + " from trusted GitHub release...");
            using (var response = await Http.GetAsync(entry.DownloadUrl,
                       HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaximumJarBytes)
                    throw new InvalidDataException("Oversized optional mod download");
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(staged);
                byte[] buffer = new byte[65536];
                int received = 0;
                for (int count; (count = await source.ReadAsync(buffer, ct)) > 0;)
                {
                    received += count;
                    if (received > MaximumJarBytes) throw new InvalidDataException("Oversized mod JAR");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }

            string modId;
            using (var jar = ZipFile.OpenRead(staged))
            {
                var manifest = jar.GetEntry("coda.mod.json")
                    ?? throw new InvalidDataException("Downloaded asset is not a H.O.W.L. mod");
                if (manifest.Length is <= 0 or > 8192)
                    throw new InvalidDataException("Invalid mod manifest size");
                using var data = manifest.Open();
                using var memory = new MemoryStream();
                await data.CopyToAsync(memory, ct);
                using var json = JsonDocument.Parse(memory.ToArray());
                var root = json.RootElement;
                modId = root.GetProperty("id").GetString() ?? "";
                var version = root.GetProperty("version").GetString() ?? "";
                var minecraft = root.GetProperty("minecraft").GetString() ?? "";
                var entrypoint = root.GetProperty("entrypoint").GetString() ?? "";
                string normalized = slug.Replace('-', '_');
                if (modId != normalized && modId != "hw_" + normalized)
                    throw new InvalidDataException("Release filename and mod manifest ID disagree");
                if (version != entry.Version || minecraft != "26.4-snapshot-3")
                    throw new InvalidDataException("Mod version or Minecraft compatibility mismatch");
                if (entrypoint.Length is < 1 or > 180 ||
                    !entrypoint.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '$')
                    || jar.GetEntry(entrypoint.Replace('.', '/') + ".class") == null)
                    throw new InvalidDataException("Missing H.O.W.L. mod entrypoint class");
                if (jar.Entries.Count > 12000)
                    throw new InvalidDataException("Unreasonable mod archive");
            }

            string hash = Hash(staged);
            string target = Path.Combine(mods, entry.AssetName);
            // Never replace an unrelated JAR, even during a version upgrade.
            if (File.Exists(target) && (old == null ||
                old.FileName != entry.AssetName || !IsManaged(gameRoot, entry)))
                throw new IOException("A different local mod already owns the target filename");
            string receipts = Path.GetDirectoryName(ReceiptPath(gameRoot, slug))!;
            Directory.CreateDirectory(receipts);
            var next = new Receipt(slug, modId, entry.AssetName, entry.Version, entry.Tag, hash);
            await File.WriteAllTextAsync(receiptTemp, JsonSerializer.Serialize(next), ct);

            File.Move(staged, target, true);
            File.Move(receiptTemp, ReceiptPath(gameRoot, slug), true);
            if (old != null && old.FileName != entry.AssetName)
            {
                string previous = Path.Combine(mods, old.FileName);
                // Only the old hash-owned JAR can be cleaned up.
                if (File.Exists(previous) && Hash(previous).Equals(old.Hash,
                    StringComparison.OrdinalIgnoreCase)) File.Delete(previous);
            }
            report(entry.Name + " installed: " + entry.Tag + " (SHA-256 verified locally).");
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
            if (File.Exists(receiptTemp)) File.Delete(receiptTemp);
        }
    }

    internal static void Uninstall(GitHubModRelease entry, string gameRoot,
        Action<string> report)
    {
        var receipt = ReadReceipt(gameRoot, entry)
            ?? throw new IOException("No managed installation found. Manual files are preserved.");
        if (!IsManaged(gameRoot, entry))
            throw new IOException("The installed JAR is changed or missing. Nothing was deleted.");
        File.Delete(Path.Combine(ModsRoot(gameRoot), receipt.FileName));
        File.Delete(ReceiptPath(gameRoot, Slug(entry)));
        report(entry.Name + " removed. Worlds and resource packs were not touched.");
    }

    private static string Hash(string file)
    {
        using var source = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
    }
}
