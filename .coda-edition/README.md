# CodaLauncher · Coda Edition

A separate, working launcher with Coda’s warmer adventure-desk design. The original repository and launcher have not been changed.

## Download from GitHub

This preview is on the separate `feat/coda-edition-preview` branch under `.coda-edition/`. The original launcher source and build configuration are unchanged. The preview is based on the 0.7.15 code inspected during design work; it does not replace the current main branch’s release.

The **Build Coda Edition Preview** workflow creates Windows and Linux packages on branch pushes. Open the workflow’s successful run, then download the matching artifact. These are CI artifacts, not official releases. Extract the GitHub artifact ZIP first; it contains the portable package named below.

The browser design demo is already committed under `downloads/` and can be downloaded immediately. It uses sample data and cannot install or launch Minecraft.

## Open it

- Windows: extract `CodaLauncher-CodaEdition-windows.zip` into a new folder and run `CodaLauncher.CodaEdition.exe`. Keep the package files together. WebView2 is required, as with the original Windows launcher.
- Linux x64: extract `CodaLauncher-CodaEdition-linux-x64.tar.gz` and run `./start.sh` on an X11/XWayland desktop. The .NET runtime is included. Native dependencies are `libx11-6 libice6 libsm6 libfontconfig1`.
- Browser design demo: extract `downloads/CodaLauncher-CodaEdition-browser-demo.zip` and open `index.html`. Or open `web/index.html` from this source folder. This mode uses clearly labelled illustrative data; it cannot install or start Minecraft. Play explains that boundary.

The desktop editions preserve the existing game preparation, verified content installation, mod scanning, official-launcher handoff, local test mode and logs. Minecraft still requires Java 25 or newer and its normal account/asset prerequisites. No game download or live Minecraft playtest was performed while building this edition.

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

Windows is cross-compiled; its WebView2 UI still needs a Windows runtime smoke test. Live Minecraft gameplay is untested.
