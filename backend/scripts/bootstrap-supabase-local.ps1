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
function Invoke-Bounded([string]$File, [string[]]$Arguments, [int]$TimeoutSeconds) {
    $quoted = @($Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } })
    $process = Start-Process -FilePath $File -ArgumentList $quoted -PassThru -NoNewWindow
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        throw 'command_timeout'
    }
    if ($process.ExitCode -ne 0) { throw "command_failed:$File" }
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
        $capture = Join-Path $AuthRoot ('.signing-capture-' + [guid]::NewGuid().ToString('N') + '.json')
        $placeholderCreated = $false
        try {
            $stream = [System.IO.File]::Open($SigningPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try { $stream.Write([byte[]](91, 93), 0, 2); $stream.Flush($true) } finally { $stream.Dispose() }
            $placeholderCreated = $true
            Set-PrivateFile $SigningPath
            $npmCommand = (Get-Command $(if ($RunningOnWindows) { 'npm.cmd' } else { 'npm' })).Source
            $process = New-Object System.Diagnostics.Process
            $process.StartInfo.FileName = $npmCommand
            $process.StartInfo.Arguments = ('--prefix "' + $BackendRoot + '" exec --offline -- supabase gen signing-key --algorithm ES256 --yes')
            $process.StartInfo.WorkingDirectory = $env:TEMP
            $process.StartInfo.UseShellExecute = $false
            $process.StartInfo.RedirectStandardOutput = $true
            $process.StartInfo.RedirectStandardError = $true
            if (-not $process.Start()) { throw 'signing_generation_failed' }
            $stdout = $process.StandardOutput.ReadToEnd()
            $null = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            if ($process.ExitCode -ne 0) { throw 'signing_generation_failed' }
            [System.IO.File]::WriteAllText($capture, ($stdout -join "`n"), (New-Object System.Text.UTF8Encoding($false)))
            Set-PrivateFile $capture
            Invoke-Checked 'node' @($Validator, 'normalize-cli-jwk', $capture, $LocalRoot, $SigningPath)
            $placeholderCreated = $false
        } finally {
            if (Test-Path -LiteralPath $capture) { Remove-Item -LiteralPath $capture -Force }
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

    if (-not $SkipStart) { Invoke-Bounded $LocalCli @('start', '--workdir', $BackendRoot) $StartTimeoutSeconds }
    Write-Host 'Supabase local bootstrap completed with strict TLS and ES256 validation.'
} catch {
    Write-Error ('bootstrap_failed:' + $_.Exception.Message)
    exit 1
} finally {
    Pop-Location
}
