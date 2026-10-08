using System.Text.Json;
using HowlingWhispers.CodaLauncher;

const string package = "HOWL-BuildCraft-Singleplayer-Playtest.zip";
const string sha = package + ".sha256";
const string prefix = "nightly-buildcraft-";

static string Release(string tag, string published, bool complete = true,
    bool prerelease = true, bool draft = false, string host = "github.com")
{
    string asset(string filename) =>
        $$"""{"name":"{{filename}}","browser_download_url":"https://{{host}}/HowlingWhispers/HW-Mods/releases/download/{{tag}}/{{filename}}"}""";
    return $$"""{"tag_name":"{{tag}}","published_at":"{{published}}","prerelease":{{prerelease.ToString().ToLowerInvariant()}},"draft":{{draft.ToString().ToLowerInvariant()}},"assets":[{{asset(package)}}{{(complete ? "," + asset(sha) : "")}}]}""";
}

static NightlyReleaseSelector.Release? Select(string json)
{
    using var document = JsonDocument.Parse(json);
    return NightlyReleaseSelector.SelectNewest(document.RootElement, prefix, package, sha);
}

var old = Release("nightly-buildcraft-20261008-f642d3a673", "2026-10-08T05:59:31Z");
var newPack = Release("nightly-buildcraft-20261008-345b4cfef0", "2026-10-08T06:22:26Z");
var before = Release("nightly-buildcraft-20261008-e8224596f1", "2026-10-08T05:54:06Z");

void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

// This is the EXACT release-order bug reported by the player on Oct 8:
// GitHub listed f642 FIRST and the newer 345b fourth or fifth.
var oldestFirst = Select("[" + string.Join(",", old, before, newPack) + "]");
Assert(oldestFirst?.Tag == "nightly-buildcraft-20261008-345b4cfef0",
    "Must select new Quiet Underground nightly even when old release appears first");
var newestFirst = Select("[" + string.Join(",", newPack, old, before) + "]");
Assert(newestFirst?.Tag == oldestFirst?.Tag, "Order of GitHub JSON must not affect selection");
Assert(oldestFirst?.PackageUrl.AbsolutePath.Contains("/nightly-buildcraft-20261008-345b4cfef0/") == true,
    "Selected package URL must point to newest tag, not merely report newest version");
Assert(oldestFirst?.ChecksumUrl.ToString().Contains(".sha256") == true,
    "Selected release must have matching checksum asset");

Assert(Select("[" + Release("nightly-buildcraft-20261008-no-sha", "2026-10-08T07:00:00Z", false)
    + "," + newPack + "]")?.Tag == newestFirst?.Tag,
    "An incomplete newer release must not be used");
Assert(Select("[" + Release("nightly-buildcraft-20261008-draft", "2026-10-08T08:00:00Z", draft:true)
    + "," + newPack + "]")?.Tag == newestFirst?.Tag, "Draft release must be ignored");
Assert(Select("[" + Release("nightly-buildcraft-20261008-untrusted", "2026-10-08T08:00:00Z", host:"evil.example")
    + "," + newPack + "]")?.Tag == newestFirst?.Tag, "Release URL outside HW-Mods must be ignored");
Assert(Select("[" + Release("nightly-buildcraft-20261008-stable", "2026-10-08T08:00:00Z", prerelease:false)
    + "," + newPack + "]")?.Tag == newestFirst?.Tag, "Only opt-in prereleases are valid");
Assert(Select("[]") is null, "Empty release result must fail safely");

var atom = $"""
    <feed xmlns="http://www.w3.org/2005/Atom">
      <entry><updated>2026-10-08T10:00:00Z</updated><link rel="alternate" href="https://github.com/HowlingWhispers/HW-Mods/releases/tag/nightly-buildcraft-20261008-latest"/></entry>
      <entry><updated>2026-10-07T10:00:00Z</updated><link rel="alternate" href="https://github.com/HowlingWhispers/HW-Mods/releases/tag/nightly-buildcraft-20261007-older"/></entry>
      <entry><updated>2026-10-09T10:00:00Z</updated><link rel="alternate" href="https://github.com/HowlingWhispers/HW-Mods/releases/tag/nightly-codawolf-20261009-other"/></entry>
    </feed>
    """;
var atomBuild = NightlyAtomReleaseReader.SelectNewest(atom, prefix, package, sha);
Assert(atomBuild?.Tag == "nightly-buildcraft-20261008-latest",
    "BuildCraft Atom fallback selects newest matching public release");
Assert(atomBuild?.PackageUrl.AbsolutePath.EndsWith("/nightly-buildcraft-20261008-latest/" + package),
    "BuildCraft Atom fallback constructs the exact expected official release asset URL");
Assert(atomBuild?.ChecksumUrl.AbsolutePath.EndsWith(".sha256"),
    "BuildCraft fallback includes official SHA-256 filename");

Console.WriteLine("PASS: newest published Nightly selection, old-first release order, URLs, checksum, draft and host restrictions.");
