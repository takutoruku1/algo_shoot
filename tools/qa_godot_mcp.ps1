#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$logDirectory = Join-Path $projectRoot 'build/qa_story/godot_mcp'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$start = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WorkingDirectory = $projectRoot
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($argument in @('-NoLogo', '-NoProfile', '-File', (Join-Path $PSScriptRoot 'run_godot_mcp.ps1'))) { $start.ArgumentList.Add($argument) }
$relay = [Diagnostics.Process]::Start($start)
$stderr = $relay.StandardError.ReadToEndAsync()
$script:requestId = 0
$responses = [Collections.Generic.List[object]]::new()
function Request([string]$Method, [hashtable]$Parameters) {
    $script:requestId++
    $request = @{ jsonrpc = '2.0'; id = $script:requestId; method = $Method; params = $Parameters }
    $relay.StandardInput.WriteLine(($request | ConvertTo-Json -Depth 20 -Compress))
    $deadline = [Threading.CancellationTokenSource]::new(30000)
    try { $line = $relay.StandardOutput.ReadLineAsync($deadline.Token).AsTask().GetAwaiter().GetResult() }
    finally { $deadline.Dispose() }
    if (-not $line) { throw 'MCP closed stdout before responding.' }
    $result = $line | ConvertFrom-Json -Depth 30
    if ($result.id -ne $script:requestId) { throw 'MCP response ID mismatch.' }
    $responses.Add($result)
    return $result
}
function Tool([string]$Name, [hashtable]$Arguments = @{}) {
    $response = Request 'tools/call' @{ name = $Name; arguments = $Arguments }
    if ($response.error -or $response.result.isError) { throw ($response | ConvertTo-Json -Depth 10 -Compress) }
    return $response.result.content[0].text | ConvertFrom-Json -Depth 30
}
try {
    $init = Request 'initialize' @{ protocolVersion = '2024-11-05'; capabilities = @{}; clientInfo = @{ name = 'RefrainQA'; version = '1' } }
    if ($init.result.serverInfo.name -ne 'GodotSharpMCP') { throw 'Unexpected MCP server.' }
    $relay.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $list = Request 'tools/list' @{}
    if ('get_project_info' -notin $list.result.tools.name) { throw 'Project inspection tool missing.' }
    $connected = $false
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        $response = Request 'tools/call' @{ name = 'get_project_info'; arguments = @{} }
        if (-not $response.error) { $connected = $true; break }
        [Threading.Tasks.Task]::Delay(500).GetAwaiter().GetResult()
    }
    if (-not $connected) { throw 'Editor did not connect to the project pipe.' }
    $project = Tool 'get_project_info'
    if ($project.project_name -ne 'Refrain') { throw "Wrong project: $($project | ConvertTo-Json -Compress)" }
    $null = Tool 'open_scene' @{ scene_path = 'res://BossPractice.tscn' }
    $tree = Tool 'get_scene_tree'
    $visible = Tool 'get_node_property' @{ node_path = '.'; property = 'visible' }
    $null = Tool 'set_node_property' @{ node_path = '.'; property = 'visible'; value = $false }
    $hidden = Tool 'get_node_property' @{ node_path = '.'; property = 'visible' }
    $null = Tool 'set_node_property' @{ node_path = '.'; property = 'visible'; value = $visible.value }
    if ($hidden.value -ne $false) { throw 'Property edit was not reflected in the editor.' }
    $null = Tool 'get_project_setting' @{ key = 'application/config/name' }
    Write-Output "[GodotMCPQA] ALL PASS: initialize, notifications, tools, project, scene tree, property edit/restore; $($list.result.tools.Count) tools advertised."
} finally {
    $relay.StandardInput.Close()
    if (-not $relay.WaitForExit(10000)) { throw "Relay did not exit after stdin closed (PID $($relay.Id))." }
    [IO.File]::WriteAllText((Join-Path $logDirectory 'relay.log'), $stderr.GetAwaiter().GetResult())
    [IO.File]::WriteAllText((Join-Path $logDirectory 'responses.json'), ($responses | ConvertTo-Json -Depth 40))
    $relay.Dispose()
}
