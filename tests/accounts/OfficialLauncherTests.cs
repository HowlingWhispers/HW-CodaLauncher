using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using HowlingWhispers.CodaLauncher;

internal static class OfficialLauncherTests
{
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "coda-launcher-fixture-" + Guid.NewGuid().ToString("N"));
        string minecraft = Path.Combine(root, ".minecraft"), coda = Path.Combine(root, "cml"),
            jar = Path.Combine(root, "loader", "CodaLoader.jar");
        Directory.CreateDirectory(minecraft);
        Directory.CreateDirectory(Path.GetDirectoryName(jar)!);
        await File.WriteAllTextAsync(jar, "fixture binary");
        var launcherProfiles = Path.Combine(minecraft, "launcher_profiles.json");
        await File.WriteAllTextAsync(launcherProfiles,
            """{"clientToken":"leave-me-alone","profiles":{"otherProfile":{"name":"Friend's world","type":"custom"}}}""");
        var original = await File.ReadAllTextAsync(launcherProfiles);
        using var http = new HttpClient(new Handler());
        try
        {
            string selected = await OfficialMinecraftLauncher.InstallProfileAsync(jar, coda, default, minecraft, http);
            Check(selected == launcherProfiles, "expected official profiles path");
            var updated = JsonNode.Parse(await File.ReadAllTextAsync(launcherProfiles))!;
            Check(updated["clientToken"]?.ToString() == "leave-me-alone", "unrelated launcher metadata changed");
            Check(updated["profiles"]?["otherProfile"]?["name"]?.ToString() == "Friend's world", "unrelated profile changed");
            Check(updated["profiles"]?[OfficialMinecraftLauncher.ProfileId]?["gameDir"]?.ToString() == Path.GetFullPath(coda), "isolated world directory missing");
            Check(updated["profiles"]?[OfficialMinecraftLauncher.ProfileId]?["name"]?.ToString() == OfficialMinecraftLauncher.ProfileName,
                "HOWL installation name missing");
            Check(updated["profiles"]?[OfficialMinecraftLauncher.ProfileId]?["lastVersionId"]?.ToString() == OfficialMinecraftLauncher.VersionId,
                "custom version association missing");
            Check(File.Exists(launcherProfiles + ".codaloader-backup"), "profile backup missing");
            Check(await File.ReadAllTextAsync(launcherProfiles + ".codaloader-backup") == original, "first backup corrupted");
            var version = JsonNode.Parse(await File.ReadAllTextAsync(
                Path.Combine(minecraft, "versions", OfficialMinecraftLauncher.VersionId,
                    OfficialMinecraftLauncher.VersionId + ".json")))!;
            Check(version["id"]?.ToString() == OfficialMinecraftLauncher.VersionId, "custom Minecraft ID wrong");
            var jvm = version["arguments"]?["jvm"]!.AsArray().Select(x => x!.ToString()).ToList() ?? [];
            Check(jvm.Any(x => x.StartsWith("-javaagent:") && x.Contains("CodaLoader.jar") && x.Contains(Path.GetFullPath(coda).Replace('\\', '/'))),
                "Java agent not installed in JVM arguments");
            Check(jvm.Contains("-Dcodaloader.officialLauncher=true"), "profile ownership marker missing");
            await OfficialMinecraftLauncher.InstallProfileAsync(jar, coda, default, minecraft, http);
            Check(JsonNode.Parse(await File.ReadAllTextAsync(launcherProfiles))!["profiles"]!.AsObject().Count == 2,
                "repeated registration duplicated profiles");

            var p = JsonNode.Parse(await File.ReadAllTextAsync(launcherProfiles))!;
            p["profiles"]![OfficialMinecraftLauncher.ProfileId]!["name"] = "Howling Whispers | CodaLoader";
            p["profiles"]![OfficialMinecraftLauncher.ProfileId]!["javaArgs"] = "-Xmx4G";
            p["profiles"]![OfficialMinecraftLauncher.ProfileId]!.AsObject().Remove("codaloaderManaged");
            await File.WriteAllTextAsync(launcherProfiles, p.ToJsonString());
            await OfficialMinecraftLauncher.InstallProfileAsync(jar, coda, default, minecraft, http);
            Check(JsonNode.Parse(await File.ReadAllTextAsync(launcherProfiles))!["profiles"]!.AsObject().Count == 2,
                "profile was lost when Mojang removed custom metadata");
            var migrated = JsonNode.Parse(await File.ReadAllTextAsync(launcherProfiles))!["profiles"]![OfficialMinecraftLauncher.ProfileId]!;
            Check(migrated["name"]?.ToString() == OfficialMinecraftLauncher.ProfileName && migrated["javaArgs"]?.ToString() == "-Xmx4G",
                "legacy profile migration lost its name or custom memory settings");
            migrated.AsObject().Remove("codaloaderManaged");
            var rewritten = JsonNode.Parse(await File.ReadAllTextAsync(launcherProfiles))!;
            rewritten["profiles"]![OfficialMinecraftLauncher.ProfileId] = migrated.DeepClone();
            await File.WriteAllTextAsync(launcherProfiles, rewritten.ToJsonString());
            await OfficialMinecraftLauncher.InstallProfileAsync(jar, coda, default, minecraft, http);
            p = JsonNode.Parse(await File.ReadAllTextAsync(launcherProfiles))!;
            p["profiles"]![OfficialMinecraftLauncher.ProfileId]!["codaloaderManaged"] = false;
            p["profiles"]![OfficialMinecraftLauncher.ProfileId]!["name"] = "Unrelated profile";
            await File.WriteAllTextAsync(launcherProfiles, p.ToJsonString());
            string protectedOriginal = await File.ReadAllTextAsync(launcherProfiles);
            bool rejected = false;
            try { await OfficialMinecraftLauncher.InstallProfileAsync(jar, coda, default, minecraft, http); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && await File.ReadAllTextAsync(launcherProfiles) == protectedOriginal,
                "non-CodaLoader profile overwritten");
            Console.WriteLine("PASS: official profile installed idempotently, preserves other profiles, installs agent, backs up and rejects collisions");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void Check(bool ok, string why)
    {
        if (!ok) throw new Exception("Official launcher fixture: " + why);
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var manifest = """{"id":"26.4-snapshot-3","type":"snapshot","arguments":{"jvm":["-Xmx2G"],"game":[]},"downloads":{"client":{"url":"https://piston-data.mojang.com/fixture.jar","sha1":"1234"}}}""";
            var payload = Encoding.UTF8.GetBytes(manifest);
            var hash = Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant();
            var index = "{\"versions\":[{\"id\":\"26.4-snapshot-3\",\"url\":\"https://piston-meta.mojang.com/fixture.json\",\"sha1\":\"" + hash + "\"}]}";
            string response = request.RequestUri!.AbsolutePath.EndsWith("version_manifest_v2.json") ? index : manifest;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(response, Encoding.UTF8, "application/json") });
        }
    }
}
