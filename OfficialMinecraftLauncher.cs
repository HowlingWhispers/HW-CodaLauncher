using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Registers an isolated CodaLoader installation with Mojang's official launcher.
/// Authentication stays entirely inside Minecraft Launcher; no token is read here.
/// Never writes into accounts, tokens, or any profile other than our own.
/// </summary>
internal static class OfficialMinecraftLauncher
{
    internal const string MinecraftVersion = "26.4-snapshot-3";
    internal const string VersionId = "codaloader-" + MinecraftVersion;
    internal const string ProfileId = "HowlingWhispers-CodaLoader";
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    internal static string DefaultMinecraftDirectory =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
            : OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "minecraft")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minecraft");

    internal static bool IsLauncherRunning()
    {
        foreach (var name in new[] { "MinecraftLauncher", "minecraft-launcher", "Minecraft Launcher" })
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0) return true;
            }
            catch { /* best effort: launcher may live in an isolated OS package */ }
        }
        return false;
    }

    internal static async Task<string> InstallProfileAsync(
        string loaderJar, string gameDirectory, CancellationToken ct,
        string? minecraftDirectory = null, HttpClient? client = null,
        string? basePackDirectory = null)
    {
        var minecraft = Path.GetFullPath(minecraftDirectory ?? DefaultMinecraftDirectory);
        loaderJar = Path.GetFullPath(loaderJar);
        gameDirectory = Path.GetFullPath(gameDirectory);
        if (!File.Exists(loaderJar)) throw new FileNotFoundException("CodaLoader must be installed first.", loaderJar);
        if (!Directory.Exists(minecraft))
            throw new InvalidOperationException("Official Minecraft Launcher has not set up its Minecraft folder. Open it once and sign in, then close it before clicking Play here.");
        if (IsLauncherRunning())
            throw new InvalidOperationException("Close the official Minecraft Launcher first so it does not overwrite the new CodaLoader profile. Then click Play here again.");

        string profilesPath = LocateProfiles(minecraft);
        using var ownedClient = client is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : null;
        var http = client ?? ownedClient!;

        // Use the official signed-by-hash version metadata, not a modified or
        // private launcher account cache. The launcher will download the game.
        var index = JsonNode.Parse(await http.GetStringAsync(ManifestUrl, ct))
            as JsonObject ?? throw new InvalidDataException("Mojang version index is invalid.");
        JsonObject? reference = null;
        if (index["versions"] is JsonArray versions)
            foreach (var item in versions)
                if (item?["id"]?.GetValue<string>() == MinecraftVersion) { reference = item as JsonObject; break; }
        if (reference is null)
            throw new InvalidOperationException("The target Minecraft snapshot is not listed in Mojang's version manifest.");
        var url = reference["url"]?.GetValue<string>() ?? "";
        var digest = reference["sha1"]?.GetValue<string>() ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.EndsWith(".mojang.com", StringComparison.OrdinalIgnoreCase) ||
            digest.Length != 40)
            throw new InvalidDataException("Mojang returned unexpected version metadata.");
        var bytes = await http.GetByteArrayAsync(uri, ct);
        var sha = Convert.ToHexString(SHA1.HashData(bytes));
        if (!sha.Equals(digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Minecraft version metadata checksum failed.");
        var version = JsonNode.Parse(bytes) as JsonObject
            ?? throw new InvalidDataException("Invalid official Minecraft version JSON.");
        if (version["id"]?.GetValue<string>() != MinecraftVersion)
            throw new InvalidDataException("Unexpected Minecraft version ID.");
        if (version["arguments"] is not JsonObject arguments || arguments["jvm"] is not JsonArray jvm)
            throw new InvalidDataException("Snapshot has no compatible JVM arguments for CodaLoader.");
        version["id"] = VersionId;
        var agent = loaderJar.Replace('\\', '/');
        var root = gameDirectory.Replace('\\', '/');
        var basePack = Path.GetFullPath(basePackDirectory ?? Path.Combine(
            Path.GetDirectoryName(gameDirectory)!, "resourcepacks", "cml-base-resources")).Replace('\\', '/');
        if (agent.Contains('\n') || root.Contains('\n') || agent.Contains('\r') || root.Contains('\r')
            || agent.Contains('=') || basePack.Contains('\n') || basePack.Contains('\r'))
            throw new InvalidOperationException("CodaLoader is installed in a path unsuitable for Java agent arguments.");
        jvm.Add("-javaagent:" + agent + "=" + root);
        jvm.Add("-Dcodaloader.officialLauncher=true");
        jvm.Add("-Dcodaloader.basePack=" + basePack);

        var profiles = JsonNode.Parse(await File.ReadAllTextAsync(profilesPath, ct)) as JsonObject
            ?? throw new InvalidDataException("Minecraft launcher profiles JSON is invalid. Nothing was changed.");
        var entries = profiles["profiles"] as JsonObject
            ?? throw new InvalidDataException("The official launcher profiles file is missing its profiles object.");
        var existing = entries[ProfileId] as JsonObject;
        // Minecraft Launcher may discard unknown profile fields when it saves.
        // Accept our original profile signature even if that custom marker is
        // removed, while still refusing an unrelated profile collision.
        bool recognizable = existing is not null && (
            existing["codaloaderManaged"]?.ToString() == "true" ||
            (existing["name"]?.ToString() == "Howling Whispers | CodaLoader" &&
             existing["lastVersionId"]?.ToString() == VersionId &&
             existing["gameDir"]?.ToString() == gameDirectory));
        if (entries.ContainsKey(ProfileId) && !recognizable)
            throw new InvalidOperationException("An existing Minecraft installation has the reserved Howling Whispers profile ID. It was not overwritten.");

        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var profile = existing is null ? new JsonObject() : (JsonObject)existing.DeepClone();
        profile["name"] = "Howling Whispers | CodaLoader";
        profile["type"] = "custom";
        profile["lastVersionId"] = VersionId;
        profile["gameDir"] = gameDirectory;
        profile["codaloaderManaged"] = true;
        if (profile["created"] is null) profile["created"] = now;
        profile["lastUsed"] = now;
        entries[ProfileId] = profile;

        // Preserve the original launcher file before changing only our profile.
        // Do not overwrite custom Minecraft versions not owned by CodaLoader.
        string versionPath = Path.Combine(minecraft, "versions", VersionId, VersionId + ".json");
        if (File.Exists(versionPath))
        {
            var previous = JsonNode.Parse(await File.ReadAllTextAsync(versionPath, ct)) as JsonObject;
            if (previous?["id"]?.GetValue<string>() != VersionId ||
                previous["arguments"]?["jvm"] is not JsonArray oldJvms ||
                !oldJvms.Any(x => x is JsonValue v && v.TryGetValue<string>(out var value)
                    && value.StartsWith("-Dcodaloader.officialLauncher=", StringComparison.Ordinal)))
                throw new InvalidOperationException("A non-CodaLoader Minecraft version uses our version ID. Nothing was overwritten.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(versionPath)!);
        Directory.CreateDirectory(gameDirectory);
        await AtomicWriteAsync(versionPath, version.ToJsonString(), ct);
        // Backups intentionally persist next to the original launcher profile.
        File.Copy(profilesPath, profilesPath + ".codaloader-backup", overwrite: true);
        await AtomicWriteAsync(profilesPath, profiles.ToJsonString(), ct);
        return profilesPath;
    }

    private static string LocateProfiles(string directory)
    {
        var store = Path.Combine(directory, "launcher_profiles_microsoft_store.json");
        var desktop = Path.Combine(directory, "launcher_profiles.json");
        if (OperatingSystem.IsWindows() && File.Exists(store)) return store;
        if (File.Exists(desktop)) return desktop;
        if (File.Exists(store)) return store;
        throw new InvalidOperationException("Official Minecraft Launcher profiles were not found. Run the official Minecraft Launcher once, close it, then try again.");
    }

    private static async Task AtomicWriteAsync(string path, string text, CancellationToken ct)
    {
        var temporary = path + ".coda-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false), ct);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Best-effort open. User can always open Minecraft Launcher manually.</summary>
    internal static bool TryOpenLauncher()
    {
        try
        {
            var candidates = OperatingSystem.IsWindows()
                ? new[] {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "Minecraft Launcher", "MinecraftLauncher.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Programs", "Minecraft Launcher", "MinecraftLauncher.exe")
                }
                : OperatingSystem.IsMacOS()
                    ? new[] { "/Applications/Minecraft.app" }
                    : new[] { "/usr/bin/minecraft-launcher", "/usr/local/bin/minecraft-launcher" };
            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) continue;
                var info = OperatingSystem.IsMacOS()
                    ? new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false, ArgumentList = { candidate } }
                    : new ProcessStartInfo(candidate) { UseShellExecute = true };
                if (Process.Start(info) is not null) return true;
            }
            // Windows Store builds do not expose a normal MinecraftLauncher.exe path.
            // Ask the shell to activate the official package. If it isn't installed,
            // the caller still explains how to open the launcher manually.
            if (OperatingSystem.IsWindows())
            {
                var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                info.ArgumentList.Add(@"shell:AppsFolder\Microsoft.4297127D64EC6_8wekyb3d8bbwe!Minecraft");
                if (Process.Start(info) is not null) return true;
            }
        }
        catch { /* caller prints the manual-launch instructions */ }
        return false;
    }
}
