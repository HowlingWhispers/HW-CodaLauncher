param(
    [Parameter(Mandatory)][string]$Version,
    [string]$PayloadDir = 'dist/payload',
    [string]$OutputDir = 'dist/release'
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$') { throw 'Invalid release version' }
$payload = (Resolve-Path $PayloadDir).Path
foreach ($file in @('CodaLauncher.CodaEdition.exe', 'web/index.html', 'web/assets/coda-headshot.png')) {
    if (!(Test-Path (Join-Path $payload $file))) { throw "Payload missing: $file" }
}
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$output = (Resolve-Path $OutputDir).Path
$bootstrapper = Join-Path (Split-Path $output -Parent) 'MicrosoftEdgeWebview2Setup.exe'
Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper
$signature = Get-AuthenticodeSignature $bootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw 'WebView2 bootstrapper must have a valid Microsoft signature'
}
$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
if (!(Test-Path $iscc)) { throw 'Install Inno Setup 6 first' }
& $iscc "/DAppVersion=$Version" "/DPayloadDir=$payload" "/DOutputDir=$output" "/DBootstrapper=$bootstrapper" "$PSScriptRoot/CodaLauncher.iss"
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$installer = Join-Path $output "CodaLauncher-CodaEdition-v$Version-win64-Setup.exe"
$sha = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Encoding ascii "$installer.sha256" "$sha  $([IO.Path]::GetFileName($installer))"
