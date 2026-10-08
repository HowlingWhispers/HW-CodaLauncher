using HowlingWhispers.CodaLauncher;

var root = Path.Combine(Path.GetTempPath(), "coda-managed-mods-" + Guid.NewGuid().ToString("N"));
var loader = Path.Combine(root, "loader");
var game = Path.Combine(root, "minecraft");
var bundled = Path.Combine(loader, "run", "mods", "hw-essentials.jar");
var target = Path.Combine(game, "mods", "hw-essentials.jar");
var checks = 0;
void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
void Refuses(Action action) {
    try { action(); } catch (IOException) { checks++; return; }
    throw new Exception("Expected installation refusal");
}
try
{
    Check(!ManagedMods.IsCurrent(loader, game), "missing mod is not current");
    Refuses(() => ManagedMods.Install(loader, game, _ => {}));
    Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
    File.WriteAllText(bundled, "first official mod");
    ManagedMods.Install(loader, game, _ => {});
    Check(ManagedMods.IsCurrent(loader, game), "first install detected in active profile");
    var config = Path.Combine(game, "config", "hw_essentials", "essentials.properties");
    Directory.CreateDirectory(Path.GetDirectoryName(config)!);
    File.WriteAllText(config, "max-homes=25");
    var home = Path.Combine(game, "saves", "world", "cml", "hw-essentials", "homes", "player.properties");
    Directory.CreateDirectory(Path.GetDirectoryName(home)!);
    File.WriteAllText(home, "saved home");
    File.WriteAllText(bundled, "second official mod");
    Check(!ManagedMods.IsCurrent(loader, game), "stale mod detected even with unchanged loader version");
    ManagedMods.Install(loader, game, _ => {});
    Check(File.ReadAllText(target) == "second official mod", "known official mod updated");
    Check(File.ReadAllText(config) == "max-homes=25" && File.ReadAllText(home) == "saved home", "homes and settings preserved");
    File.Delete(target);
    Check(!ManagedMods.IsCurrent(loader, game), "deleted mod detected");
    ManagedMods.Install(loader, game, _ => {});
    Check(ManagedMods.IsCurrent(loader, game), "deleted mod repaired");
    File.WriteAllText(target, "manually changed mod");
    Refuses(() => ManagedMods.Install(loader, game, _ => {}));
    Check(File.ReadAllText(target) == "manually changed mod", "manual change preserved");
    File.Delete(Path.Combine(game, "config", "codaloader-managed", "hw-essentials.sha256"));
    Refuses(() => ManagedMods.Install(loader, game, _ => {}));
    File.Copy(bundled, target, true);
    ManagedMods.Install(loader, game, _ => {});
    Check(ManagedMods.IsCurrent(loader, game), "matching untracked official JAR adopted");
    // Historic loader/run/mods is migration input, never an active scanner.
    var legacyCustom = Path.Combine(loader, "run", "mods", "custom-howling.jar");
    var liveCustom = Path.Combine(game, "mods", "custom-howling.jar");
    File.WriteAllText(legacyCustom, "custom player mod v1");
    var migrationLog = new List<string>();
    ManagedMods.MigrateLegacy(loader, game, migrationLog.Add);
    Check(File.ReadAllText(liveCustom) == "custom player mod v1", "third-party mod copied to game/mods");
    Check(File.ReadAllText(legacyCustom) == "custom player mod v1", "source preserved after migration");
    File.WriteAllText(liveCustom, "player edited version");
    ManagedMods.MigrateLegacy(loader, game, migrationLog.Add);
    Check(File.ReadAllText(liveCustom) == "player edited version", "different target is not overwritten");
    Check(File.ReadAllText(legacyCustom) == "custom player mod v1", "conflicting source is preserved");
    Check(migrationLog.Any(line => line.Contains("CONFLICT")), "conflict reported to user");

    // The new distribution does not need a second mods seed directory.
    // HW Essentials is embedded as an archive entry in CodaLoader.jar.
    var loaderJar = Path.Combine(loader, "CodaLoader.jar");
    using (var zip = System.IO.Compression.ZipFile.Open(loaderJar,
        System.IO.Compression.ZipArchiveMode.Create))
    {
        var embedded = zip.CreateEntry("codaloader/mods/hw-essentials.jar");
        using var stream = new StreamWriter(embedded.Open());
        stream.Write("third official mod embedded in loader");
    }
    ManagedMods.Install(loader, game, migrationLog.Add);
    Check(File.ReadAllText(target) == "third official mod embedded in loader",
        "embedded official JAR supersedes old distribution seed");
    ManagedMods.ArchiveLegacy(loader, migrationLog.Add);
    Check(!Directory.Exists(Path.Combine(loader, "run", "mods")),
        "legacy duplicate path no longer exists after archival");
    Check(File.ReadAllText(liveCustom) == "player edited version",
        "archival does not affect active mods");
    var backups = Path.Combine(root, "legacy-loader-mods-backup");
    Check(Directory.Exists(backups), "preserved legacy archive exists");
    Check(Directory.EnumerateFiles(backups, "custom-howling.jar",
            SearchOption.AllDirectories).Any(p => File.ReadAllText(p) == "custom player mod v1"),
        "old mod contents and unresolved conflict retained in backup");
    Check(ManagedMods.IsCurrent(loader, game), "current-state check reads embedded official mod");

    // Explicit uninstall affects only the managed optional JAR and SHA marker.
    ManagedMods.Uninstall(game, _ => {});
    Check(!File.Exists(target), "Optional Essentials uninstall removes the JAR");
    Check(!ManagedMods.IsManagedInstall(game), "Uninstalled Essentials is no longer managed");
    Check(File.ReadAllText(config) == "max-homes=25"
          && File.ReadAllText(home) == "saved home",
          "Uninstall must preserve settings and every existing world save");
    ManagedMods.Install(loader, game, _ => {});
    File.WriteAllText(target, "user-edited private copy");
    Refuses(() => ManagedMods.Uninstall(game, _ => {}));
    Check(File.ReadAllText(target) == "user-edited private copy",
          "Uninstall must refuse a tampered Essentials JAR");
    Console.WriteLine($"Managed mod installation and no-clobber migration tests passed: {checks} checks.");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
