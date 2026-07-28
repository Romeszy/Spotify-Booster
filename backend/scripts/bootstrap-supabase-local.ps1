[CmdletBinding()]
param(
    [switch]$TrustCertificate,
    [switch]$SkipStart,
    [ValidateRange(30, 900)]
    [int]$StartTimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$RunningOnWindows = $env:OS -eq 'Windows_NT'

$BackendRoot = Split-Path -Parent $PSScriptRoot
$SupabaseRoot = Join-Path $BackendRoot 'supabase'
$LocalRoot = Join-Path $SupabaseRoot '.local'
$TlsRoot = Join-Path $LocalRoot 'tls'
$AuthRoot = Join-Path $LocalRoot 'auth'
$CertificatePath = Join-Path $TlsRoot 'supabase-local.crt'
$CertificateKeyPath = Join-Path $TlsRoot 'supabase-local.key'
$SigningPath = Join-Path $AuthRoot 'signing_keys.json'
$Validator = Join-Path $PSScriptRoot 'validate-local-assets.mjs'
$ExpectedCli = '2.109.1'

function Invoke-Checked([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "command_failed:$File" }
}
function Invoke-BoundedSilent([string]$File, [string[]]$Arguments, [int]$TimeoutSeconds) {
    $quoted = @($Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } })
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo.FileName = $File
    $process.StartInfo.Arguments = ($quoted -join ' ')
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    if (-not $process.Start()) { throw 'command_start_failed' }
    $process.BeginOutputReadLine()
    $process.BeginErrorReadLine()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        throw 'command_timeout'
    }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'command_failed' }
}
function Assert-PrivateWindowsAcl([string]$Path) {
    $currentSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $allowed = @($currentSid, 'S-1-5-18')
    $rules = @((Get-Acl -LiteralPath $Path).Access)
    if ($rules.Count -ne 2) { throw 'invalid_windows_acl' }
    foreach ($rule in $rules) {
        $sid = $rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value
        if ($allowed -notcontains $sid -or $rule.AccessControlType -ne 'Allow' -or $rule.IsInherited -or (($rule.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::FullControl) -ne [System.Security.AccessControl.FileSystemRights]::FullControl)) {
            throw 'invalid_windows_acl'
        }
    }
}
function Set-PrivateDirectory([string]$Path) {
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    if (-not $RunningOnWindows) { Invoke-Checked 'chmod' @('700', $Path) }
    else {
        $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        Invoke-Checked 'icacls.exe' @($Path, '/inheritance:r', '/grant:r', ('*' + $sid + ':(OI)(CI)F'), '*S-1-5-18:(OI)(CI)F')
        Assert-PrivateWindowsAcl $Path
    }
}
function Set-PrivateFile([string]$Path) {
    if (-not $RunningOnWindows) { Invoke-Checked 'chmod' @('600', $Path) }
    else {
        $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        Invoke-Checked 'icacls.exe' @($Path, '/inheritance:r', '/grant:r', ('*' + $sid + ':F'), '*S-1-5-18:F')
        Assert-PrivateWindowsAcl $Path
    }
}

$nodeVersionText = (& node --version 2>$null)
if ($LASTEXITCODE -ne 0) { throw 'node_version_missing_or_malformed' }
& (Join-Path $PSScriptRoot 'check-node-version.ps1') -VersionText $nodeVersionText | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'node_version_rejected' }

