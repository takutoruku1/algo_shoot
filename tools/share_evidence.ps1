#requires -Version 7.0
param(
    [ValidateSet('Share', 'Add', 'Start', 'Status', 'Stop')][string]$Action = 'Share',
    [string[]]$Files = @(),
    [string]$Title,
    [string]$Note = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $PSScriptRoot 'serve_mobile_pv.mjs'
$runtimeRoot = Join-Path $projectRoot 'output\mobile_evidence'

if ($Action -in @('Share', 'Add')) {
    if ($Files.Count -eq 0) { throw 'Specify -Files with the images or videos to share.' }
    $arguments = @($scriptPath, 'add', '--note', $Note)
    if ($Title) { $arguments += @('--title', $Title) }
    & node @arguments -- @Files
    if ($LASTEXITCODE -ne 0) { throw 'Evidence registration failed. Sharing was not started.' }
}

if ($Action -in @('Share', 'Start')) {
    $version = '2026.10.6'
    $expectedHash = 'd521b3bf68982165f0d06d76732f22d9a105ef057ff3ae4c0986f3a23190950f'
    $binaryDirectory = Join-Path $runtimeRoot 'bin'
    $cloudflared = Join-Path $binaryDirectory "cloudflared-$version.exe"
    New-Item -ItemType Directory -Path $binaryDirectory -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $runtimeRoot '.gdignore') -Value ''
    if (-not (Test-Path -LiteralPath $cloudflared)) {
        $download = Join-Path $binaryDirectory (([guid]::NewGuid().ToString('N')) + '.download')
        Invoke-WebRequest -Uri "https://github.com/cloudflare/cloudflared/releases/download/$version/cloudflared-windows-amd64.exe" -OutFile $download
        if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
            throw 'cloudflared SHA-256 mismatch. Download was not executed.'
        }
        Move-Item -LiteralPath $download -Destination $cloudflared
    }
    if ((Get-FileHash -LiteralPath $cloudflared -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
        throw 'cloudflared SHA-256 mismatch. Binary was not executed.'
    }
    & node $scriptPath start --cloudflared $cloudflared
    if ($LASTEXITCODE -ne 0) { throw 'Evidence sharing did not become ready. Inspect output/mobile_evidence/server.log.' }
} elseif ($Action -in @('Status', 'Stop')) {
    & node $scriptPath $Action.ToLowerInvariant()
    if ($LASTEXITCODE -ne 0) { throw 'Evidence sharing command failed.' }
}
