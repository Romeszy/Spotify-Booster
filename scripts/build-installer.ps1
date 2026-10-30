[CmdletBinding()]
param(
    [ValidatePattern("^\d+\.\d+\.\d+$")]
    [string]$Version = "0.2.2"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$portableDirectory = Join-Path $repoRoot "artifacts\portable"
$portableExe = Join-Path $portableDirectory "SpotifyBooster-Portable-x64.exe"
$requiredConfig = Join-Path $portableDirectory "appsettings.json"
$installerScript = Join-Path $repoRoot "installer\SpotifyBooster.nsi"
$outputDirectory = Join-Path $repoRoot "artifacts\installer"
$outputFile = Join-Path $outputDirectory "SpotifyBooster-Setup-x64.exe"
$icon = Join-Path $repoRoot "src\SpotifyBooster.App\Assets\SpotifyBooster.ico"

foreach ($requiredFile in @($portableExe, $requiredConfig, $installerScript, $icon)) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Required installer input was not found: '$requiredFile'. Run scripts\publish-portable.ps1 first."
    }
}

$makensisCommand = Get-Command makensis.exe -ErrorAction SilentlyContinue
$makensis = if ($null -ne $makensisCommand) {
    $makensisCommand.Source
}
else {
    @(
        (Join-Path $env:ProgramFiles "NSIS\makensis.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "NSIS\makensis.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\NSIS\makensis.exe")
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($makensis)) {
    throw "NSIS 3.x is required to build the installer. Install NSIS and ensure makensis.exe is on PATH, or install it at '%ProgramFiles%\NSIS\makensis.exe'."
}

if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDirectory | Out-Null

& $makensis `
    "/INPUTCHARSET" `
    "UTF8" `
    "/DVERSION=$Version" `
    "/DSOURCE_DIR=$portableDirectory" `
    "/DOUTPUT_FILE=$outputFile" `
    "/DAPP_ICON=$icon" `
    $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "makensis failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $outputFile)) {
    throw "NSIS completed without producing '$outputFile'."
}

Write-Host "Installer created at '$outputFile'."
