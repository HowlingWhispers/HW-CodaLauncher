param(
    [Parameter(Mandatory = $true)][string]$PayloadDirectory
)
$ErrorActionPreference = 'Stop'
$payload = (Resolve-Path $PayloadDirectory).Path
$id = [Guid]::NewGuid().ToString('N')
$stage = Join-Path $env:TEMP "CodaUpdateSmoke-stage-$id"
$target = Join-Path $env:TEMP "CodaUpdateSmoke-target-$id"
$log = Join-Path $env:TEMP "CodaLauncher-update.log"
New-Item -ItemType Directory -Force -Path $stage, $target | Out-Null
try {
    Copy-Item -Path (Join-Path $payload '*') -Destination $stage -Recurse -Force
    if (-not (Test-Path (Join-Path $stage 'CodaLauncher.exe'))) {
        throw 'Self-update staging failed to copy the launcher'
    }
    $marker = Join-Path $stage 'smoke-probe-ok.txt'
    $probe = Start-Process -FilePath (Join-Path $stage 'CodaLauncher.exe') -WorkingDirectory $stage -ArgumentList ('--update-probe "' + $marker + '"') -PassThru -Wait
    if ($probe.ExitCode -ne 0 -or -not (Test-Path $marker)) {
        throw "New launcher staged probe failed (exit $($probe.ExitCode))"
    }
    $version = (Get-Content $marker -Raw).Trim()
    Remove-Item $marker -Force
    $keep = Join-Path $target 'leave-this-file.txt'
    Set-Content -Path $keep -Value 'player-folder-untouched'
    $applyArgs = '--apply-update 2147483647 "' + $stage + '" "' + $target + '" --smoke-update'
    $apply = Start-Process -FilePath (Join-Path $stage 'CodaLauncher.exe') -WorkingDirectory $stage -ArgumentList $applyArgs -PassThru -Wait
    if ($apply.ExitCode -ne 0) {
        throw "Staged updater exited with code $($apply.ExitCode)"
    }
    $installed = Join-Path $target 'CodaLauncher.exe'
    if (-not (Test-Path $installed) -or -not (Test-Path (Join-Path $target 'web/index.html'))) {
        throw 'Self-update did not install a complete launcher'
    }
    if ((Get-FileHash $installed -Algorithm SHA256).Hash -ne
        (Get-FileHash (Join-Path $stage 'CodaLauncher.exe') -Algorithm SHA256).Hash) {
        throw 'Self-update installed the wrong executable'
    }
    if ((Get-Content $keep -Raw).Trim() -ne 'player-folder-untouched') {
        throw 'Self-update touched unrelated player files'
    }
    if (-not (Test-Path $log) -or -not (Select-String -Path $log -Pattern 'Update confirmed: new executable reached headless probe' -Quiet)) {
        throw 'Self-update did not confirm that the installed executable restarted successfully'
    }
    Write-Host "PASS: CodaLauncher $version staged EXE, full replacement, preserved player files and verified restart"
} catch {
    Write-Warning ("Self-update smoke failed: " + $_.Exception.Message)
    if (Test-Path $log) {
        Write-Host "--- CodaLauncher update transaction log ---"
        Get-Content $log | Select-Object -Last 35 | ForEach-Object { Write-Host $_ }
    }
    if (Test-Path $target) {
        Write-Host "--- Installed target contents ---"
        Get-ChildItem $target -Recurse -File | Select-Object -First 25 -ExpandProperty FullName | ForEach-Object { Write-Host $_ }
    }
    throw
} finally {
    foreach ($dir in @($stage, $target)) {
        if (Test-Path $dir) { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
