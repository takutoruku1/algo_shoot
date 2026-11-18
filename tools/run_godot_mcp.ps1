#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
$relayProject = Join-Path $PSScriptRoot 'godot_mcp/GodotSharp.Relay.csproj'
$relayAssembly = Join-Path $PSScriptRoot 'godot_mcp/bin/Release/net8.0/GodotSharp.Relay.dll'
& dotnet build $relayProject --configuration Release --nologo --verbosity quiet 2>&1 | ForEach-Object { [Console]::Error.WriteLine($_) }
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet $relayAssembly
exit $LASTEXITCODE
