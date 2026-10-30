[CmdletBinding()]
param(
    [switch]$NoRestore,
    [ValidatePattern("^\d+\.\d+\.\d+$")]
    [string]$Version = "0.2.2",
    [string]$InformationalVersion = $Version
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Test-FileContainsForbiddenContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $pattern = [regex]::new(
        "(?i:service_role|sb_secret_[A-Za-z0-9_-]{6,}|(?:sk|rk)_(?:live|test)_[A-Za-z0-9_-]{12,}|-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----|-----BEGIN CERTIFICATE-----|eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,})",
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    $encoding = [System.Text.Encoding]::GetEncoding(28591)
    $buffer = New-Object byte[] (1024 * 1024)
    $overlapLength = 64 * 1024
    $tail = ""
    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)

    try {
        while (($bytesRead = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $text = $tail + $encoding.GetString($buffer, 0, $bytesRead)
            if ($pattern.IsMatch($text)) {
                return $true
            }

            $tailLength = [Math]::Min($overlapLength, $text.Length)
            $tail = $text.Substring($text.Length - $tailLength, $tailLength)
        }
    }
    finally {
        $stream.Dispose()
    }

    return $false
}

function Assert-PortableArtifactIsClean {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $root = [System.IO.Path]::GetFullPath($Directory).
        TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $allowedDirectories = @(
        "Data",
        "licenses"
    )
    $allowedFiles = @(
        "appsettings.json",
        "LICENSE.txt",
        "portable.mode",
        "SpotifyBooster-Portable-x64.exe",
        "THIRD_PARTY_NOTICES.md",
        "licenses/DOTNET-LICENSE.txt",
        "licenses/DOTNET-THIRD-PARTY-NOTICES.txt",
        "licenses/NAUDIO-LICENSE.txt",
        "licenses/NSIS-LICENSE.txt"
    )
    $forbiddenPath = [regex]::new(
        "(?i)(^|/)(?:node_modules|\.local|secrets?)(?:/|$)|(^|/)\.env(?:\.|$)|(^|/)(?:supabase|node|npm|npx)(?:\.exe|\.cmd|\.ps1)?$|\.(?:key|pem|pfx|p12|cer|crt|der|jwk|jwks)$",
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)

    $entries = Get-ChildItem -LiteralPath $Directory -Recurse -Force
    foreach ($entry in $entries) {
        if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Portable artifact contains a reparse point and was rejected."
        }

        $fullPath = [System.IO.Path]::GetFullPath($entry.FullName)
        if (-not $fullPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Portable artifact contains a path outside its output directory."
        }

        $relativePath = $fullPath.Substring($root.Length).Replace("\", "/")
        if ($forbiddenPath.IsMatch($relativePath)) {
            throw "Portable artifact contains a forbidden path: '$relativePath'."
        }

        if ($entry.PSIsContainer) {
            if ($relativePath -notin $allowedDirectories) {
                throw "Portable artifact contains an unexpected directory: '$relativePath'."
            }
            continue
        }

        if ($relativePath -notin $allowedFiles) {
            throw "Portable artifact contains an unexpected file: '$relativePath'."
        }
        if (Test-FileContainsForbiddenContent -Path $fullPath) {
            throw "Portable artifact contains forbidden sensitive content in '$relativePath'."
        }
    }

    foreach ($requiredPath in $allowedDirectories + $allowedFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $Directory $requiredPath))) {
            throw "Portable artifact is incomplete: '$requiredPath' is missing."
        }
    }
}

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

Assert-PortableArtifactIsClean -Directory $outputDirectory

Write-Host "Portable package created at '$outputDirectory'."
