# CodaLauncher

Desktop launcher for Howling Whispers Minecraft, pinned to **Minecraft Java 26.4 Snapshot 3**.

## Downloads

Choose the package for your computer from [GitHub Releases](https://github.com/HowlingWhispers/HW-CodaLauncher/releases).

| Computer | Package |
| --- | --- |
| Windows x64 | `win64-Setup.exe` (recommended), `win64.zip` (portable) |
| Linux x64 | `linux-x64.tar.gz` |
| Linux ARM64 | `linux-arm64.tar.gz` |
| Intel Mac | `macos-x64.zip` |
| Apple Silicon Mac | `macos-arm64.zip` |

Windows: download and run `CodaLauncher-v0.7.0-win64-Setup.exe`. Setup installs to `%LOCALAPPDATA%\Programs\CodaLauncher`, adds a Start Menu shortcut and offers a desktop shortcut (selected by default). Open CodaLauncher when Setup finishes. Administrator access is not required. If WebView2 is missing, Setup installs it from Microsoft; this step needs internet access.

For portable Windows use, extract the whole ZIP and open CodaLauncher.exe. Keep the ZIP available for the existing self-updater; Setup is the player-facing download.

Uninstall through Windows Settings → Installed Apps. Worlds, Minecraft files, settings and WebView2 are preserved. Installing a later Setup over the existing installation uses the same application identity and location.
Linux: extract the archive and run `./start.sh`.
macOS: extract the ZIP, move CodaLauncher.app to Applications and open it.

Keep package contents together. The .NET runtime is bundled; the Linux/macOS editions do not need WebView2. Minecraft still needs Java 25 or newer available as `java` on PATH, with the correct CPU architecture. GUI-launched macOS apps may have a different PATH from Terminal; install Java so `/usr/bin/java` resolves the intended JVM.

Linux requires a desktop with X11 or XWayland, fontconfig, libX11, libICE and libSM. Debian/Ubuntu package names: `libx11-6 libice6 libsm6 libfontconfig1`. A headless Debian server cannot show the launcher.

macOS builds are ad-hoc signed and are not Apple-notarized. macOS may require approval through its normal Open Anyway flow. Do not globally disable Gatekeeper.

## Play and mods

PLAY checks the current CodaLoader release, prepares CML Base Resources and installs/updates HW Essentials in the active Minecraft profile before starting the game. Mod versions are read from `coda.mod.json` in the active profile's `mods` folder.

Known official mod files share CodaLoader's ownership marker. Manually modified conflicting files are preserved and reported. Player worlds, homes, settings and custom music are stored outside the launcher application folder.

The current loader release ZIP is labeled win64 because it also includes a Windows BAT file. Its Java loader and bundled mods are architecture-independent; Linux/macOS launch CodaLoader.jar directly through Java and do not execute that BAT file. Minecraft libraries and native files are selected by CodaLoader for the running OS.

## Interface and updates

Windows keeps the WPF/WebView2 interface and verified staged self-updater.

Linux/macOS use Avalonia with Home, Mods, Settings and Logs. All editions reuse the feed client, managed installer, mod scanner and Java launch service. The approved Coda portrait is bundled unchanged.

Linux/macOS check for launcher updates while open, every five minutes, on activation and on Refresh. The update button opens the matching platform download. Replace the launcher manually for this first desktop edition. This differs from Windows' automatic staged replacement.

## Data and settings

Data is stored below the OS ApplicationData directory in `.howlingshispers`: Windows normally uses %APPDATA%, Linux normally ~/.config, macOS normally ~/Library/Application Support. Settings live in `launcher/settings.json`; Minecraft lives in `minecraft/`.

Default feed: https://thehowlingwhispers.com/launcher. The client appends `api/feed` and offers offline news fallback. CML Base Resources downloads remain SHA-256 checked.

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
./packaging/windows/build-installer.ps1 -Version 0.7.0
```

The build script downloads Microsoft's WebView2 bootstrapper and verifies its Authenticode signature before embedding it. Setup and shortcut icons use the approved bundled Coda portrait. CI runs `test-installer.ps1` on a clean Windows runner to check install/reinstall, shortcut targets, Installed Apps registration, actual WebView2 UI startup, uninstall and saved-world preservation. Run that test only on a disposable Windows environment. The installer itself is currently unsigned.

Landing-page Windows download buttons should link directly to the release's `win64-Setup.exe`; the update manifest continues pointing to `win64.zip`. Do not advertise automatic Java installation: Java management is not included in this release.
