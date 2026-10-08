# CodaLauncher

Desktop launcher for Howling Whispers Minecraft, pinned to **Minecraft Java 26.4 Snapshot 3**.

## Downloads

Choose the package for your computer from [GitHub Releases](https://github.com/HowlingWhispers/HW-CodaLauncher/releases).

| Computer | Package |
| --- | --- |
| Windows x64 | `win64.zip` |
| Linux x64 | `linux-x64.tar.gz` |
| Linux ARM64 | `linux-arm64.tar.gz` |
| Intel Mac | `macos-x64.zip` |
| Apple Silicon Mac | `macos-arm64.zip` |

Windows: extract the whole ZIP and open CodaLauncher.exe.
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
