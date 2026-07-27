[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoRestore,
    [switch]$NoBuild,
    [string]$ResultsDirectory
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

$arguments = @(
    "test",
    $solution,
    "--configuration", $Configuration,
    "--no-restore",
    "--logger", "console;verbosity=normal",
    "-p:BaseOutputPath=$buildOutput"
)
if ($NoBuild) {
    $arguments += "--no-build"
}

if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $resolvedResultsDirectory =
        [System.IO.Path]::GetFullPath($ResultsDirectory)
    New-Item -ItemType Directory -Path $resolvedResultsDirectory -Force |
        Out-Null
    $arguments += @(
        "--logger", "trx",
        "--results-directory", $resolvedResultsDirectory
    )
}

& $dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE."
}
