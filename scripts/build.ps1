[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoRestore,
    [ValidatePattern("^\d+\.\d+\.\d+$")]
    [string]$Version = "0.2.1",
    [string]$InformationalVersion = $Version
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "SpotifyBooster.sln"
$buildOutput = (Join-Path $repoRoot "artifacts\build\bin").Replace("\", "/") + "/"
$localDotnet = Join-Path $repoRoot ".dotnet\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
}
else {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw ".NET SDK 8.0.423 is required. Install it or place dotnet.exe at '$localDotnet'."
    }

    $dotnetCommand.Source
}

if (-not $NoRestore) {
    & $dotnet restore $solution --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }
}

& $dotnet build $solution `
    --configuration $Configuration `
    --no-restore `
    -p:Version=$Version `
    -p:InformationalVersion=$InformationalVersion `
    -p:BaseOutputPath=$buildOutput `
    -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}
