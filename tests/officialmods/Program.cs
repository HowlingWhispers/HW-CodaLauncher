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
// Optional release acceptance mode: exercise the production automatic installer with actual published files.
// Only a newly allocated disposable profile is used; never accept a user's game directory.
if(args.Length==2 && args[0]=="--published-frontier") {
    string root=Path.Combine(Path.GetTempPath(),"howl-published-frontier-"+Guid.NewGuid().ToString("N"));
    string game=PlayProfiles.NightlyGameRoot(root,"frontier");
    try {
        using var client=new HttpClient(new PublishedApiAuthentication()) { Timeout=TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CodaLauncher-Frontier-Release-Acceptance/0.7.32");
        var acceptance=new OfficialModsInstaller(client);acceptance.SetMode("frontier");PlayProfiles.Prepare(game,"frontier");
        await acceptance.EnsureAsync(game,Path.GetFullPath(args[1]),"nightly",Console.WriteLine,CancellationToken.None);
        string installed=Directory.EnumerateFiles(Path.Combine(game,"mods"),"*.jar").Single();
        using(var z=ZipFile.OpenRead(installed)) {
            using var input=z.GetEntry("coda.mod.json")!.Open();using var metadata=JsonDocument.Parse(input);
            Check(metadata.RootElement.GetProperty("id").GetString()=="howl_frontier","Published Frontier identity");
            Check(metadata.RootElement.GetProperty("minecraft").GetString()=="26.4-snapshot-3","Published Frontier Minecraft target");
            Check(z.GetEntry("dev/howlingwhispers/frontier/FrontierMod.class")!=null,"Published Frontier entrypoint");
        }
        Check(!Directory.Exists(PlayProfiles.NightlyGameRoot(root,"classic")),"Actual published installation leaves Classic absent and untouched");
        byte[] verified=File.ReadAllBytes(installed);File.Delete(installed);
        await acceptance.EnsureAsync(game,Path.GetFullPath(args[1]),"nightly",Console.WriteLine,CancellationToken.None);
        Check(File.ReadAllBytes(installed).SequenceEqual(verified),"Actual published missing JAR is automatically repaired");
        Console.WriteLine("PASS: actual published Frontier automatically installed, checksum verified and repaired in its isolated Nightly profile");
    } finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    return;
}
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
        string codexVersion = "0.1.0-dev.2";
        string codexName = "howl-codex-" + codexVersion + ".jar";
        string codexTag = "codex-v" + codexVersion;
        string codexUrl = "https://github.com/HowlingWhispers/HW-Mods/releases/download/"
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
        string previousCodexTag = codexTag;
        string previousCodexName = codexName;
        string previousCodexUrl = codexUrl;
        byte[] previousCodex = codex;

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
            if (url.Contains("HW-CodaLoader/releases?"))
                return new StringContent("[]");
            if (url.Contains("HW-Mods/releases?"))
                return new StringContent(JsonSerializer.Serialize(new[] {
                    new {tag_name=previousCodexTag, draft=false, assets=new[] {
                        new {name=previousCodexName,browser_download_url=previousCodexUrl}
                    }},
                    new {tag_name=codexTag, draft=false, assets=new[] {
                        new {name=codexName,browser_download_url=codexUrl}
                    }}
                }));
            if (url == codexUrl)
                return new ByteArrayContent(codex);
            if (url == previousCodexUrl)
                return new ByteArrayContent(previousCodex);
            if (url == codexUrl + ".sha256")
                return new StringContent(Convert.ToHexString(SHA256.HashData(codex))
                    + "  " + codexName);
            if (url == previousCodexUrl + ".sha256")
                return new StringContent(Convert.ToHexString(SHA256.HashData(previousCodex))
                    + "  " + previousCodexName);
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
        // A real published Codex update must replace the old managed JAR
        // even when the H.O.W.L. runtime stays at 0.0.42 and the launcher
        // has not restarted or changed its mod-catalog instance.
        codexVersion = "0.1.0-dev.3";
        codexName = "howl-codex-" + codexVersion + ".jar";
        codexTag = "codex-v" + codexVersion;
        codexUrl = "https://github.com/HowlingWhispers/HW-Mods/releases/download/"
            + codexTag + "/" + codexName;
        using (var next = new MemoryStream()) {
            using (var zip = new ZipArchive(next, ZipArchiveMode.Create, true)) {
                using (var writer = new StreamWriter(zip.CreateEntry("coda.mod.json").Open()))
                    writer.Write(JsonSerializer.Serialize(new {
                        schema=1, id="howl_codex", name="H.O.W.L. Codex",
                        version=codexVersion, minecraft="26.4-snapshot-3",
                        entrypoint="dev.howlingwhispers.codex.HowlCodexMod"
                    }));
                zip.CreateEntry("dev/howlingwhispers/codex/HowlCodexMod.class").Open().Dispose();
            }
            codex = next.ToArray();
        }
        await codexInstaller.EnsureAsync(codexRoot,loader,"nightly",
                messages.Add,CancellationToken.None);
        string upgraded = Path.Combine(codexRoot, "mods", codexName);
        Check(File.Exists(upgraded) && File.ReadAllBytes(upgraded).SequenceEqual(codex),
            "Codex 0.1.0-dev.3 installed automatically while loader remains 0.0.42");
        Check(!File.Exists(codexInstalled),
            "Old checksum-owned Codex 0.1.0-dev.2 must be retired");
        Check(messages.Any(m => m.Contains("codex-v0.1.0-dev.3")),
            "Nightly logs must report the newly installed Codex release");

        Check(codexInstaller.IsRequired("github:howl-codex", "nightly"),
            "Codex flagged required in Nightly");
        Check(!codexInstaller.IsRequired("github:howl-codex", "stable"),
            "Codex must never become required in Stable");
        string codexStable = Path.Combine(temporary, "codex-stable");
        await codexInstaller.EnsureAsync(codexStable,loader,"stable",messages.Add,CancellationToken.None);
        Check(!File.Exists(Path.Combine(codexStable, "mods", codexName)),
            "Stable must not receive Codex automatically");
    }
    // Frontier uses its own required catalog and profile; older clients keep the original catalog.
    {
        string classicRoot = PlayProfiles.NightlyGameRoot(Path.Combine(temporary,"mode-isolation"), "classic");
        string frontierRoot = PlayProfiles.NightlyGameRoot(Path.Combine(temporary,"mode-isolation"), "frontier");
        Check(classicRoot == Path.Combine(temporary, "mode-isolation", "nightly", "minecraft"), "Classic retains the historic save path");
        Check(frontierRoot != classicRoot && !frontierRoot.StartsWith(classicRoot + Path.DirectorySeparatorChar), "Frontier saves/mods/config are isolated");
        try { PlayProfiles.Validate("frontier", "stable"); throw new Exception("Frontier Stable accepted"); } catch(InvalidDataException) { checks++; }
        try { PlayProfiles.Validate("../classic", "nightly"); throw new Exception("Path mode accepted"); } catch(InvalidDataException) { checks++; }
        Check(!PlayProfiles.CatalogVisible("classic","github:howl-frontier"), "Frontier is not advertised as an optional Classic add-on");
        Check(!PlayProfiles.CatalogVisible("frontier","github:howl-sorting-pipes") && PlayProfiles.CatalogVisible("frontier","github:howl-frontier"), "Frontier catalog shows only its intended gameplay bundle");
        PlayProfiles.Prepare(frontierRoot, "frontier");
        Check(File.ReadAllText(Path.Combine(frontierRoot,"config","howl-play-mode.txt")).Trim()=="frontier", "Frontier enabled only in its own profile");
        Check(!Directory.Exists(classicRoot), "Choosing Frontier leaves Classic files untouched");
        string frontierVersion = "0.1.0-dev.1";
        byte[] FrontierJar() {
            using var bytes = new MemoryStream();
            using(var z = new ZipArchive(bytes, ZipArchiveMode.Create, true)) {
                using(var w = new StreamWriter(z.CreateEntry("coda.mod.json").Open())) w.Write(JsonSerializer.Serialize(new {
                    schema=1,id="howl_frontier",name="H.O.W.L. Frontier",version=frontierVersion,minecraft="26.4-snapshot-3",entrypoint="dev.howlingwhispers.frontier.FrontierMod"
                }));
                z.CreateEntry("dev/howlingwhispers/frontier/FrontierMod.class").Open().Dispose();
            } return bytes.ToArray();
        }
        byte[] candidate = FrontierJar(); bool badFrontierHash=false;
        using var frontierHttp = new HttpClient(new Handler(request => {
            string url=request.RequestUri!.ToString();
            if(url==OfficialModsInstaller.FrontierManifestUrl) return new StringContent(JsonSerializer.Serialize(new {
                schema=1,minecraft="26.4-snapshot-3",mods=new[]{new {
                    slug="howl-frontier",modId="howl_frontier",name="H.O.W.L. Frontier",channel="nightly",minimumLoader="0.0.43",
                    requiredLoaderClass="dev/howlingwhispers/codaloader/api/CodaLivingWorld.class"
                }}
            }));
            string tag="frontier-v"+frontierVersion, filename="howl-frontier-"+frontierVersion+".jar";
            string download="https://github.com/HowlingWhispers/HW-Mods/releases/download/"+tag+"/"+filename;
            if(url.Contains("HW-CodaLoader/releases?"))return new StringContent("[]");
            if(url.Contains("HW-Mods/releases?"))return new StringContent(JsonSerializer.Serialize(new[]{new {tag_name=tag,draft=false,assets=new[]{new {name=filename,browser_download_url=download}}}}));
            if(url.EndsWith(".sha256"))return new StringContent((badFrontierHash?new string('0',64):Convert.ToHexString(SHA256.HashData(candidate)))+"  "+filename);
            if(url==download)return new ByteArrayContent(candidate);
            throw new Exception("Unexpected Frontier request: "+url);
        }));
        var frontierInstaller=new OfficialModsInstaller(frontierHttp);frontierInstaller.SetMode("frontier");
        Loader("0.0.42");
        try { await frontierInstaller.EnsureAsync(frontierRoot,loader,"nightly",messages.Add,CancellationToken.None);throw new Exception("Old Frontier loader accepted"); } catch(InvalidDataException) { checks++; }
        Loader("0.0.43");
        using(var zip=ZipFile.Open(loader,ZipArchiveMode.Update))zip.CreateEntry("dev/howlingwhispers/codaloader/api/CodaLivingWorld.class");
        await frontierInstaller.EnsureAsync(frontierRoot,loader,"nightly",messages.Add,CancellationToken.None);
        Check(File.Exists(Path.Combine(frontierRoot,"mods","howl-frontier-0.1.0-dev.1.jar")), "Nightly Play automatically installs the Frontier mod");
        Check(!Directory.Exists(classicRoot), "Frontier installation never populates Classic");
        Check(frontierInstaller.IsRequired("github:howl-frontier","nightly") && !frontierInstaller.IsRequired("github:howl-sorting-pipes","nightly"),"Frontier has its own complete required mod set");
        frontierVersion="0.1.0-dev.2";candidate=FrontierJar();
        await frontierInstaller.EnsureAsync(frontierRoot,loader,"nightly",messages.Add,CancellationToken.None);
        string updated=Path.Combine(frontierRoot,"mods","howl-frontier-0.1.0-dev.2.jar");
        Check(File.Exists(updated)&&!File.Exists(Path.Combine(frontierRoot,"mods","howl-frontier-0.1.0-dev.1.jar")), "Automatic Frontier upgrade retires only the checksum-owned previous JAR");
        frontierVersion="0.1.0-dev.3";candidate=FrontierJar();badFrontierHash=true;
        try { await frontierInstaller.EnsureAsync(frontierRoot,loader,"nightly",messages.Add,CancellationToken.None);throw new Exception("Corrupt Frontier accepted"); } catch(InvalidDataException) { checks++; }
        Check(File.Exists(updated),"Failed Frontier update preserves the previous verified install");
        PlayProfiles.Select("classic","nightly");frontierInstaller.SetMode("classic");
        Check(!frontierInstaller.IsRequired("github:howl-frontier","nightly"),"Switching modes resets required catalog state");
    }
    Console.WriteLine("PASS: "+checks+" automatic official-mod install/update and preservation checks");
} finally {Directory.Delete(temporary,true);}
internal sealed class Handler(Func<HttpRequestMessage,HttpContent> contents):HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=contents(request)});
}

internal sealed class PublishedApiAuthentication : DelegatingHandler {
    internal PublishedApiAuthentication() : base(new HttpClientHandler()) { }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
        string? token=Environment.GetEnvironmentVariable("GH_TOKEN");
        if(request.RequestUri?.Host=="api.github.com" && !string.IsNullOrEmpty(token))
            request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
        return base.SendAsync(request,ct);
    }
}
