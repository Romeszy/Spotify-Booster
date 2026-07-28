$ErrorActionPreference='Stop'
$checker=Join-Path $PSScriptRoot 'check-node-version.ps1'
function Expect([string]$value,[bool]$accepted) {
  $p=Start-Process powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$checker+'"'),'-VersionText',$value) -Wait -PassThru -NoNewWindow
  if (($p.ExitCode -eq 0) -ne $accepted) { throw ('unexpected_node_version_result:'+$value) }
}
Expect 'v19.9.0' $false
Expect 'v20.0.0' $true
Expect 'v24.15.0' $true
Expect '19.9.0' $false
Expect 'vNext' $false
Write-Output 'node_version_matrix_pass v19=reject v20=accept newer=accept missing_prefix=reject malformed=reject'
