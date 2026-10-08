using HowlingWhispers.CodaLauncher;

var root = Path.Combine(Path.GetTempPath(), "coda-resource-install-" + Guid.NewGuid().ToString("N"));
var source = Path.Combine(root, "incoming");
var target = Path.Combine(root, "resourcepacks", "cml-base-resources");
var checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
try
{
    Directory.CreateDirectory(Path.Combine(source, "branding"));
    Directory.CreateDirectory(Path.Combine(source, "music", "default"));
    File.WriteAllText(Path.Combine(source, "branding", "title.png"), "original official branding");
    File.WriteAllText(Path.Combine(source, "music", "default", "menu.ogg"), "official music");
    File.WriteAllText(Path.Combine(source, "resourcepack.json"), "{\"version\":1}");
    Check(!File.Exists(Path.Combine(source, "CodaLoader.jar")), "fixture must reproduce a resource pack without loader files");
    ResourcePackInstaller.Install(source, target);
    Check(File.ReadAllText(Path.Combine(target, "branding", "title.png")) == "original official branding", "fresh branding installation failed");
    Check(File.ReadAllText(Path.Combine(target, "music", "default", "menu.ogg")) == "official music", "nested music installation failed");
    Check(File.Exists(Path.Combine(target, "resourcepack.json")), "resource metadata was not installed");
    ResourcePackInstaller.Install(source, target);
    Check(!Directory.Exists(Path.Combine(root, "resourcepacks", "previous-resource-pack-code")), "identical reinstall should not create backups");
    var custom = Path.Combine(target, "music", "menu", "my-song.ogg");
    Directory.CreateDirectory(Path.GetDirectoryName(custom)!);
    File.WriteAllText(custom, "player-added music");
    File.WriteAllText(Path.Combine(source, "branding", "title.png"), "updated official branding");
    ResourcePackInstaller.Install(source, target);
    Check(File.ReadAllText(Path.Combine(target, "branding", "title.png")) == "updated official branding", "branding update failed");
    Check(File.ReadAllText(custom) == "player-added music", "update removed player-added content");
    var backups = Path.Combine(root, "resourcepacks", "previous-resource-pack-code");
    Check(Directory.EnumerateFiles(backups, "title.png", SearchOption.AllDirectories).Any(p => File.ReadAllText(p) == "original official branding"), "previous branding was not preserved");
    var invalid = Path.Combine(root, "invalid");
    Directory.CreateDirectory(Path.Combine(invalid, "branding"));
    bool refused = false;
    try { ResourcePackInstaller.Install(invalid, target); }
    catch (InvalidDataException) { refused = true; }
    Check(refused, "invalid resource pack should be rejected");
    Check(File.ReadAllText(Path.Combine(target, "branding", "title.png")) == "updated official branding" && File.ReadAllText(custom) == "player-added music", "invalid pack changed existing files");
    Check(!Directory.EnumerateFiles(target, "*.tmp", SearchOption.AllDirectories).Any(), "temporary installation files were left behind");
    Console.WriteLine($"PASS: {checks} resource-pack installation checks, including the reported missing-loader regression");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
