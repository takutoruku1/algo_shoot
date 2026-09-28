param([string]$Root = (Split-Path $PSScriptRoot -Parent), [switch]$CostumesOnly)
$ErrorActionPreference = 'Stop'
$poses = @('idle', 'aim_u', 'aim_ur', 'aim_r', 'aim_dr', 'aim_d', 'spin_00', 'spin_01', 'spin_02', 'spin_03', 'spin_04')
$loaded = $false
foreach ($id in @('mina', 'akari', 'koharu', 'rei')) {
    $folder = Join-Path $Root "char/player/$id/costume_v1"
    $metadata = Get-Content (Join-Path $folder 'generation.prompt.json') -Raw | ConvertFrom-Json
    $src = Join-Path $Root $metadata.source
    for ($i=0; $i -lt $poses.Count; $i++) {
        $region = [int[]]$metadata.regions[$i]
        $dst = Join-Path $folder "$($poses[$i]).png"
        if (!$loaded) {
            & "$PSScriptRoot/key_trim_scale.ps1" -In $src -Out $dst -Key none -TargetH 720 -Region $region -KeepCanvas
            $loaded = $true
        } else {
            [KeyTrimScale]::Run($src, $dst, 'none', 720, 0, 0, $region, $false, $true)
        }
    }
}
if ($CostumesOnly) { return }
foreach ($id in @('ember','star')) {
    [KeyTrimScale]::Run((Join-Path $Root "char/raw/cursor_$($id)_v1.png"),
        (Join-Path $Root "char/ui/cursor_$($id)_v1.png"), 'none', 40, 0, 0, [int[]]@(), $false, $false)
    [KeyTrimScale]::Run((Join-Path $Root "char/raw/cursor_$($id)_v1.png"),
        (Join-Path $Root "char/ui/cursor_$($id)_v1_preview.png"), 'none', 320, 0, 0, [int[]]@(), $false, $false)
}
[KeyTrimScale]::Run((Join-Path $Root 'char/ui/cursor_refrain_v1_source.png'),
    (Join-Path $Root 'char/ui/cursor_refrain_v1_preview.png'), 'none', 320, 0, 0, [int[]]@(), $false, $false)
