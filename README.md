# CodaLauncher

## Optional and recommended mods (v0.7.16)

Only H.O.W.L. and its required resource pack are automatic. The Mods tab
offers **Install / Check for Update / Uninstall** for optional BuildCraft CML,
Coda Wolf Companion and HW Essentials. Nightly Quiet Underground is also
optional in Packs. Normal PLAY and Install/Repair never silently install or
reactivate those add-ons. Uninstall only removes a checksum-owned add-on
file and marker; manually edited mods, worlds and configs are preserved.
**Back up your worlds before opening them without a gameplay mod**, since
Minecraft can change block or item references when a mod is missing.

Nightly updates still use a combined GitHub ZIP as the transport for the
required H.O.W.L. loader. Its optional bundled files are **not installed**
except upon an explicit player action. BuildCraft tracks its own installed
Nightly tag independently from the loader build version.


## GitHub-hosted Coda News (independent of launcher updates)

The player-facing News panel reads the public `web/news.json` file on the
`main` branch of `HowlingWhispers/HW-CodaLauncher` through
`https://raw.githubusercontent.com/HowlingWhispers/HW-CodaLauncher/main/web/news.json`.
Changes to this file appear in the launcher without publishing a new executable.
The launcher checks at most hourly per running session, stores a validated
copy under its local launcher data folder and falls back to that cache or the
news included with the executable if GitHub is unreachable. The GitHub API is
not used for articles. Server `/api/feed` remains responsible for pack and
resource update metadata, cached separately (30 minutes online, 2 minutes
after a failure). Keep articles player-facing and nontechnical.


## CodaLauncher 0.7.12: reliable Nightly updates during GitHub API rate limits

GitHub's anonymous API has a shared request quota. When GitHub REST release discovery returns HTTP 403/429, both Coda Wolf and BuildCraft Nightly automatically discover published release tags through GitHub's public Atom feed instead. Downloaded official assets still require valid SHA-256 sidecars, and user-edited files cannot be overwritten. Previously verified local mods remain available when *both* discovery routes are unavailable. The background self-updater backs off for an hour after GitHub rate limiting, avoiding unnecessary repeated requests. No changes to the loader, worlds or Stable channel.


## CodaLauncher 0.7.11: GitHub connection timeout hotfix

If GitHub is unreachable while preparing Nightly, H.O.W.L. reuses existing checksum-verified BuildCraft, Quiet Underground, and Coda Wolf content in the Nightly profile. A new or damaged installation still requires a working connection. The launcher logs the cache fallback and checks for updates again on the next Play. A failed GitHub status refresh after starting the game no longer reports a failed launch. No changes are made to player worlds, stable profiles, or the mod loader.


## CodaLauncher 0.7.10: Coda Wolf automatic Nightly delivery

With **Settings > Nightly** and **Local Test Mode**, **PLAY** and **Install/Repair** now discover the latest `nightly-codawolf-*` release from the HW-Mods GitHub release feed and checksum-verify the `coda-wolf-0.1.0-dev.jar` before copying it into the selected Nightly Minecraft mods directory. It appears in Mods upon refresh. No manual ZIP installation required, and the normal Stable installation stays unchanged. Existing modified or duplicate Coda Wolf jars are protected instead of overwritten. This is a **prototype**: the wolf's runtime entity hooks need a disposable-world playtest before gameplay claims can be made.

Desktop launcher for **H.O.W.L. (Howling Open Works Loader)** and Howling Whispers Minecraft, pinned to **Minecraft Java 26.4 Snapshot 3**.

## CodaLauncher 0.7.6: one active mod folder

Fresh Windows installations use `%APPDATA%\.howlingwhispers\minecraft\mods`.
Existing profiles with the historic `.howlingshispers` typo continue using
their original root until an explicit data-root migration is available,
preventing silent loss of access to worlds, profiles, or settings.
Each channel scans exactly one mods folder: Stable under its game profile
and Nightly under its separate nightly/minecraft profile. Legacy
`loader/run/mods` JARs migrate without overwriting conflicts, then the
entire old folder is preserved in a named backup. Install/update code no
longer creates the duplicate path. The Mod Drawer opens the active folder
directly through OPEN MODS FOLDER. Real Snapshot 3 play must still be tested.

**Windows builds and installer smoke tests passed**, including preservation
of player data. Linux/macOS stable builds and launcher unit tests passed.

## CodaLauncher 0.7.5: opt-in Nightly (Windows)

A new **Stable / Nightly** selector appears under Settings. Stable remains the
default after install and for all existing users. Saving Nightly requires
**Local Test Mode**, as it is for experimental single-player gameplay only.

