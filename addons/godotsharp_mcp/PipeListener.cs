#if TOOLS
using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using System.IO;
using System.Threading;

namespace GodotSharp.Plugin;

/// <summary>
/// Connects to the Relay's named pipe server from a background Task.
/// Reads incoming PipeCommand JSON lines and dispatches them to CommandDispatcher
/// via Callable.CallDeferred to ensure all engine operations run on the main thread.
/// </summary>
public sealed class PipeListener
{
    private readonly string _pipeName = "GodotSharpMCP_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(ProjectSettings.GlobalizePath("res://")).TrimEnd('/', '\\').ToUpperInvariant())))[..16];

    private readonly McpPlugin _plugin;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public PipeListener(McpPlugin plugin)
    {
        _plugin = plugin;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ConnectAndListenAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listenTask?.Wait(TimeSpan.FromSeconds(2));
        _cts?.Dispose();
    }

    // -----------------------------------------------------------------

    private async Task ConnectAndListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    serverName: ".",
                    pipeName: _pipeName,
                    direction: PipeDirection.InOut,
                    options: PipeOptions.Asynchronous);

                _plugin.LogDeferred("Connecting to relay pipe...");
                await pipe.ConnectAsync(ct);
                _plugin.LogDeferred("Connected to relay pipe.", LogLevel.Success);

                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Parse the command on the background thread (safe — pure data).
                    var cmdNode = JsonNode.Parse(line);
                    if (cmdNode is null) continue;

                    var cmdId   = cmdNode["id"]?.GetValue<string>() ?? string.Empty;
                    var tool    = cmdNode["tool"]?.GetValue<string>() ?? string.Empty;
                    var args    = cmdNode["arguments"]?.DeepClone();

                    // Marshal execution to the main thread.
                    // We capture 'writer' and the response callback in a closure.
                    Callable.From(() =>
                    {
                        string responseText;
                        bool isError = false;

                        _plugin.Log($"-> {tool}", LogLevel.Tool);

                        try
                        {
                            responseText = CommandDispatcher.Dispatch(_plugin, tool, args);
                            _plugin.Log($"<- {tool} OK", LogLevel.Success);
                        }
                        catch (Exception ex)
                        {
                            responseText = $"Error executing '{tool}': {ex.Message}";
                            isError = true;
                            _plugin.Log($"<- {tool} ERROR: {ex.Message}", LogLevel.Error);
                            GD.PrintErr($"[MCP Plugin] {responseText}");
                        }

                        // Send response back — schedule on a thread-pool task so we don't
                        // block the main thread on a synchronous pipe write.
                        var response = new JsonObject
                        {
                            ["id"]      = cmdId,
                            ["isError"] = isError,
                            ["text"]    = responseText
                        }.ToJsonString();

                        // Use ct so this task does not outlive the plugin and hold a
                        // reference to the old AssemblyLoadContext during hot-reload.
                        _ = Task.Run(async () =>
                        {
                            try { await writer.WriteLineAsync(response); }
                            catch { /* pipe may have closed or ct was cancelled */ }
                        }, ct);

                    }).CallDeferred();
                }

                _plugin.LogDeferred("Pipe disconnected. Will retry.", LogLevel.Warning);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _plugin.LogDeferred($"Pipe error: {ex.Message}. Retrying in 3s...", LogLevel.Error);
                try { await Task.Delay(3000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}

#endif
