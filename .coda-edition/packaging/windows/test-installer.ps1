param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference = 'Stop'
$installerPath = (Resolve-Path $Installer).Path
$installDir = Join-Path $env:LOCALAPPDATA 'Programs/CodaLauncher'
$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'CodaLauncher.lnk'
$startLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'CodaLauncher.lnk'
$registryPath = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{60308D26-7772-43DD-95DC-E08776AD50B4}_is1'
if (Test-Path $installDir) { throw 'Run this smoke test only on a clean Windows runner' }
$dataDir = Join-Path $env:APPDATA '.howlingshispers/minecraft/saves/installer-smoke'
New-Item -ItemType Directory -Force $dataDir | Out-Null
$sentinel = Join-Path $dataDir 'world-preserved.txt'
Set-Content $sentinel 'Keep my world, Coda.'
function RunChecked([string]$Executable, [string[]]$Arguments) {
    # -Wait includes child processes (Inno's uninstaller hands off to a temporary copy).
    # The workflow job timeout bounds setup; the UI smoke mode has its own 30s timeout.
    $process = Start-Process $Executable -ArgumentList $Arguments -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Exit code $($process.ExitCode): $Executable" }
}
try {
    # Install, then reinstall over the same installation (same AppId).
    foreach ($pass in 1..2) {
        RunChecked $installerPath @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=desktopicon', "/LOG=$env:TEMP/coda-install-$pass.log")
        foreach ($file in @("$installDir/CodaLauncher.exe", "$installDir/web/index.html", $desktopLink, $startLink, "$installDir/unins000.exe")) {
            if (!(Test-Path $file)) { throw "Missing installed file: $file" }
        }
        if (!(Test-Path $registryPath)) { throw 'Installed Apps entry missing' }
        $shell = New-Object -ComObject WScript.Shell
        foreach ($link in @($desktopLink, $startLink)) {
            if ($shell.CreateShortcut($link).TargetPath -ne "$installDir\CodaLauncher.exe") { throw 'Wrong shortcut target' }
        }
        # Exit 0 only after the installed WebView2 interface sends its ready message.
        RunChecked "$installDir/CodaLauncher.exe" @('--smoke-ui')
    }
    RunChecked "$installDir/unins000.exe" @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$env:TEMP/coda-uninstall.log")
    foreach ($file in @("$installDir/CodaLauncher.exe", $desktopLink, $startLink)) {
        if (Test-Path $file) { throw "Uninstaller left behind: $file" }
    }
    if (Test-Path $registryPath) { throw 'Uninstaller left Installed Apps entry' }
    if ((Get-Content $sentinel -Raw).Trim() -ne 'Keep my world, Coda.') { throw 'Player data was changed' }
    Write-Output 'PASS: install, shortcuts, Installed Apps, reinstall, WebView2 UI, uninstall, world preservation'
} finally {
    Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
}
