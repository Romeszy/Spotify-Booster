[CmdletBinding()]
param(
    [switch]$TrustCertificate,
    [switch]$SkipStart
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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
function Set-PrivateDirectory([string]$Path) {
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    if (-not $IsWindows) { Invoke-Checked 'chmod' @('700', $Path) }
}
function Set-PrivateFile([string]$Path) {
    if (-not $IsWindows) { Invoke-Checked 'chmod' @('600', $Path) }
    else {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
        $system = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-18')
        $acl = New-Object System.Security.AccessControl.FileSecurity
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($sid in @($identity, $system)) {
            $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'Allow')
            $acl.AddAccessRule($rule)
        }
        Set-Acl -Path $Path -AclObject $acl
    }
}

$nodeVersionText = (& node --version 2>$null)
if ($LASTEXITCODE -ne 0 -or $nodeVersionText -notmatch '^v(?<major>[0-9]+)\.[0-9]+\.[0-9]+$') { throw 'node_version_missing_or_malformed' }
if ([int]$Matches.major -lt 20) { throw 'node_version_below_20' }

Push-Location $BackendRoot
try {
    Invoke-Checked 'npm' @('ci', '--ignore-scripts=false')
    $LocalCli = Join-Path $BackendRoot ('node_modules/.bin/supabase' + $(if ($IsWindows) { '.cmd' } else { '' }))
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
    Invoke-Checked 'node' @($Validator, 'validate-tls', $CertificatePath, $CertificateKeyPath, $LocalRoot)

    if (-not (Test-Path -LiteralPath $SigningPath -PathType Leaf)) {
        $capture = Join-Path $AuthRoot ('.signing-capture-' + [guid]::NewGuid().ToString('N') + '.json')
        $placeholderCreated = $false
        try {
            $stream = [System.IO.File]::Open($SigningPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try { $stream.Write([byte[]](91, 93), 0, 2); $stream.Flush($true) } finally { $stream.Dispose() }
            $placeholderCreated = $true
            Set-PrivateFile $SigningPath
            $stdout = & npm exec --offline -- supabase gen signing-key --algorithm ES256 --yes 2>$null
            if ($LASTEXITCODE -ne 0) { throw 'signing_generation_failed' }
            [System.IO.File]::WriteAllText($capture, ($stdout -join "`n"), (New-Object System.Text.UTF8Encoding($false)))
            Set-PrivateFile $capture
            Invoke-Checked 'node' @($Validator, 'normalize-cli-jwk', $capture, $LocalRoot, $SigningPath)
            $placeholderCreated = $false
        } finally {
            if (Test-Path -LiteralPath $capture) { Remove-Item -LiteralPath $capture -Force }
            if ($placeholderCreated -and (Test-Path -LiteralPath $SigningPath)) { Remove-Item -LiteralPath $SigningPath -Force }
        }
    } else {
        Invoke-Checked 'node' @($Validator, 'validate-jwk', $SigningPath, $LocalRoot)
    }

    if ($TrustCertificate) {
        if (-not $IsWindows) { throw 'automatic_trust_is_windows_only' }
        Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
        Write-Host 'Explicitly trusted the local certificate for the current Windows account.'
    } else {
        Write-Host 'Certificate trust was not changed. Re-run with -TrustCertificate only after explicit review.'
    }

    if (-not $SkipStart) { Invoke-Checked $LocalCli @('start', '--workdir', $SupabaseRoot) }
    Write-Host 'Supabase local bootstrap completed with strict TLS and ES256 validation.'
} catch {
    Write-Error ('bootstrap_failed:' + $_.Exception.Message)
    exit 1
} finally {
    Pop-Location
}
