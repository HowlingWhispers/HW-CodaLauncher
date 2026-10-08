# CodaLauncher · Coda Edition

A separate, working launcher with Coda’s warmer adventure-desk design. The original repository and launcher have not been changed.

## Download from GitHub

This preview is on the separate `feat/coda-edition-preview` branch under `.coda-edition/`. The original launcher source and build configuration are unchanged. The preview is based on the 0.7.15 code inspected during design work; it does not replace the current main branch’s release.

The **Build Coda Edition Preview** workflow creates Windows and Linux packages on branch pushes. Open the workflow’s successful run, then download the matching artifact. These are CI artifacts, not official releases. Extract the GitHub artifact ZIP first; it contains the portable package named below.

The browser design demo is already committed under `downloads/` and can be downloaded immediately. It uses sample data and cannot install or launch Minecraft.

## Windows installer

On the successful **Build Coda Edition Preview** run, sign into GitHub and download **CodaEdition-Windows-Installer** under Artifacts. Extract that ZIP and run `CodaLauncher-CodaEdition-v0.7.15-coda.1-win64-Setup.exe`. It installs to `%LOCALAPPDATA%/Programs/CodaLauncher.CodaEdition` and creates **CodaLauncher Coda Edition** shortcuts.

The installer has its own application identity and Installed Apps entry. It does not replace or uninstall the original CodaLauncher. Setup installs Microsoft WebView2 if it is missing; the downloaded Microsoft bootstrapper’s Authenticode signature is verified before packaging. The Coda Edition installer itself is unsigned.

## Portable alternatives

- Windows: extract `CodaLauncher-CodaEdition-windows.zip` into a new folder and run `CodaLauncher.CodaEdition.exe`. Keep the package files together. WebView2 is required, as with the original Windows launcher.
- Linux x64: extract `CodaLauncher-CodaEdition-linux-x64.tar.gz` and run `./start.sh` on an X11/XWayland desktop. The .NET runtime is included. Native dependencies are `libx11-6 libice6 libsm6 libfontconfig1`.
- Browser design demo: extract `downloads/CodaLauncher-CodaEdition-browser-demo.zip` and open `index.html`. Or open `web/index.html` from this source folder. This mode uses clearly labelled illustrative data; it cannot install or start Minecraft. Play explains that boundary.

The desktop editions preserve the existing game preparation, verified content installation, mod scanning, official-launcher handoff, local test mode and logs. Minecraft still requires Java 25 or newer and its normal account/asset prerequisites. No game download or live Minecraft playtest was performed while building this edition.

## Resource-pack repair fix (0.7.15-coda.1)

This preview fixes the reported `Loader bundle is missing managed file CodaLoader.jar` error after the HOWL Base resource download. Resource archives now use a dedicated installer instead of the loader-only copy routine. The pinned archive checksum remains enforced; existing player-added files stay in place and replaced resource files are backed up.

Install this build over the previous Coda Edition installation, then retry Play or Install / repair. Existing Coda Edition game data is preserved. No changes were made to the original launcher.

## Explore the design

- Home: one prominent Play action, expandable game details, Coda and news from the desk.
- My Game: mod information; the Windows/browser interface also includes Packs and Art & music shelves.
- Settings: friendly copy and a quieter desk option. In the browser, the quieter setting is remembered locally; native quiet mode lasts for the current session.
- Help: clear recovery actions and copyable diagnostics.
- Click Coda for clipboard antics. Motion respects the browser’s reduced-motion preference.
- The browser demo’s setting changes last for that page session; they never change native launcher settings.

## Kept separate from the original

The executable is `CodaLauncher.CodaEdition`. Game files and settings are under the operating system’s ApplicationData directory in `.howlingwhispers-coda-edition`, without importing or migrating existing worlds. The WebView2 cache and account vault use `HowlingWhispers/CodaLauncher.CodaEdition`.

When you explicitly press Play, the official Minecraft Launcher gets an additional installation named **Howling Whispers | H.O.W.L. · Coda Edition**, with a distinct profile ID and version manifest. It preserves the original H.O.W.L. profile. Select the Coda Edition installation to play this edition.

Launcher self-updates are disabled for this separate edition so a standard release cannot overwrite it. Game-content updates retain the existing verification logic.

## Build from source

Requires .NET 8 SDK. From this folder:

```sh
dotnet run --project Desktop/CodaLauncher.Desktop.csproj -c Release
dotnet publish Desktop/CodaLauncher.Desktop.csproj -c Release -r linux-x64 --self-contained true -o artifacts/linux
```

For Windows (build can be cross-compiled from Linux):

```sh
dotnet publish CodaLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/windows
```

In the prepared cloud workspace, first run `source /workspace/.cloud-setup/activate.sh` to activate the installed .NET SDK.

## Validation

The native Linux build and UI startup were tested under Xvfb. Browser checks exercise navigation, all game shelves, Coda’s responses, quiet mode, settings feedback, the browser launch boundary, profile navigation and mobile layout. The official-profile fixture verifies preservation of the original launcher’s installation, repeat registration and collision protection. Managed-mod fixtures verify no-clobber installation and migration behavior.

The installer workflow validates install, reinstall, the installed WebView2 UI, uninstall and preservation of a simulated original launcher plus world files on a disposable Windows runner. Live Minecraft gameplay remains untested.
