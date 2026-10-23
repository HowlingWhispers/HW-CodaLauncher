#!/usr/bin/env python3
"""Pick and verify the H.O.W.L. loader the release gate tests Frontier against.

It uses the same rule as NightlyRuntimeInstaller.FindReleaseAsync, so the gate
tests the loader players actually receive on Nightly Play:
the highest non-draft vX.Y.Z release that has both CodaLoader-vX.Y.Z-win64.zip
and update-manifest.json on github.com.

It then checks that loader against the live Frontier catalog in HW-Mods
(minimumLoader and requiredLoaderClass), verifies every published checksum,
writes CodaLoader.jar into OUT_DIR and prints the version on stdout.

Usage: resolve-release-loader.py OUT_DIR [--releases FILE] [--catalog FILE] [--no-download]
The optional files replace the live GitHub data, for offline tests.
"""
import hashlib
import json
import pathlib
import subprocess
import sys
import urllib.request
import zipfile

CATALOG_URL = "https://raw.githubusercontent.com/HowlingWhispers/HW-Mods/main/mods/frontier-mods.json"
RELEASES_API = "repos/HowlingWhispers/HW-CodaLoader/releases?per_page=20"


def fail(message):
    print("Release gate: " + message, file=sys.stderr)
    sys.exit(1)


def parse_version(text):
    parts = text.split(".")
    if len(parts) != 3 or not all(p.isdigit() for p in parts):
        return None
    return tuple(int(p) for p in parts)


def fetch(url):
    with urllib.request.urlopen(url, timeout=60) as response:
        return response.read()


def select_release(releases):
    best = None
    for item in releases:
        if item.get("draft"):
            continue
        tag = item.get("tag_name") or ""
        version = parse_version(tag[1:]) if tag.startswith("v") else None
        if version is None:
            continue
        name = "CodaLoader-" + tag + "-win64.zip"
        zip_url = manifest_url = None
        for asset in item.get("assets", []):
            link = asset.get("browser_download_url") or ""
            if not link.startswith("https://github.com/"):
                continue
            if asset.get("name") == name:
                zip_url = link
            if asset.get("name") == "update-manifest.json":
                manifest_url = link
        if zip_url and manifest_url and (best is None or version > best[0]):
            best = (version, tag[1:], name, zip_url, manifest_url)
    if best is None:
        fail("no versioned official H.O.W.L. release with a bundle and manifest was found")
    return best


def main(argv):
    import argparse
    parser = argparse.ArgumentParser(description="Pick and verify the release-gate loader.")
    parser.add_argument("out_dir")
    parser.add_argument("--releases", help="HW-CodaLoader releases JSON instead of the GitHub API")
    parser.add_argument("--catalog", help="Frontier catalog JSON instead of HW-Mods main")
    parser.add_argument("--no-download", action="store_true", help="only select and print the version")
    args = parser.parse_args(argv)
    out = pathlib.Path(args.out_dir)
    download = not args.no_download

    if args.releases:
        releases = json.loads(pathlib.Path(args.releases).read_text())
    else:
        releases = json.loads(subprocess.run(["gh", "api", RELEASES_API], check=True,
                                             capture_output=True, text=True).stdout)
    if args.catalog:
        catalog = json.loads(pathlib.Path(args.catalog).read_text())
    else:
        catalog = json.loads(fetch(CATALOG_URL))

    frontier = [m for m in catalog.get("mods", []) if m.get("modId") == "howl_frontier"]
    if len(frontier) != 1:
        fail("the Frontier catalog must declare exactly one howl_frontier entry")
    minimum = parse_version(frontier[0].get("minimumLoader", ""))
    required_class = frontier[0].get("requiredLoaderClass") or ""
    if minimum is None or not required_class.endswith(".class"):
        fail("the Frontier catalog entry has no valid minimumLoader/requiredLoaderClass")

    version, text, name, zip_url, manifest_url = select_release(releases)
    if version < minimum:
        fail("newest published loader v%s is older than Frontier's minimumLoader %s"
             % (text, frontier[0]["minimumLoader"]))
    if not download:
        print(text)
        return

    out.mkdir(parents=True, exist_ok=True)
    manifest = json.loads(fetch(manifest_url))
    if manifest.get("version") != text or manifest.get("bundle") != name:
        fail("update-manifest.json does not describe v%s / %s" % (text, name))
    archive = fetch(zip_url)
    if hashlib.sha256(archive).hexdigest() != manifest.get("sha256"):
        fail("loader bundle SHA-256 does not match its manifest")
    (out / name).write_bytes(archive)
    with zipfile.ZipFile(out / name) as bundle:
        jar = bundle.read("CodaLoader.jar")
    if hashlib.sha256(jar).hexdigest() != manifest.get("files", {}).get("CodaLoader.jar"):
        fail("CodaLoader.jar SHA-256 does not match its manifest")
    (out / "CodaLoader.jar").write_bytes(jar)
    with zipfile.ZipFile(out / "CodaLoader.jar") as loader:
        if required_class not in loader.namelist():
            fail("loader v%s lacks Frontier's required class %s" % (text, required_class))
    print("Release gate: Frontier needs H.O.W.L. %s+; testing published v%s"
          % (frontier[0]["minimumLoader"], text), file=sys.stderr)
    print(text)


if __name__ == "__main__":
    main(sys.argv[1:])