- Clicking Play in Nightly fetches the newest public, checksum-verified
  `nightly-buildcraft-*` prerelease from
  [HW-Mods](https://github.com/HowlingWhispers/HW-Mods/releases).
- Loader code goes in `%APPDATA%\\.howlingshispers\\nightly\\loader`. Mods
  and Minecraft world saves go in
  `%APPDATA%\\.howlingshispers\\nightly\\minecraft`.
  The existing Stable loader, resource packs, mods, accounts and saves are
  not replaced or used as experimental saves.
- Before running it downloads the latest nightly release, checks its published
  SHA-256 checksum, and refuses missing or unexpected package contents.
  If no published nightly is available, Play shows an explicit error rather
  than silently falling back to Stable.
- First experimental BuildCraft gameplay: two standard single chests/barrels
  separated by a straight 1-16 block line of **vanilla glass** used as
  temporary pipes. Use `/buildcraft pulse x1 y1 z1 x2 y2 z2` to move up to
  16 items in the integrated single-player server. Make a new throwaway world.
  Native BuildCraft blocks and engines are still being ported.
- Switching back to Stable is a Settings change. No automatic promotion of
  nightly worlds to Stable. CodaLauncher itself still uses stable self-updates.
- Nightly channel installation through the app is initially **Windows-only**.
  Linux/macOS builds remain on the existing stable launcher path until native
  desktop parity is implemented.

The nightly package is test code. Passing automated tests does not establish
a successful live Minecraft Snapshot 3 playtest. **Do not use valuable worlds**.

## H.O.W.L. 0.7.4 updater hotfix

Fixes the Windows reboot/update loop in 0.7.3: the Windows app was still
reporting itself as 0.7.2 after a successful update. The displayed and checked
version now comes from the executable's assembly metadata. This release keeps
the H.O.W.L. branding introduced in 0.7.3 and requires no reset of Minecraft
data, mod profiles, or user settings.

### Included features

CodaLauncher keeps its name. The loader and player-facing mod platform now use
H.O.W.L.; the official Minecraft installation is renamed in place. Legacy
profile IDs, loader filenames, CML pack IDs, asset names, Java API packages,
settings and save directories remain compatible. Custom official-launcher JVM
settings are preserved. Both launch modes and Copy All Logs remain available.

Linux/macOS Settings now show save confirmation or an error next to Save.
Windows retains its existing save feedback. Live Snapshot 3 launch and menu
verification remains outstanding; this is an early-access prerelease.

## Downloads

Choose the package for your computer from [GitHub Releases](https://github.com/HowlingWhispers/HW-CodaLauncher/releases).

| Computer | Package |
| --- | --- |
| Windows x64 | `win64-Setup.exe` (recommended), `win64.zip` (portable) |
| Linux x64 | `linux-x64.tar.gz` |
| Linux ARM64 | `linux-arm64.tar.gz` |
| Intel Mac | `macos-x64.zip` |
| Apple Silicon Mac | `macos-arm64.zip` |

Windows: download and run `CodaLauncher-v0.7.4-win64-Setup.exe`. Setup installs to `%LOCALAPPDATA%\Programs\CodaLauncher`, adds a Start Menu shortcut and offers a desktop shortcut (selected by default). Open CodaLauncher when Setup finishes. Administrator access is not required. If WebView2 is missing, Setup installs it from Microsoft; this step needs internet access.

For portable Windows use, extract the whole ZIP and open CodaLauncher.exe. Keep the ZIP available for the existing self-updater; Setup is the player-facing download.

Uninstall through Windows Settings → Installed Apps. Worlds, Minecraft files, settings and WebView2 are preserved. Installing a later Setup over the existing installation uses the same application identity and location.
Linux: extract the archive and run `./start.sh`.
macOS: extract the ZIP, move CodaLauncher.app to Applications and open it.

Keep package contents together. The .NET runtime is bundled; the Linux/macOS editions do not need WebView2. Minecraft still needs Java 25 or newer available as `java` on PATH, with the correct CPU architecture. GUI-launched macOS apps may have a different PATH from Terminal; install Java so `/usr/bin/java` resolves the intended JVM.

Linux requires a desktop with X11 or XWayland, fontconfig, libX11, libICE and libSM. Debian/Ubuntu package names: `libx11-6 libice6 libsm6 libfontconfig1`. A headless Debian server cannot show the launcher.

macOS builds are ad-hoc signed and are not Apple-notarized. macOS may require approval through its normal Open Anyway flow. Do not globally disable Gatekeeper.

## Official Minecraft Launcher and Local Test Mode (in development)

The upcoming launcher update uses the **official Minecraft Launcher** for
Microsoft authentication and Minecraft Java ownership. Players do not enter a
Microsoft password or OAuth token into CodaLauncher.

1. Install and sign in to the official Minecraft Launcher once.
2. **Close it** before clicking Play in CodaLauncher; otherwise Minecraft
   Launcher may overwrite the installation-profile file on exit.
3. CodaLauncher checks the published CodaLoader and mods, registers its own
   `Howling Whispers | H.O.W.L.` installation in the official launcher,
   and asks the official launcher to open.
4. Select the **Howling Whispers | H.O.W.L.** installation (enable modded
   installations if necessary), then click Play in Minecraft Launcher.
5. The official launcher supplies your Minecraft credentials. CodaLoader's
   Java agent supplies the HW menus, game hooks, and managed mods.

The custom version manifest is based on an official Mojang version JSON
verified by its published SHA-1. CodaLauncher writes only its own profile under
the existing `.minecraft` launcher-profile file, saves a backup and preserves
all other installations, accounts, and worlds. The HW game directory remains
the isolated `.howlingshispers/minecraft` folder.

**Settings → Local Test Mode:** opt in to directly launch local singleplayer
using the original `CodaPlayer` identity without Microsoft sign-in. This is
an **unverified development identity** and cannot authorize friends, shared
worlds, remote bank/storage, or online Howling Whispers services. It preserves
the old `CodaPlayer` UUID so local saves don't appear to change owners. The
initial game/loader download requires internet access; previously downloaded
assets can be reused. Turn the switch off to return to the official launcher.

The old Microsoft account implementation remains in source code but its
CodaLauncher controls are paused. Azure app registration can be completed in a
future version without affecting this launch path. The official-profile
integration is still awaiting a live game test on Minecraft 26.4 Snapshot 3.

## Required first-party mods

Official H.O.W.L. builds do not offer optional first-party gameplay mods. PLAY installs and checks all officially bundled modules. Development-only projects such as BuildCraft CML and HW Quiet Underground are not released player mods. Once Quiet Underground passes worldgen validation, its rules belong in required automatic new-world content, not an optional data-pack selector; existing saves must be protected.

The current Mod Drawer displays installed JARs, not unreleased prototypes or world data packs.

## Play and mods

PLAY checks the current CodaLoader release, prepares HOWL Base Resources and installs/updates HW Essentials in the active Minecraft profile before starting the game. Mod versions are read from `coda.mod.json` in the active profile's `mods` folder.

Known official mod files share CodaLoader's ownership marker. Manually modified conflicting files are preserved and reported. Player worlds, homes, settings and custom music are stored outside the launcher application folder.

The current loader release ZIP is labeled win64 because it also includes a Windows BAT file. Its Java loader and bundled mods are architecture-independent; Linux/macOS launch CodaLoader.jar directly through Java and do not execute that BAT file. Minecraft libraries and native files are selected by CodaLoader for the running OS.

## Interface and updates

Windows keeps the WPF/WebView2 interface and verified staged self-updater.

Linux/macOS use Avalonia with Home, Mods, Settings and Logs. All editions reuse the feed client, managed installer, mod scanner and Java launch service. The approved Coda portrait is bundled unchanged.

Linux/macOS check for launcher updates while open, every five minutes, on activation and on Refresh. The update button opens the matching platform download. Replace the launcher manually for this first desktop edition. This differs from Windows' automatic staged replacement.

## Data and settings

Data is stored below the OS ApplicationData directory in `.howlingshispers`: Windows normally uses %APPDATA%, Linux normally ~/.config, macOS normally ~/Library/Application Support. Settings live in `launcher/settings.json`; Minecraft lives in `minecraft/`.

Default feed: https://thehowlingwhispers.com/launcher. The client appends `api/feed` and offers offline news fallback. HOWL Base Resources downloads remain SHA-256 checked.

## Build

Requires .NET 8 SDK.

Windows:

```sh
dotnet build CodaLauncher.csproj -c Release
```

Linux/macOS:

```sh
dotnet run --project Desktop/CodaLauncher.Desktop.csproj
dotnet publish Desktop/CodaLauncher.Desktop.csproj -c Release -r linux-x64 --self-contained true
```

Other runtime targets: `linux-arm64`, `osx-x64`, `osx-arm64`.

CI compiles Windows/Linux/macOS, runs managed mod installation tests on all build hosts, packages all five platforms and opens the Linux x64 UI under Xvfb. Cross-compiled ARM packages and live Minecraft gameplay on Linux/macOS still need real-device testing. Release assets and tags are immutable.

## Windows installer development

On Windows with Inno Setup 6 and .NET 8 installed:

```powershell
dotnet publish CodaLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o dist/payload
./packaging/windows/build-installer.ps1 -Version 0.7.4
```

The build script downloads Microsoft's WebView2 bootstrapper and verifies its Authenticode signature before embedding it. Setup and shortcut icons use the approved bundled Coda portrait. CI runs `test-installer.ps1` on a clean Windows runner to check install/reinstall, shortcut targets, Installed Apps registration, actual WebView2 UI startup, uninstall and saved-world preservation. Run that test only on a disposable Windows environment. The installer itself is currently unsigned.

Landing-page Windows download buttons should link directly to the release's `win64-Setup.exe`; the update manifest continues pointing to `win64.zip`. Do not advertise automatic Java installation: Java management is not included in this release.

## Account verification in development

The source now extends **Profile** with Microsoft sign-in, Minecraft Java ownership verification, sign-out, and explicit offline selection. This is not in the published 0.7.4 installer yet. Live sign-in awaits CodaLauncher's own registered Microsoft application ID and API access. See [authentication setup](docs/microsoft-authentication.md) for configuration, storage behavior and release prerequisites.
