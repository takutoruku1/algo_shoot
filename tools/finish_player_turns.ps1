param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$art = Join-Path $Root 'char/player'
$sheets = (Get-Content (Join-Path $art 'spin_angles.json') -Raw | ConvertFrom-Json).sheets
foreach ($sheet in $sheets) {
    $src = Join-Path $Root $sheet.source
    foreach ($frame in $sheet.frames) {
        $dst = Join-Path $art $frame[0]
        $region = [int[]]$frame[1..4]
        if ('KeyTrimScale' -as [type]) {
            [KeyTrimScale]::Run($src, $dst, 'none', 720, 0, 0, $region, $false, $true)
        } else {
            & "$PSScriptRoot/key_trim_scale.ps1" -In $src -Out $dst -Key none -TargetH 720 -Region $region -KeepCanvas
        }
    }
}
