$ErrorActionPreference = 'Stop'
$RunningOnWindows = $env:OS -eq 'Windows_NT'
. (Join-Path $PSScriptRoot 'process-helpers.ps1')

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$fixtureRoot = Join-Path $tempRoot ('spotify-booster-process-tree-' + [guid]::NewGuid().ToString('N'))
$parentScript = Join-Path $fixtureRoot 'parent.ps1'
$childScript = Join-Path $fixtureRoot 'child.ps1'
$marker = Join-Path $fixtureRoot 'child.pid'
$childPid = $null

try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    [IO.File]::WriteAllText($childScript, @'
param([string]$Marker)
[IO.File]::WriteAllText($Marker, $PID.ToString())
Start-Sleep -Seconds 60
'@)
    [IO.File]::WriteAllText($parentScript, @'
param([string]$ChildScript, [string]$Marker)
$child = Start-Process powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $ChildScript + '"'),('"' + $Marker + '"')) -PassThru
$child.WaitForExit()
'@)

    $timedOut = $false
    try {
        Invoke-BoundedSilent 'powershell.exe' @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
            $parentScript, $childScript, $marker
        ) 1
    } catch {
        if ($_.Exception.Message -ne 'command_timeout') { throw }
        $timedOut = $true
    }
    if (-not $timedOut -or -not (Test-Path -LiteralPath $marker)) { throw 'timeout_fixture_not_exercised' }
    $childPid = [int][IO.File]::ReadAllText($marker)
    Start-Sleep -Milliseconds 250
    if (Get-Process -Id $childPid -ErrorAction SilentlyContinue) { throw 'child_process_survived_timeout' }
    Write-Output 'process_tree_timeout_pass'
} finally {
    if ($childPid -and (Get-Process -Id $childPid -ErrorAction SilentlyContinue)) {
        Stop-Process -Id $childPid -Force
    }
    $canonicalFixture = [IO.Path]::GetFullPath($fixtureRoot)
    if ($canonicalFixture.StartsWith($tempRoot + '\') -and
        [IO.Path]::GetFileName($canonicalFixture) -match '^spotify-booster-process-tree-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $canonicalFixture -Recurse -Force -ErrorAction SilentlyContinue
    }
}
