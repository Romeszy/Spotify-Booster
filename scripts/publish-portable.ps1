[CmdletBinding()]
param(
    [switch]$NoRestore,
    [ValidatePattern("^\d+\.\d+\.\d+$")]
    [string]$Version = "0.2.0",
    [string]$InformationalVersion = $Version
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\SpotifyBooster.App\SpotifyBooster.App.csproj"
$outputDirectory = Join-Path $repoRoot "artifacts\portable"
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
    & $dotnet restore $project `
        --runtime win-x64 `
        --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }
}

if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDirectory | Out-Null

& $dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $outputDirectory `
    -p:Version=$Version `
    -p:InformationalVersion=$InformationalVersion `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedExe = Join-Path $outputDirectory "SpotifyBooster.App.exe"
$releaseExe = Join-Path $outputDirectory "SpotifyBooster-Portable-x64.exe"
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "Published executable was not found at '$publishedExe'."
}
Move-Item -LiteralPath $publishedExe -Destination $releaseExe

$requiredConfig = Join-Path $outputDirectory "appsettings.json"
if (-not (Test-Path -LiteralPath $requiredConfig)) {
    throw "Required runtime configuration was not published at '$requiredConfig'."
}

Get-ChildItem -LiteralPath $outputDirectory -File |
    Where-Object { $_.Name -notin @("SpotifyBooster-Portable-x64.exe", "appsettings.json") } |
    Remove-Item -Force

foreach ($requiredNotice in @(
    (Join-Path $repoRoot "LICENSE.txt"),
    (Join-Path $repoRoot "THIRD_PARTY_NOTICES.md"),
    (Join-Path $repoRoot "licenses\NAUDIO-LICENSE.txt"),
    (Join-Path $repoRoot "licenses\DOTNET-LICENSE.txt"),
    (Join-Path $repoRoot "licenses\DOTNET-THIRD-PARTY-NOTICES.txt"),
    (Join-Path $repoRoot "licenses\NSIS-LICENSE.txt")
)) {
    if (-not (Test-Path -LiteralPath $requiredNotice)) {
        throw "Required distribution notice was not found: '$requiredNotice'."
    }
}

Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE.txt") `
    -Destination $outputDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot "THIRD_PARTY_NOTICES.md") `
    -Destination $outputDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot "licenses") `
    -Destination (Join-Path $outputDirectory "licenses") `
    -Recurse

New-Item -ItemType File -Path (Join-Path $outputDirectory "portable.mode") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $outputDirectory "Data") -Force | Out-Null

Write-Host "Portable package created at '$outputDirectory'."
