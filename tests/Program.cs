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
    Console.WriteLine($"Managed mod installation tests passed: {checks} checks.");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
