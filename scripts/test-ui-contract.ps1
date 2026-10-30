$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
[xml]$window = Get-Content -LiteralPath (Join-Path $repo 'src/SpotifyBooster.App/MainWindow.xaml') -Raw
$expected = Get-Content -LiteralPath (Join-Path $repo 'tests/fixtures/main-window-bindings.json') -Raw | ConvertFrom-Json
$actual = @($window.SelectNodes('//@*') | ForEach-Object {
    foreach ($match in [regex]::Matches($_.Value, '\{Binding\s+([^},\s]+)')) {
        $match.Groups[1].Value
    }
} | Sort-Object -Unique)
$missing = @($expected | Where-Object { $_ -notin $actual })
if ($missing.Count -gt 0) { throw "Liaisons fonctionnelles supprimées : $($missing -join ', ')" }

$ns = [System.Xml.XmlNamespaceManager]::new($window.NameTable)
$ns.AddNamespace('p', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$gain = $window.SelectSingleNode('//p:Slider[starts-with(@Value, "{Binding MainGainDb,")]', $ns)
if ($null -eq $gain -or $gain.Minimum -ne '0' -or
    $gain.Maximum -ne '{Binding GainMaximumDb}' -or
    $gain.IsSnapToTickEnabled -ne 'True') {
    throw 'Le réglage du gain doit conserver ses bornes et sa quantification.'
}
if ($window.DocumentElement.MinWidth -ne '880' -or $window.DocumentElement.MinHeight -ne '620') {
    throw 'La taille minimale historique doit rester disponible.'
}
$overlay = $window.SelectSingleNode('//p:Grid[starts-with(@Visibility, "{Binding ExpertConfirmationVisible,")]', $ns)
if ($null -eq $overlay -or $overlay.GetAttribute('Grid.ColumnSpan') -ne '2' -or
    $overlay.GetAttribute('KeyboardNavigation.TabNavigation') -ne 'Cycle') {
    throw 'La confirmation Expert doit couvrir la fenêtre et conserver le cycle clavier.'
}
Write-Output "UI contract passed: $($expected.Count) bindings, gain limits, minimum size, Expert overlay."
