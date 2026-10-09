using System.Security.Cryptography;
using HowlingWhispers.CodaLauncher;

int count = 0;
void Check(bool ok, string label)
{
    count++;
    if (!ok) throw new Exception("FAILED: " + label);
}
string temporary = Path.Combine(Path.GetTempPath(), "howl-mod-identity-" + Guid.NewGuid().ToString("N"));
string nightly = Path.Combine(temporary, "nightly", "minecraft");
string mods = Path.Combine(nightly, "mods");
string loader = Path.Combine(temporary, "nightly", "loader");
Directory.CreateDirectory(mods);
Directory.CreateDirectory(loader);
try
{
    string wolfName = "coda-wolf-0.1.0-dev.jar";
    string wolfFile = Path.Combine(mods, wolfName);
    File.WriteAllBytes(wolfFile, [11, 22, 33, 44]);
    string wolfHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(wolfFile))).ToLowerInvariant();
    string wolfTag = "nightly-codawolf-20261008-67c20977c9";
    File.WriteAllText(Path.Combine(mods, ".howl-codawolf-managed.sha256"), wolfHash);
    File.WriteAllText(Path.Combine(mods, ".howl-codawolf-tag"), wolfTag);

    var wolf = new ModInfo(wolfName, "coda_wolf", "Coda Wolf Companion", "0.1.0-dev", true, null);
    var verified = NightlyModIdentity.Attach(wolf, nightly);
    Check(verified.ReleaseStatus == "Verified" && verified.ReleaseTag == wolfTag,
        "Wolf Nightly build identity comes from tag plus verified JAR bytes, not 0.1.0-dev");
    Check(verified.Version == "0.1.0-dev", "Manifest version remains an independent value");

    // Same metadata version, different GitHub release: visible tag must change.
    string newTag = "nightly-codawolf-20261009-bb223344";
    File.WriteAllText(Path.Combine(mods, ".howl-codawolf-tag"), newTag);
    Check(NightlyModIdentity.Attach(wolf, nightly).ReleaseTag == newTag,
        "Two JAR releases with identical mod version remain visibly distinguishable");

    File.WriteAllBytes(wolfFile, [55, 66, 77]);
    var altered = NightlyModIdentity.Attach(wolf, nightly);
    Check(altered.ReleaseStatus == "Modified" && altered.ReleaseTag is null,
        "Tampered JAR must not display verified release");
    File.Delete(Path.Combine(mods, ".howl-codawolf-managed.sha256"));
    Check(NightlyModIdentity.Attach(wolf, nightly).ReleaseStatus == "Untracked",
        "Missing checksum marker must not imply verified build");

    string bcName = "buildcraft-cml-0.1.0-dev.jar";
    string bcFile = Path.Combine(mods, bcName);
    File.WriteAllBytes(bcFile, [1, 2, 3, 4, 5]);
    string bcHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bcFile))).ToLowerInvariant();
    string bcTag = "nightly-buildcraft-20261008-2cf61acc91";
    File.WriteAllText(Path.Combine(mods, ".howl-buildcraft-managed.sha256"), bcHash);
    File.WriteAllText(Path.Combine(loader, ".nightly-tag"), bcTag);
    var buildcraft = new ModInfo(bcName, "buildcraft_cml", "BuildCraft CML", "0.1.0-dev", true, null);
    Check(NightlyModIdentity.Attach(buildcraft, nightly).ReleaseTag == bcTag,
        "BuildCraft installed identity reads loader's Nightly tag and local mod checksum");

    var essentials = new ModInfo("hw-essentials.jar", "hw_essentials", "HW Essentials", "0.2.0", true, null);
    Check(NightlyModIdentity.Attach(essentials, nightly).ReleaseTag is null,
        "Unmanaged Essentials must never inherit a Nightly release identity");

    string stable = Path.Combine(temporary, "minecraft");
    Directory.CreateDirectory(Path.Combine(stable, "mods"));
    Check(NightlyModIdentity.Attach(buildcraft, stable).ReleaseTag is null,
        "Stable profile must not claim Nightly identity from another game root");

    File.WriteAllText(Path.Combine(mods, ".howl-buildcraft-managed.sha256"), new string('f', 64));
    var modifiedBc = NightlyModIdentity.Attach(buildcraft, nightly);
    Check(modifiedBc.ReleaseStatus == "Modified" && modifiedBc.ReleaseTag is null,
        "BuildCraft mismatched local bytes cannot be called verified");

    // Loader can advance without changing the optional BuildCraft release.
    File.WriteAllText(Path.Combine(mods, ".howl-buildcraft-managed.sha256"), bcHash);
    File.WriteAllText(Path.Combine(mods, ".howl-buildcraft-tag"), bcTag);
    File.WriteAllText(Path.Combine(loader, ".nightly-tag"), "nightly-buildcraft-20261009-newruntime");
    var untouchedBuildcraft = NightlyModIdentity.Attach(buildcraft, nightly);
    Check(untouchedBuildcraft.ReleaseTag == bcTag,
        "Required runtime update must not relabel an unchanged optional BuildCraft JAR");

    File.WriteAllBytes(bcFile, [1, 2, 3, 4, 5]);
    var catalog = OptionalModCatalog.Build([wolf, buildcraft], nightly, true);
    Check(catalog.Count == 3 && catalog.Count(x => x.Required) == 2
            && catalog.First(x => x.Id == "coda_wolf").Required
            && catalog.First(x => x.Id == "hw_essentials").Required
            && !catalog.First(x => x.Id == "buildcraft_cml").Required,
        "Coda and Essentials required; experimental BuildCraft optional.");
    Check(catalog.First(x => x.Id == "coda_wolf").Installed,
        "Present Coda Wolf appears installed even if manually modified");
    Check(!catalog.First(x => x.Id == "hw_essentials").Installed,
        "Uninstalled Essentials appears missing until bundled H.O.W.L. provisions it");
    var stableCatalog = OptionalModCatalog.Build([], stable, false);
    Check(stableCatalog.All(x => !x.Installed)
            && stableCatalog.Count(x => x.Required) == 2
            && stableCatalog.First(x => x.Id == "coda_wolf").Required
            && stableCatalog.First(x => x.Id == "hw_essentials").Required,
        "Fresh Stable profile requires Coda and Essentials but does not fake installation");
    Check(!stableCatalog.Any(x => x.Id == "buildcraft_cml"),
        "Retired BuildCraft must not be advertised to new installs.");
    Check(catalog.First(x => x.Id == "buildcraft_cml").Name.StartsWith("RETIRED"),
        "Previously installed BuildCraft must be labeled retired for safe uninstall.");

    // A newly published GitHub mod is discovered without writing a new
    // hard-coded OptionalModCatalog entry. Required components never appear.
    string releases = """
      [
        {"tag_name":"v0.0.35","draft":false,"assets":[
          {"name":"buildcraft-lite-0.1.0-dev.jar",
           "browser_download_url":"https://github.com/HowlingWhispers/HW-CodaLoader/releases/download/v0.0.35/buildcraft-lite-0.1.0-dev.jar"},
          {"name":"hw-essentials.jar",
           "browser_download_url":"https://github.com/HowlingWhispers/HW-CodaLoader/releases/download/v0.0.35/hw-essentials.jar"},
          {"name":"howl-api-0.0.35.jar",
           "browser_download_url":"https://github.com/HowlingWhispers/HW-CodaLoader/releases/download/v0.0.35/howl-api-0.0.35.jar"},
          {"name":"buildcraft-lite-0.1.0-dev.jar",
           "browser_download_url":"https://malicious.example/whatever.jar"}
        ]},
        {"tag_name":"evil-draft","draft":true,"assets":[
          {"name":"experimental-mod-1.0.0.jar",
           "browser_download_url":"https://github.com/HowlingWhispers/HW-CodaLoader/releases/download/evil-draft/experimental-mod-1.0.0.jar"}
        ]}
      ]
      """;
    var available = GitHubModCatalog.ParseReleases(releases, "HW-CodaLoader");
    Check(available.Count == 1 && available[0].Id == "github:buildcraft-lite",
        "Discovery lists only eligible owner-published optional H.O.W.L. JARs");
    Check(!GitHubModCatalog.TrustedUrl("https://github.com.evil.example/HowlingWhispers/HW-CodaLoader/releases/download/v0.0.35/buildcraft-lite-0.1.0-dev.jar",
        "HW-CodaLoader", "v0.0.35", "buildcraft-lite-0.1.0-dev.jar"),
        "Disallow deceptive GitHub download URLs");
    var shelf = OptionalModCatalog.Build([], nightly, true, available);
    Check(shelf.Count == 3 && shelf.Single(x => x.Id == "github:buildcraft-lite").AvailableVersion == "0.1.0-dev",
        "Discovered GitHub release automatically appears alongside required mods");

    // A complete offline download fixture validates the archive manifest,
    // the SHA receipt, updates, user edits, and removal without real HTTP.
    byte[] JarBytes(string version) {
        using var mem = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(mem, System.IO.Compression.ZipArchiveMode.Create, true)) {
            var metadata = zip.CreateEntry("coda.mod.json");
            using (var writer = new StreamWriter(metadata.Open()))
                writer.Write(System.Text.Json.JsonSerializer.Serialize(new {
                    schema = 1, id = "hw_buildcraft_lite", name = "BuildCraft Lite",
                    version, minecraft = "26.4-snapshot-3",
                    entrypoint = "dev.howlingwhispers.buildcraftlite.BuildCraftLiteMod"
                }));
            using var entry = zip.CreateEntry("dev/howlingwhispers/buildcraftlite/BuildCraftLiteMod.class").Open();
            entry.WriteByte(42);
        }
        return mem.ToArray();
    }
    var payload = JarBytes("0.1.0-dev");
    using var http = new HttpClient(new StubHandler(() => payload));
    var store = new GitHubModStore(http);
    string game = Path.Combine(temporary, "discovered-game");
    var release = available.Single();
    await store.InstallAsync(release, game, _ => {}, CancellationToken.None);
    Check(GitHubModStore.IsManaged(game, release),
        "Downloaded JAR is managed only after manifest and hash verification");
    Check(File.Exists(Path.Combine(game, "mods", release.AssetName)), "Mod installed in active profile mods/");
    Check(!GitHubModStore.HasUpdate(game, release), "Installed matching tag has no pending update");

    var changed = release with {
        Version = "0.1.1-dev", Tag = "v0.0.36",
        AssetName = "buildcraft-lite-0.1.1-dev.jar",
        DownloadUrl = "https://github.com/HowlingWhispers/HW-CodaLoader/releases/download/v0.0.36/buildcraft-lite-0.1.1-dev.jar"
    };
    Check(GitHubModStore.HasUpdate(game, changed), "New tagged release triggers installed-only update");
    payload = JarBytes("0.1.1-dev");
    await store.InstallAsync(changed, game, _ => {}, CancellationToken.None);
    Check(GitHubModStore.IsManaged(game, changed) &&
        !File.Exists(Path.Combine(game, "mods", release.AssetName)),
        "Updated package replaces only old owned JAR");
    string installed = Path.Combine(game, "mods", changed.AssetName);
    File.AppendAllText(installed, "user edit");
    Check(!GitHubModStore.IsManaged(game, changed), "Local edits revoke managed status");
    try {
        GitHubModStore.Uninstall(changed, game, _ => {});
        throw new Exception("Modified JAR uninstall should have been refused");
    }
    catch (IOException) { count++; }
    Check(File.Exists(installed), "Modified JAR remains untouched after refused uninstall");
    // Restore exact bytes to simulate the user reverting a local modification.
    File.WriteAllBytes(installed, payload);
    GitHubModStore.Uninstall(changed, game, _ => {});
    Check(!File.Exists(installed), "Owned JAR can be removed without affecting any world");

    Console.WriteLine("PASS: " + count + " offline GitHub shelf and SHA-256 mod install assertions");
}
finally { Directory.Delete(temporary, recursive: true); }


internal sealed class StubHandler(Func<byte[]> bytes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new ByteArrayContent(bytes())
        });
}
