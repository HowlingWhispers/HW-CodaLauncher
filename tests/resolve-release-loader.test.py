#!/usr/bin/env python3
"""Offline checks for tests/resolve-release-loader.py (selection and catalog rules only)."""
import json
import pathlib
import subprocess
import sys
import tempfile

SCRIPT = pathlib.Path(__file__).with_name("resolve-release-loader.py")


def release(tag, zip_asset=True, manifest=True, draft=False, host="https://github.com/"):
    assets = []
    if zip_asset:
        assets.append({"name": "CodaLoader-" + tag + "-win64.zip",
                       "browser_download_url": host + "x/" + tag + ".zip"})
    if manifest:
        assets.append({"name": "update-manifest.json",
                       "browser_download_url": host + "x/" + tag + ".json"})
    return {"tag_name": tag, "draft": draft, "assets": assets}


def catalog(minimum, required="dev/howlingwhispers/codaloader/bootstrap/X.class"):
    return {"schema": 1, "mods": [{"modId": "howl_frontier", "minimumLoader": minimum,
                                   "requiredLoaderClass": required}]}


def run(releases, frontier):
    with tempfile.TemporaryDirectory() as temp:
        r = pathlib.Path(temp, "releases.json"); r.write_text(json.dumps(releases))
        c = pathlib.Path(temp, "catalog.json"); c.write_text(json.dumps(frontier))
        done = subprocess.run([sys.executable, "-I", str(SCRIPT), temp, "--releases", str(r),
                               "--catalog", str(c), "--no-download"], capture_output=True, text=True)
        return done.returncode, done.stdout.strip(), done.stderr


releases = [
    release("v0.0.45"),
    release("v0.0.46"),
    release("v0.0.47", draft=True),                    # drafts never ship
    release("v0.0.50", manifest=False),                 # incomplete release is ignored
    release("v0.0.49", host="https://evil.example/"),   # assets must come from github.com
    release("v1.0"),                                    # not vX.Y.Z
    release("frontier-v0.3.2"),
]
code, out, err = run(releases, catalog("0.0.46"))
assert code == 0 and out == "0.0.46", (code, out, err)

code, out, err = run(releases, catalog("0.0.44"))
assert code == 0 and out == "0.0.46", "the gate tests the newest loader players get, not the minimum"

code, out, err = run(releases, catalog("0.0.48"))
assert code != 0 and "older than Frontier's minimumLoader" in err, (code, err)

code, out, err = run(releases, {"schema": 1, "mods": []})
assert code != 0 and "exactly one howl_frontier" in err, (code, err)

code, out, err = run(releases, catalog("latest"))
assert code != 0 and "no valid minimumLoader" in err, (code, err)

code, out, err = run([release("v0.0.46", manifest=False)], catalog("0.0.40"))
assert code != 0 and "no versioned official" in err, (code, err)

print("PASS: release gate picks the newest published loader and enforces Frontier's catalog minimum")