Push-Location $BackendRoot
try {
    Invoke-Checked 'npm' @('ci', '--ignore-scripts=false')
    $LocalCli = Join-Path $BackendRoot ('node_modules/.bin/supabase' + $(if ($RunningOnWindows) { '.cmd' } else { '' }))
    if (-not (Test-Path -LiteralPath $LocalCli -PathType Leaf)) { throw 'locked_supabase_cli_missing' }
    $actualCli = (& $LocalCli --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualCli -ne $ExpectedCli) { throw 'locked_supabase_cli_version_mismatch' }
    Invoke-Checked 'docker' @('info')

    Set-PrivateDirectory $LocalRoot
    Set-PrivateDirectory $TlsRoot
    Set-PrivateDirectory $AuthRoot

    $certExists = Test-Path -LiteralPath $CertificatePath -PathType Leaf
    $keyExists = Test-Path -LiteralPath $CertificateKeyPath -PathType Leaf
    if ($certExists -xor $keyExists) { throw 'incomplete_tls_pair_refused' }
    if (-not $certExists) {
        Invoke-Checked 'openssl' @(
            'req','-x509','-newkey','ec','-pkeyopt','ec_paramgen_curve:P-256',
            '-nodes','-sha256','-days','30','-subj','/CN=127.0.0.1',
            '-addext','subjectAltName=IP:127.0.0.1',
            '-keyout',$CertificateKeyPath,'-out',$CertificatePath
        )
        Set-PrivateFile $CertificateKeyPath
    }
    Set-PrivateFile $CertificateKeyPath
    Invoke-Checked 'node' @($Validator, 'validate-tls', $CertificatePath, $CertificateKeyPath, $LocalRoot)

    if (-not (Test-Path -LiteralPath $SigningPath -PathType Leaf)) {
        $stageRoot = Join-Path $AuthRoot ('.signing-stage-' + [guid]::NewGuid().ToString('N'))
        $stageProjectRoot = Join-Path $stageRoot 'project'
        $stageSupabaseRoot = Join-Path $stageProjectRoot 'supabase'
        $stageSigningPath = Join-Path $stageSupabaseRoot 'signing_keys.json'
        $stageConfigPath = Join-Path $stageSupabaseRoot 'config.toml'
        $placeholderCreated = $false
        try {
            # The canonical placeholder proves exclusive creation but is never
            # passed to the CLI, whose implementation truncates its configured path.
            $stream = [System.IO.File]::Open($SigningPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try { $stream.Write([byte[]](91, 93), 0, 2); $stream.Flush($true) } finally { $stream.Dispose() }
            $placeholderCreated = $true
            Set-PrivateFile $SigningPath

            Set-PrivateDirectory $stageRoot
            Set-PrivateDirectory $stageProjectRoot
            Set-PrivateDirectory $stageSupabaseRoot
            [System.IO.File]::WriteAllText(
                $stageConfigPath,
                "project_id = `"spotify-booster-signing-stage`"`n`n[auth]`nsigning_keys_path = `"./signing_keys.json`"`n",
                (New-Object System.Text.UTF8Encoding($false)))
            Set-PrivateFile $stageConfigPath
            $stageStream = [System.IO.File]::Open($stageSigningPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try { $stageStream.Write([byte[]](91, 93), 0, 2); $stageStream.Flush($true) } finally { $stageStream.Dispose() }
            Set-PrivateFile $stageSigningPath

            # CLI v2.109.1 writes via O_TRUNC to the staging signing_keys_path.
            # Both streams are asynchronously discarded: a private JWK never
            # enters a transcript, PowerShell string, capture file, or final path.
            $npmCommand = (Get-Command $(if ($RunningOnWindows) { 'npm.cmd' } else { 'npm' })).Source
            Invoke-BoundedSilent $npmCommand @(
                '--prefix', $BackendRoot, 'exec', '--offline', '--', 'supabase',
                'gen', 'signing-key', '--algorithm', 'ES256', '--yes',
                '--workdir', $stageProjectRoot
            ) 60
            Set-PrivateFile $stageSigningPath
            Invoke-Checked 'node' @($Validator, 'validate-jwk', $stageSigningPath, $LocalRoot, $SigningPath)
            $placeholderCreated = $false
        } finally {
            if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
            if ($placeholderCreated -and (Test-Path -LiteralPath $SigningPath)) { Remove-Item -LiteralPath $SigningPath -Force }
        }
    } else {
        Set-PrivateFile $SigningPath
        Invoke-Checked 'node' @($Validator, 'validate-jwk', $SigningPath, $LocalRoot)
    }

    if ($TrustCertificate) {
        if (-not $RunningOnWindows) { throw 'automatic_trust_is_windows_only' }
        Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
        Write-Host 'Explicitly trusted the local certificate for the current Windows account.'
    } else {
        Write-Host 'Certificate trust was not changed. Re-run with -TrustCertificate only after explicit review.'
    }

    if (-not $SkipStart) {
        $npmCommand = (Get-Command $(if ($RunningOnWindows) { 'npm.cmd' } else { 'npm' })).Source
        Invoke-BoundedSilent $npmCommand @('--prefix', $BackendRoot, 'exec', '--offline', '--', 'supabase', 'start', '--workdir', $BackendRoot) $StartTimeoutSeconds
        $jwksPath = Join-Path $AuthRoot ('.jwks-' + [guid]::NewGuid().ToString('N') + '.json')
        try {
            $jwks = Invoke-RestMethod -Method Get -Uri 'https://127.0.0.1:54321/auth/v1/.well-known/jwks.json' -TimeoutSec 5
            [System.IO.File]::WriteAllText($jwksPath, ($jwks | ConvertTo-Json -Depth 8 -Compress), (New-Object System.Text.UTF8Encoding($false)))
            Invoke-Checked 'node' @($Validator, 'validate-jwks', $SigningPath, $jwksPath, $LocalRoot)
        } finally {
            if (Test-Path -LiteralPath $jwksPath) { Remove-Item -LiteralPath $jwksPath -Force }
        }
    }
    Write-Host 'Supabase local bootstrap completed with strict TLS and ES256/JWKS validation.'
} catch {
    Write-Error ('bootstrap_failed:' + $_.Exception.Message)
    exit 1
} finally {
    Pop-Location
}
