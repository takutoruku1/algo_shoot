param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$poses = @('idle', 'aim_u', 'aim_ur', 'aim_r', 'aim_dr', 'aim_d', 'spin_00', 'spin_01', 'spin_02', 'spin_03', 'spin_04')
# Measured transparent gutters avoid clipping raised hands and umbrella tips.
$sheets = @(
    @{ Id='mina'; X=@(0,362,724,1086); Y=@(0,362,724,1083,1448) },
    @{ Id='akari'; X=@(0,362,724,1086); Y=@(0,362,724,1086,1448) },
    @{ Id='koharu'; X=@(0,362,724,1086); Y=@(0,374,724,1085,1448) },
    @{ Id='rei'; X=@(0,374,724,1086); Y=@(0,375,724,1076,1448) }
)
$loaded = $false
foreach ($sheet in $sheets) {
    $folder = Join-Path $Root "char/player/$($sheet.Id)/costume_v1"
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    for ($i=0; $i -lt $poses.Count; $i++) {
        $col = $i % 3
        $row = [int][Math]::Floor($i / 3)
        $region = [int[]]@($sheet.X[$col], $sheet.Y[$row], ($sheet.X[$col+1]-$sheet.X[$col]), ($sheet.Y[$row+1]-$sheet.Y[$row]))
        $src = Join-Path $Root "char/raw/costume_$($sheet.Id)_v1.png"
        $dst = Join-Path $folder "$($poses[$i]).png"
        if (!$loaded) {
            & "$PSScriptRoot/key_trim_scale.ps1" -In $src -Out $dst -Key none -TargetH 720 -Region $region -KeepCanvas
            $loaded = $true
        } else {
            [KeyTrimScale]::Run($src, $dst, 'none', 720, 0, 0, $region, $false, $true)
        }
    }
}
foreach ($id in @('ember','star')) {
    [KeyTrimScale]::Run((Join-Path $Root "char/raw/cursor_$($id)_v1.png"),
        (Join-Path $Root "char/ui/cursor_$($id)_v1.png"), 'none', 40, 0, 0, [int[]]@(), $false, $false)
    [KeyTrimScale]::Run((Join-Path $Root "char/raw/cursor_$($id)_v1.png"),
        (Join-Path $Root "char/ui/cursor_$($id)_v1_preview.png"), 'none', 320, 0, 0, [int[]]@(), $false, $false)
}
[KeyTrimScale]::Run((Join-Path $Root 'char/ui/cursor_refrain_v1_source.png'),
    (Join-Path $Root 'char/ui/cursor_refrain_v1_preview.png'), 'none', 320, 0, 0, [int[]]@(), $false, $false)
