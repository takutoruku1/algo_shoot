param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$manifest = Get-Content (Join-Path $Root 'char/v3/down/generation.prompt.json') -Raw | ConvertFrom-Json
foreach ($character in $manifest.characters) {
    $source = Join-Path $Root $character.source
    foreach ($sprite in $character.sprites) {
        $out = Join-Path $Root $sprite.output
        if (-not ('KeyTrimScale' -as [type])) {
            & (Join-Path $PSScriptRoot 'key_trim_scale.ps1') -In $source -Out $out -Key none -TargetH $sprite.targetHeight -Region $sprite.region -KeepCanvas
        } else {
            [KeyTrimScale]::Run($source, $out, 'none', $sprite.targetHeight, 0, 0, [int[]]$sprite.region, $false, $true)
        }
    }
}
