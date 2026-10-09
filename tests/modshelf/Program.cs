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

    Console.WriteLine("PASS: " + count + " offline Nightly release-tag / SHA-256 identity assertions");
}
finally { Directory.Delete(temporary, recursive: true); }
