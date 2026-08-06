[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$VersionText)
$match = [regex]::Match($VersionText.Trim(), '^v(?<major>[0-9]+)\.[0-9]+\.[0-9]+$')
if (-not $match.Success) { Write-Error 'node_version_missing_or_malformed'; exit 1 }
if ([int]$match.Groups['major'].Value -lt 20) { Write-Error 'node_version_below_20'; exit 1 }
Write-Output 'node_version_ok'
