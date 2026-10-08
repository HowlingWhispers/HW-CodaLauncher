param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference = 'Stop'
$installerPath = (Resolve-Path $Installer).Path
$installDir = Join-Path $env:LOCALAPPDATA 'Programs/CodaLauncher.CodaEdition'
$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'CodaLauncher Coda Edition.lnk'
$startLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'CodaLauncher Coda Edition.lnk'
$registryPath = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{A972DF50-1A6B-4AEC-BDB9-CA5D9AAB7F44}_is1'
$originalDir = Join-Path $env:LOCALAPPDATA 'Programs/CodaLauncher'
$originalDesktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'CodaLauncher.lnk'
$originalStart = Join-Path ([Environment]::GetFolderPath('Programs')) 'CodaLauncher.lnk'
$originalRegistry = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{60308D26-7772-43DD-95DC-E08776AD50B4}_is1'
$legacyData = Join-Path $env:APPDATA '.howlingshispers/minecraft/saves/coda-edition-installer-smoke'
$canonicalData = Join-Path $env:APPDATA '.howlingwhispers/minecraft/saves/coda-edition-installer-smoke'
$editionData = Join-Path $env:APPDATA '.howlingwhispers-coda-edition/minecraft/saves/installer-smoke'
$protectedFiles = @()
$createdDirs = @()
$createdFiles = @()
$createdOriginalRegistry = $false

# This fixture creates a simulated original installation on a disposable runner.
foreach ($path in @($installDir, $originalDir, $desktopLink, $startLink, $originalDesktop, $originalStart, $legacyData, $canonicalData, $editionData, $originalRegistry, $registryPath)) {
    if (Test-Path $path) { throw "Use a clean, disposable Windows runner: $path already exists" }
}
function RunChecked([string]$Executable, [string[]]$Arguments) {
    $process = Start-Process $Executable -ArgumentList $Arguments -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Exit code $($process.ExitCode): $Executable" }
}
function AssertOriginalPreserved {
    foreach ($file in $protectedFiles) {
        if (!(Test-Path $file) -or (Get-Content $file -Raw).Trim() -ne 'Original file. Keep me, Coda.') {
            throw "Original launcher or world data changed: $file"
        }
    }
    if ((Get-ItemProperty $originalRegistry).DisplayName -ne 'Original CodaLauncher fixture') {
        throw 'Original Installed Apps entry changed'
    }
}
try {
    foreach ($dir in @($originalDir, $legacyData, $canonicalData, $editionData)) {
        New-Item -ItemType Directory -Force $dir | Out-Null
        $createdDirs += $dir
    }
    $protectedFiles = @((Join-Path $originalDir 'CodaLauncher.exe'), $originalDesktop, $originalStart,
        (Join-Path $legacyData 'world-preserved.txt'), (Join-Path $canonicalData 'world-preserved.txt'),
        (Join-Path $editionData 'world-preserved.txt'))
    foreach ($file in $protectedFiles) {
        Set-Content $file 'Original file. Keep me, Coda.'
        $createdFiles += $file
    }
    New-Item $originalRegistry -Force | Out-Null
    $createdOriginalRegistry = $true
    New-ItemProperty $originalRegistry -Name DisplayName -Value 'Original CodaLauncher fixture' -Force | Out-Null
    foreach ($pass in 1..2) {
        RunChecked $installerPath @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=desktopicon', "/LOG=$env:TEMP/coda-edition-install-$pass.log")
        foreach ($file in @("$installDir/CodaLauncher.CodaEdition.exe", "$installDir/web/index.html", $desktopLink, $startLink, "$installDir/unins000.exe")) {
            if (!(Test-Path $file)) { throw "Missing installed file: $file" }
        }
        if (!(Test-Path $registryPath)) { throw 'Coda Edition Installed Apps entry missing' }
        $shell = New-Object -ComObject WScript.Shell
        foreach ($link in @($desktopLink, $startLink)) {
            if ($shell.CreateShortcut($link).TargetPath -ne "$installDir\CodaLauncher.CodaEdition.exe") {
                throw 'Wrong Coda Edition shortcut target'
            }
        }
        RunChecked "$installDir/CodaLauncher.CodaEdition.exe" @('--smoke-ui')
        AssertOriginalPreserved
    }
    RunChecked "$installDir/unins000.exe" @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$env:TEMP/coda-edition-uninstall.log")
    foreach ($file in @("$installDir/CodaLauncher.CodaEdition.exe", $desktopLink, $startLink, $registryPath)) {
        if (Test-Path $file) { throw "Coda Edition uninstall left behind: $file" }
    }
    AssertOriginalPreserved
    Write-Output 'PASS: separate install, shortcuts, Installed Apps, reinstall, WebView2 UI, uninstall; original launcher and all world sentinels preserved'
} finally {
    foreach ($file in $createdFiles) { Remove-Item $file -Force -ErrorAction SilentlyContinue }
    foreach ($dir in $createdDirs) { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
    if ($createdOriginalRegistry) { Remove-Item $originalRegistry -Recurse -Force -ErrorAction SilentlyContinue }
}
