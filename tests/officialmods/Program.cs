using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HowlingWhispers.CodaLauncher;

int checks = 0;
void Check(bool b, string what) { checks++; if (!b) throw new Exception(what); }
byte[] Jar(string version) {
    using var bytes = new MemoryStream();
    using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true)) {
        using (var writer = new StreamWriter(zip.CreateEntry("coda.mod.json").Open())) writer.Write(JsonSerializer.Serialize(new {
            schema=1, id="howl_sorting_pipes", name="Sorting Pipes", version, minecraft="26.4-snapshot-3", entrypoint="dev.howlingwhispers.sortingpipes.SortingPipesMod"
        }));
        using var output = zip.CreateEntry("dev/howlingwhispers/sortingpipes/SortingPipesMod.class").Open(); output.WriteByte(42);
    }
    return bytes.ToArray();
}
string Manifest(string channel = "nightly", string minimum = "0.0.38") => JsonSerializer.Serialize(new {
    schema = 1, minecraft="26.4-snapshot-3", mods = new[] {new {slug="howl-sorting-pipes", modId="howl_sorting_pipes", name="Sorting Pipes", channel, minimumLoader=minimum,
        requiredLoaderClass="dev/howlingwhispers/codaloader/api/CodaItemFilter.class"}}
});
string temporary=Path.Combine(Path.GetTempPath(),"howl-required-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
string loader=Path.Combine(temporary,"CodaLoader.jar");
void Loader(string version, bool feature = true) {
    if(File.Exists(loader))File.Delete(loader);
    using var zip=ZipFile.Open(loader,ZipArchiveMode.Create);
    using(var w=new StreamWriter(zip.CreateEntry("codaloader/runtime-version.json").Open()))w.Write(JsonSerializer.Serialize(new {version,minecraft="26.4-snapshot-3"}));
    if(feature)zip.CreateEntry("dev/howlingwhispers/codaloader/api/CodaItemFilter.class");
}
try {
    string version="1.0.0"; byte[] jar=Jar(version); bool offline=false,badHash=false,missingChecksum=false;
    using var http=new HttpClient(new Handler(request=> {
        if(offline)throw new HttpRequestException("Offline fixture");
        string url=request.RequestUri!.ToString();
        if(url==OfficialModsInstaller.ManifestUrl)return new StringContent(Manifest());
        if(url.Contains("HW-CodaLoader/releases?"))return new StringContent("[]");
        string tag="sorting-pipes-v"+version, filename="howl-sorting-pipes-"+version+".jar";
        string download="https://github.com/HowlingWhispers/HW-Mods/releases/download/"+tag+"/"+filename;
        if(url.Contains("HW-Mods/releases?"))return new StringContent(JsonSerializer.Serialize(new[]{new {tag_name=tag,draft=false,assets=new[]{new {name=filename,browser_download_url=download}}}}));
        if(url.EndsWith(".sha256")) {
            if(missingChecksum)throw new HttpRequestException("Missing sidecar");
            return new StringContent((badHash?new string('0',64):Convert.ToHexString(SHA256.HashData(jar)))+"  "+filename);
        }
        if(url==download)return new ByteArrayContent(jar);
        throw new Exception("Unexpected request: "+url);
    }));
    var installer=new OfficialModsInstaller(http);
    string game=Path.Combine(temporary,"nightly","minecraft");
    var messages=new List<string>();
    Loader("0.0.37");
    try{await installer.EnsureAsync(game,loader,"nightly",messages.Add,CancellationToken.None);throw new Exception("Old loader accepted");}catch(InvalidDataException){checks++;}
    Check(!Directory.Exists(Path.Combine(game,"mods")),"Loader incompatibility detected before writing mods");
    Loader("0.0.38",false);
    try{await installer.EnsureAsync(game,loader,"nightly",messages.Add,CancellationToken.None);throw new Exception("Missing API accepted");}catch(InvalidDataException){checks++;}
    Loader("0.0.38");
    await installer.EnsureAsync(game,loader,"nightly",messages.Add,CancellationToken.None);
    string file=Path.Combine(game,"mods","howl-sorting-pipes-1.0.0.jar");
    Check(File.ReadAllBytes(file).SequenceEqual(jar),"First Nightly preparation automatically downloads required mod");
    Check(installer.IsRequired("github:howl-sorting-pipes","nightly"),"Required mod recognized in UI");
    Check(!installer.IsRequired("github:howl-sorting-pipes","stable"),"Nightly mod excluded from Stable");
    File.Delete(file);
    await installer.EnsureAsync(game,loader,"nightly",messages.Add,CancellationToken.None);
    Check(File.Exists(file),"Missing managed mod repaired");
    version="1.0.1";jar=Jar(version);
    await installer.EnsureAsync(game,loader,"nightly",messages.Add,CancellationToken.None);
    string latest=Path.Combine(game,"mods","howl-sorting-pipes-1.0.1.jar");
    Check(File.Exists(latest)&&!File.Exists(file),"New release installed and old hash-owned filename retired");
    byte[] previous=File.ReadAllBytes(latest);version="1.0.2";jar=Jar(version);badHash=true;
    try{await installer.EnsureAsync(game,loader,"nightly",messages.Add,CancellationToken.None);throw new Exception("Bad hash accepted");}catch(InvalidDataException){checks++;}
    Check(File.ReadAllBytes(latest).SequenceEqual(previous),"Published checksum failure preserves working mod");badHash=false;
    offline=true;
    await installer.EnsureAsync(game,loader,"nightly",messages.Add,CancellationToken.None);
    Check(messages.Any(m=>m.Contains("Updates not confirmed")),"Offline fallback explicitly reports unconfirmed update");offline=false;
    File.AppendAllText(latest,"player edit");
    try{await installer.EnsureAsync(game,loader,"nightly",messages.Add,CancellationToken.None);throw new Exception("Edited mod overwritten");}catch(IOException){checks++;}
    Check(File.ReadAllBytes(latest).Length>previous.Length,"Edited local mod preserved");
    string stable=Path.Combine(temporary,"stable");
    await installer.EnsureAsync(stable,loader,"stable",messages.Add,CancellationToken.None);
    Check(!Directory.Exists(Path.Combine(stable,"mods")),"Stable preparation never auto-installs Nightly code");
    offline=true;
    await new OfficialModsInstaller(http).EnsureAsync(Path.Combine(temporary,"fresh-stable"),loader,"stable",messages.Add,CancellationToken.None);
    Check(!Directory.Exists(Path.Combine(temporary,"fresh-stable","mods")),"Nightly catalog outage does not block an unchanged Stable profile");
    offline=false;
    string fresh=Path.Combine(temporary,"fresh");missingChecksum=true;
    try{await installer.EnsureAsync(fresh,loader,"nightly",messages.Add,CancellationToken.None);throw new Exception("Missing checksum accepted");}catch(IOException){checks++;}
    Check(!File.Exists(Path.Combine(fresh,"mods","howl-sorting-pipes-1.0.2.jar")),"Fresh install requires a published checksum");missingChecksum=false;
    string duplicate=Path.Combine(temporary,"duplicate");Directory.CreateDirectory(Path.Combine(duplicate,"mods"));File.WriteAllBytes(Path.Combine(duplicate,"mods","renamed.jar"),Jar("1.0.0"));
    try{await installer.EnsureAsync(duplicate,loader,"nightly",messages.Add,CancellationToken.None);throw new Exception("Duplicate mod ID installed");}catch(IOException){checks++;}
    Check(File.Exists(Path.Combine(duplicate,"mods","renamed.jar")),"Manual duplicate preserved");
    string adopt=Path.Combine(temporary,"adopt");Directory.CreateDirectory(Path.Combine(adopt,"mods"));File.WriteAllBytes(Path.Combine(adopt,"mods","howl-sorting-pipes-1.0.2.jar"),jar);
    await installer.EnsureAsync(adopt,loader,"nightly",messages.Add,CancellationToken.None);
    Check(Directory.Exists(Path.Combine(adopt,"mods",".howl-github-mods")),"Byte-identical published manual mod safely adopted");
    foreach(string text in new[]{Manifest().Replace("howl-sorting-pipes","codaloader"),Manifest().Replace("howl_sorting_pipes","bad"),Manifest().Replace("api/CodaItemFilter.class","../../evil.class"),Manifest().Replace("26.4-snapshot-3","other")}) {
        try{OfficialModsInstaller.ParseManifest(text);throw new Exception("Bad manifest accepted");}catch(InvalidDataException){checks++;}
    }
    // Codex is a required Nightly test mod. Verify the automatic full
    // release-catalog path, minimum loader API, integrity and Stable exclusion.
    {
        const string codexVersion = "0.1.0-dev.2";
        const string codexName = "howl-codex-" + codexVersion + ".jar";
        const string codexTag = "codex-v" + codexVersion;
        const string codexUrl = "https://github.com/HowlingWhispers/HW-Mods/releases/download/"
            + codexTag + "/" + codexName;
        byte[] codex;
        using (var output = new MemoryStream()) {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true)) {
                using (var w = new StreamWriter(zip.CreateEntry("coda.mod.json").Open()))
                    w.Write(JsonSerializer.Serialize(new {
                        schema=1, id="howl_codex", name="H.O.W.L. Codex",
                        version=codexVersion, minecraft="26.4-snapshot-3",
                        entrypoint="dev.howlingwhispers.codex.HowlCodexMod"
                    }));
                zip.CreateEntry("dev/howlingwhispers/codex/HowlCodexMod.class")
                    .Open().Dispose();
            }
            codex = output.ToArray();
        }
        string codexManifest = JsonSerializer.Serialize(new {
            schema=1, minecraft="26.4-snapshot-3", mods=new[] {
                new {
                    slug="howl-codex", modId="howl_codex", name="H.O.W.L. Codex",
                    channel="nightly", minimumLoader="0.0.42",
                    requiredLoaderClass="dev/howlingwhispers/codaloader/api/CodaInventoryPanels.class"
                }
            }
        });
        using var codexHttp = new HttpClient(new Handler(request => {
            string url = request.RequestUri!.ToString();
            if (url == OfficialModsInstaller.ManifestUrl)
                return new StringContent(codexManifest);
            if (url.Contains("HW-Mods/releases?"))
                return new StringContent(JsonSerializer.Serialize(new[] {
                    new {tag_name=codexTag, draft=false, assets=new[] {
                        new {name=codexName,browser_download_url=codexUrl}
                    }}
                }));
            if (url == codexUrl)
                return new ByteArrayContent(codex);
            if (url == codexUrl + ".sha256")
                return new StringContent(Convert.ToHexString(SHA256.HashData(codex))
                    + "  " + codexName);
            throw new Exception("Unexpected Codex Nightly request: " + url);
        }));
        var codexInstaller = new OfficialModsInstaller(codexHttp);
        string codexRoot = Path.Combine(temporary, "codex-nightly");
        Loader("0.0.41");
        try {
            await codexInstaller.EnsureAsync(codexRoot,loader,"nightly",messages.Add,CancellationToken.None);
            throw new Exception("Codex accepted outdated loader");
        } catch (InvalidDataException) { checks++; }
        Loader("0.0.42");
        try {
            await codexInstaller.EnsureAsync(codexRoot,loader,"nightly",messages.Add,CancellationToken.None);
            throw new Exception("Codex accepted missing panel API");
        } catch (InvalidDataException) { checks++; }
        // Match new public 0.0.42 API. Normal local fixtures have no Codex class.
        using (var loadedZip = ZipFile.Open(loader, ZipArchiveMode.Update))
            loadedZip.CreateEntry("dev/howlingwhispers/codaloader/api/CodaInventoryPanels.class");
        await codexInstaller.EnsureAsync(codexRoot,loader,"nightly",messages.Add,CancellationToken.None);
        string codexInstalled = Path.Combine(codexRoot, "mods", codexName);
        Check(File.Exists(codexInstalled) &&
            File.ReadAllBytes(codexInstalled).SequenceEqual(codex),
            "Codex automatically installs in Nightly with verified release bytes");
        Check(codexInstaller.IsRequired("github:howl-codex", "nightly"),
            "Codex flagged required in Nightly");
        Check(!codexInstaller.IsRequired("github:howl-codex", "stable"),
            "Codex must never become required in Stable");
        string codexStable = Path.Combine(temporary, "codex-stable");
        await codexInstaller.EnsureAsync(codexStable,loader,"stable",messages.Add,CancellationToken.None);
        Check(!File.Exists(Path.Combine(codexStable, "mods", codexName)),
            "Stable must not receive Codex automatically");
    }
    Console.WriteLine("PASS: "+checks+" automatic official-mod install/update and preservation checks");
} finally {Directory.Delete(temporary,true);}
internal sealed class Handler(Func<HttpRequestMessage,HttpContent> contents):HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=contents(request)});
}
