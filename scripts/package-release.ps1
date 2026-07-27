[CmdletBinding()]
param(
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsDirectory = Join-Path $repoRoot "artifacts"
$checksumsDirectory = Join-Path $artifactsDirectory "checksums"
$reportsDirectory = Join-Path $artifactsDirectory "reports"
$solution = Join-Path $repoRoot "SpotifyBooster.sln"
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

$version = "0.2.0"
$branch = (& git -C $repoRoot branch --show-current).Trim()
$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
$shortCommit = (& git -C $repoRoot rev-parse --short=12 HEAD).Trim()
$worktreeChanges = @(& git -C $repoRoot status --porcelain)
$isDirty = $worktreeChanges.Count -gt 0
if ($isDirty -and -not $AllowDirty) {
    throw "Refusing to package a dirty worktree. Commit the intended release state or pass -AllowDirty for a clearly labelled development package."
}

$worktreeState = if ($isDirty) { "dirty" } else { "clean" }
$informationalVersion = if ($isDirty) {
    "$version+dirty.$shortCommit"
}
else {
    "$version+$shortCommit"
}

if (Test-Path -LiteralPath $artifactsDirectory) {
    $resolvedArtifacts = (Resolve-Path -LiteralPath $artifactsDirectory).Path
    $expectedArtifacts = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
    if (-not [string]::Equals($resolvedArtifacts, $expectedArtifacts, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean unexpected artifacts path '$resolvedArtifacts'."
    }

    Remove-Item -LiteralPath $resolvedArtifacts -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactsDirectory | Out-Null
New-Item -ItemType Directory -Path $checksumsDirectory | Out-Null
New-Item -ItemType Directory -Path $reportsDirectory | Out-Null

$startedAt = Get-Date
$status = "Complete"
$installerStatus = "Created"
$failure = $null

try {
    & $dotnet restore $solution --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }

    & (Join-Path $PSScriptRoot "build.ps1") `
        -Configuration Release `
        -NoRestore `
        -Version $version `
        -InformationalVersion $informationalVersion
    & (Join-Path $PSScriptRoot "test.ps1") `
        -Configuration Release `
        -NoRestore `
        -NoBuild `
        -ResultsDirectory (Join-Path $reportsDirectory "tests")
    Remove-Item -LiteralPath (Join-Path $artifactsDirectory "build") -Recurse -Force
    & (Join-Path $PSScriptRoot "publish-portable.ps1") `
        -Version $version `
        -InformationalVersion $informationalVersion

    try {
        & (Join-Path $PSScriptRoot "build-installer.ps1") -Version $version
    }
    catch {
        $installerStatus = "Not created"
        throw
    }
}
catch {
    $status = "Failed"
    $failure = $_.Exception.Message
}

$testTotal = 0
$testPassed = 0
$testFailed = 0
try {
    $trxFiles = @(
        Get-ChildItem `
            -LiteralPath (Join-Path $reportsDirectory "tests") `
            -Filter "*.trx" `
            -File `
            -ErrorAction SilentlyContinue
    )
    foreach ($trxFile in $trxFiles) {
        [xml]$trx = Get-Content -LiteralPath $trxFile.FullName -Raw
        $counters = $trx.SelectSingleNode(
            "//*[local-name()='ResultSummary']/*[local-name()='Counters']")
        if ($null -eq $counters) {
            throw "No test counters found in '$($trxFile.FullName)'."
        }

        $testTotal += [int]$counters.total
        $testPassed += [int]$counters.passed
        $testFailed += [int]$counters.failed
    }

    if ($status -eq "Complete" -and (
        $trxFiles.Count -eq 0 `
        -or $testTotal -le 0 `
        -or $testPassed -ne $testTotal `
        -or $testFailed -ne 0)) {
        throw "Automated test evidence is missing or incomplete."
    }
}
catch {
    if ($status -eq "Complete") {
        $status = "Failed"
        $failure = $_.Exception.Message
    }
}

$manifestPath = Join-Path $checksumsDirectory "SHA256SUMS.txt"
$reportPath = Join-Path $reportsDirectory "release-report.txt"
$releaseFiles = Get-ChildItem -LiteralPath $artifactsDirectory -Recurse -File |
    Where-Object { $_.FullName -notin @($manifestPath, $reportPath) } |
    Sort-Object FullName

$manifestLines = foreach ($file in $releaseFiles) {
    $relativePath = $file.FullName.Substring($artifactsDirectory.Length).
        TrimStart([char[]]"\/").
        Replace("\", "/")
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relativePath"
}
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllLines(
    $manifestPath,
    [string[]]@($manifestLines),
    $utf8WithoutBom)

$finishedAt = Get-Date
$report = @(
    "Spotify Booster release report"
    "Version: $version"
    "Informational version: $informationalVersion"
    "Branch: $branch"
    "Commit: $commit"
    "Worktree: $worktreeState"
    "Platform: win-x64"
    "Configuration: Release"
    "Portable: self-contained single-file; trimming disabled"
    "Installer: $installerStatus"
    "Status: $status"
    "Started: $($startedAt.ToString('o'))"
    "Finished: $($finishedAt.ToString('o'))"
    "SHA256 manifest: checksums/SHA256SUMS.txt"
    "Automated test results: reports/tests"
    "Automated tests: $testPassed/$testTotal passed; $testFailed failed"
)
if ($null -ne $failure) {
    $report += "Failure: $failure"
}
[System.IO.File]::WriteAllLines(
    $reportPath,
    [string[]]$report,
    $utf8WithoutBom)

if ($status -ne "Complete") {
    throw "Release packaging failed: $failure See '$reportPath'."
}

Write-Host "Release $version packaged successfully in '$artifactsDirectory'."
