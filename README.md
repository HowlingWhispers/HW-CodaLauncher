# CodaLauncher

CodaLauncher is the desktop control center for CodaLoader and the Howling Whispers Minecraft ecosystem.

## 0.2.1 self-update installer

- WPF native shell with a local HTML/CSS/JavaScript interface rendered through WebView2.
- Home screen with CodaLoader readiness, Minecraft target, mod count and PLAY.
- News cards from the Howling Whispers launcher feed with offline fallback.
- CML mod discovery from run/mods/*.jar and coda.mod.json.
- Placeholder Profile area for future CML account, Minecraft ownership, Discord linking and avatar work.
- Settings stored under %LOCALAPPDATA%\HowlingWhispers\CodaLauncher.
- Local launcher/CodaLoader logs captured inside the UI.
- PLAY starts the existing Launch-CodaLoader.bat without opening an extra command window.

## Build

Requires the .NET 8 SDK on Windows:

    dotnet restore
    dotnet build -c Release
    dotnet run

CI produces a self-contained Windows x64 ZIP.

## Configure

In Settings, point CodaLoader folder at the directory containing CodaLoader.jar, Launch-CodaLoader.bat, and run/.

Optionally set Launcher feed URL to the HW-Landing launcher feed server, for example http://SERVER_IP:3220. The prototype appends /api/feed automatically.

## Architecture

    CodaLauncher.exe
      -> local WebView2 UI
      -> launcher feed client
      -> CML mod scanner
      -> settings and logs
      -> CodaLoader
          -> Minecraft
          -> CML mods

The web UI has no direct filesystem access. File and process work stays in the native launcher backend.

Whispering Currents remains parked until the CML API foundation is ready.
