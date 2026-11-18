#requires -Version 7.0
param(
    [Parameter(Mandatory = $true)][string[]]$GodotArgs,
    [string]$ExecutablePath,
    [string]$OutputDirectory,
    [switch]$UseExecutableSaveData
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'algo_shoot.csproj') -Raw)
$version = $project.Project.Sdk.Split('/')[-1]
$engine = (Get-Command "Godot_v$version-stable_mono_win64.exe" -ErrorAction Stop).Source
$apiDirectory = Join-Path (Split-Path -Parent $engine) 'GodotSharp\Api\Debug'
$dotnetRoot = Split-Path -Parent (Get-Command dotnet -ErrorAction Stop).Source

if (-not ('RefrainQaDirectoryAccess' -as [type])) {
    Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
public static class RefrainQaDirectoryAccess {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetCurrentDirectoryW(string path);
}
'@
}

$previousDirectory = [Environment]::CurrentDirectory
try {
    foreach ($directory in @($apiDirectory, $dotnetRoot)) {
        # Godot checks directory existence through SetCurrentDirectoryW, which sandbox permissions can deny.
        if (-not [RefrainQaDirectoryAccess]::SetCurrentDirectoryW($directory)) {
            $code = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            throw "Godot QA was not started: directory access failed (Windows error $code): $directory. Check the installation path and use an approved execution context with access to this directory."
        }
    }
} finally {
    [void][RefrainQaDirectoryAccess]::SetCurrentDirectoryW($previousDirectory)
}

foreach ($assembly in @('GodotSharp.dll', 'GodotPlugins.dll', 'GodotPlugins.runtimeconfig.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $apiDirectory $assembly) -PathType Leaf)) {
        throw "Godot QA was not started: missing $apiDirectory\$assembly."
    }
}

$runName = 'godot_qa_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '_' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$runDirectory = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $projectRoot "build\qa_story\$runName" }
$qaRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'build\qa_story')) + [IO.Path]::DirectorySeparatorChar
if (-not $runDirectory.StartsWith($qaRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "QA output must be inside $qaRoot"
}
$appData = Join-Path $runDirectory 'appdata'
$localAppData = Join-Path $runDirectory 'localappdata'
New-Item -ItemType Directory -Path $appData, $localAppData -Force | Out-Null

if ($GodotArgs -contains '--export-release') {
    $templateSource = Join-Path $env:APPDATA "Godot\export_templates\$version.stable.mono"
    $templateTarget = Join-Path $appData "Godot\export_templates\$version.stable.mono"
    New-Item -ItemType Directory -Path $templateTarget -Force | Out-Null
    foreach ($file in @('version.txt', 'windows_release_x86_64.exe', 'windows_release_x86_64_console.exe', 'icudt_godot.dat')) {
        Copy-Item -LiteralPath (Join-Path $templateSource $file) -Destination $templateTarget
    }
}

$executable = if ($ExecutablePath) { (Resolve-Path -LiteralPath $ExecutablePath).Path } else { $engine }
if ($UseExecutableSaveData -and (-not $ExecutablePath -or -not $executable.StartsWith($qaRoot, [StringComparison]::OrdinalIgnoreCase))) {
    throw 'Executable save-data QA requires a copied game inside build/qa_story.'
}
$start = [Diagnostics.ProcessStartInfo]::new($executable)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WorkingDirectory = $projectRoot
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['APPDATA'] = $appData
$start.Environment['LOCALAPPDATA'] = $localAppData
$start.Environment['DOTNET_ROOT'] = $dotnetRoot
$start.Environment['REFRAIN_QA_RUN'] = if ($UseExecutableSaveData) { '0' } else { '1' }
$arguments = if ($ExecutablePath) { $GodotArgs } else { @('--path', $projectRoot) + $GodotArgs }
foreach ($argument in $arguments) {
    $start.ArgumentList.Add($argument)
}

$process = [Diagnostics.Process]::Start($start)
Write-Output "Godot QA PID $($process.Id): $runDirectory"
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
$process.WaitForExit()
[IO.File]::WriteAllText((Join-Path $runDirectory 'stdout.log'), $stdout.GetAwaiter().GetResult())
[IO.File]::WriteAllText((Join-Path $runDirectory 'stderr.log'), $stderr.GetAwaiter().GetResult())
$exitCode = $process.ExitCode
[ordered]@{
    engine = $engine
    executable = $executable
    arguments = $arguments
    pid = $process.Id
    exit_code = $exitCode
    save_profile = $appData
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDirectory 'result.json')
$process.Dispose()
Write-Output "Godot QA exit code: $exitCode"
exit $exitCode
