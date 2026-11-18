using System.Text.Json;
using System.Text.Json.Nodes;
using GodotSharp.Relay.Protocol;
using GodotSharp.Relay.Tools;

namespace GodotSharp.Relay;

/// <summary>
/// Implements MCP (Model Context Protocol) over stdio using JSON-RPC 2.0.
/// Handles initialize, tools/list, and tools/call.
/// Tool calls are forwarded to the Godot editor plugin via the PipeServer.
/// 
/// IMPORTANT: Uses a multi-line JSON accumulator so that MCP clients that
/// send pretty-printed (multi-line) JSON — such as OpenCodeAI and various
/// Copilot/Continue forks — are handled correctly. Must be flushed after
/// each complete message.
/// </summary>
public sealed class McpServer
{
    private readonly PipeServer _pipe;
    private readonly TextReader _stdin;
    private readonly TextWriter _stdout;

    // Accumulator for multi-line JSON messages. Some AI clients (e.g. Continue.dev,
    // OpenCodeAI) send pretty-printed JSON over multiple lines. We accumulate lines
    // until we have a complete, parseable JSON object.
    private string _jsonBuffer = string.Empty;

    public McpServer(PipeServer pipe, TextReader? stdin = null, TextWriter? stdout = null)
    {
        _pipe = pipe;
        _stdin = stdin ?? Console.In;
        _stdout = stdout ?? Console.Out;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.Error.WriteLine("[Relay] MCP server ready. Listening on stdio.");

        while (!ct.IsCancellationRequested)
        {
            var line = await _stdin.ReadLineAsync(ct);
            if (line is null) break; // stdin closed — AI client disconnected

            // Accumulate lines until we have a complete JSON object
            _jsonBuffer += line + "\n";

            // Try to parse the accumulated buffer
            var request = TryParseRequest();
            if (request is null)
                continue; // need more lines

            _jsonBuffer = string.Empty;

            if (request.Id is null) continue;

            var response = await HandleRequestAsync(request, ct);
            await WriteResponseAsync(response, ct);
        }
    }

    /// <summary>
    /// Attempts to parse a complete JSON-RPC request from the accumulated buffer.
    /// Returns null if the buffer doesn't contain a complete valid JSON object yet.
    /// Accepts both single-line and multi-line JSON.
    /// </summary>
    private JsonRpcRequest? TryParseRequest()
    {
        var trimmed = _jsonBuffer.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        // Quick check: JSON must start with '{' — if not, skip malformed lines
        if (trimmed[0] != '{')
            return null;

        try
        {
            // Use JsonDocument to validate the JSON is complete and parseable
            using var doc = JsonDocument.Parse(trimmed);
            return JsonRpcRequest.Parse(trimmed);
        }
        catch (JsonException)
        {
            // Incomplete or malformed JSON — wait for more lines
            return null;
        }
    }

    // -----------------------------------------------------------------

    private async Task<JsonRpcResponse> HandleRequestAsync(JsonRpcRequest req, CancellationToken ct)
    {
        try
        {
            return req.Method switch
            {
                "initialize"   => HandleInitialize(req),
                "tools/list"   => HandleToolsList(req),
                "tools/call"   => await HandleToolsCallAsync(req, ct),
                _              => JsonRpcResponse.Failure(req.Id, -32601, $"Method not found: {req.Method}")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Relay] Unhandled error: {ex.Message}");
            return JsonRpcResponse.Failure(req.Id, -32603, "Internal error");
        }
    }

    private static JsonRpcResponse HandleInitialize(JsonRpcRequest req)
    {
        var result = new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject()
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "GodotSharpMCP",
                ["version"] = "1.0.0"
            }
        };
        return JsonRpcResponse.Success(req.Id, result);
    }

    private static JsonRpcResponse HandleToolsList(JsonRpcRequest req)
    {
        var result = new JsonObject { ["tools"] = ToolRegistry.GetToolList() };
        return JsonRpcResponse.Success(req.Id, result);
    }

    private async Task<JsonRpcResponse> HandleToolsCallAsync(JsonRpcRequest req, CancellationToken ct)
    {
        var toolName = req.Params?["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(toolName))
            return JsonRpcResponse.Failure(req.Id, -32602, "Invalid params: missing tool name");

        if (!_pipe.IsConnected)
            return JsonRpcResponse.Failure(req.Id, -32603,
                "Godot editor not connected. Open this project in the Godot .NET editor using tools/run_godot_qa.ps1.");

        var command = new PipeCommand
        {
            Id = Guid.NewGuid().ToString("N"),
            Tool = toolName,
            Arguments = req.Params?["arguments"]?.DeepClone()
        };

        var pipeResponse = await _pipe.SendCommandAsync(command, ct);
        if (pipeResponse is null)
            return JsonRpcResponse.Failure(req.Id, -32603, "No response from Godot editor (pipe error).");

        // Wrap in MCP content envelope
        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = pipeResponse.Text
            }
        };

        var result = new JsonObject
        {
            ["content"] = content,
            ["isError"] = pipeResponse.IsError
        };

        return JsonRpcResponse.Success(req.Id, result);
    }

    private async Task WriteResponseAsync(JsonRpcResponse response, CancellationToken ct)
    {
        var json = response.Serialize();
        await _stdout.WriteLineAsync(json.AsMemory(), ct);
        // stdout must be flushed — Console.Out auto-flushes on writeline but explicit is safer.
        if (_stdout is StreamWriter sw) await sw.FlushAsync(ct);
    }
}
