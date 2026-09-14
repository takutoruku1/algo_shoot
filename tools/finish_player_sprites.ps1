param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$raw = Join-Path $Root 'char/raw'
$output = Join-Path $Root 'char/player'
foreach ($id in @('mina', 'akari', 'koharu', 'rei')) {
  New-Item -ItemType Directory -Force -Path (Join-Path $output $id) | Out-Null
}

& "$PSScriptRoot/key_trim_scale.ps1" -In "$raw/player_mina_idle_v2_green.png" `
  -Out "$output/mina/mina_idle_v2.png" -Key green -TargetH 720 -Despill

$bodyFrames = @('idle_v2', 'spin_v2_00', 'spin_v2_01', 'spin_v2_02', 'spin_v2_03', 'spin_v2_04')
foreach ($id in @('akari', 'koharu', 'rei')) {
  for ($i = 0; $i -lt 6; $i++) {
    $x = ($i % 3) * 418
    $y = if ($i -lt 3) { 0 } else { 648 }
    $height = if ($i -lt 3) { 648 } else { 606 }
    [KeyTrimScale]::Run("$raw/player_$($id)_body_v2_green.png", "$output/$id/$($id)_$($bodyFrames[$i]).png",
      'green', 720, 0, 0, [int[]]@($x, $y, 418, $height), $true)
  }
}

# Measured figure bounds keep neighboring hair out of unevenly spaced sheets.
$sheets = @(
  @{ Id = 'mina'; Kind = 'spin'; Bounds = @(@(28,425), @(441,820), @(871,1232), @(1317,1719), @(1760,2135)) },
  @{ Id = 'mina'; Kind = 'aim'; Bounds = @(@(10,429), @(452,801), @(840,1236), @(1304,1705), @(1751,2160)) },
  @{ Id = 'akari'; Kind = 'aim'; Bounds = @(@(53,408), @(476,846), @(918,1229), @(1350,1665), @(1781,2098)) },
  @{ Id = 'koharu'; Kind = 'aim'; Bounds = @(@(33,400), @(474,874), @(914,1252), @(1369,1707), @(1811,2148)) },
  @{ Id = 'rei'; Kind = 'aim'; Bounds = @(@(14,411), @(448,855), @(878,1268), @(1332,1708), @(1791,2160)) }
)
foreach ($sheet in $sheets) {
  $id = $sheet.Id
  $kind = $sheet.Kind
  $frames = if ($kind -eq 'spin') { @('00','01','02','03','04') } else { @('u','ur','r','dr','d') }
  for ($i = 0; $i -lt 5; $i++) {
    $x = $sheet.Bounds[$i][0] - 3
    $width = $sheet.Bounds[$i][1] - $x + 4
    [KeyTrimScale]::Run("$raw/player_$($id)_$($kind)_v2_green.png", "$output/$id/$($id)_$($kind)_v2_$($frames[$i]).png",
      'green', 720, 0, 0, [int[]]@($x, 0, $width, 724), $true)
  }
}
