using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodotSharp.Relay.Protocol;

/// <summary>Inbound JSON-RPC 2.0 request from the AI client.</summary>
public sealed class JsonRpcRequest
{
    public string Jsonrpc { get; set; } = "2.0";
    public JsonNode? Id { get; set; }
    public string Method { get; set; } = string.Empty;
    public JsonNode? Params { get; set; }

    public static JsonRpcRequest? Parse(string line)
    {
        try
        {
            var doc = JsonNode.Parse(line);
            if (doc is null) return null;
            return new JsonRpcRequest
            {
                Jsonrpc = doc["jsonrpc"]?.GetValue<string>() ?? "2.0",
                Id = doc["id"]?.DeepClone(),
                Method = doc["method"]?.GetValue<string>() ?? string.Empty,
                Params = doc["params"]?.DeepClone()
            };
        }
        catch { return null; }
    }
}

/// <summary>Outbound JSON-RPC 2.0 response to the AI client.</summary>
public sealed class JsonRpcResponse
{
    public string Jsonrpc { get; } = "2.0";
    public JsonNode? Id { get; set; }
    public JsonNode? Result { get; set; }
    public JsonRpcError? Error { get; set; }

    public string Serialize()
    {
        var obj = new JsonObject { ["jsonrpc"] = "2.0" };
        if (Id is not null)
            obj["id"] = Id.DeepClone();
        else
            obj["id"] = JsonValue.Create<object?>(null);

        if (Error is not null)
        {
            obj["error"] = new JsonObject
            {
                ["code"] = Error.Code,
                ["message"] = Error.Message
            };
        }
        else
        {
            obj["result"] = Result?.DeepClone() ?? new JsonObject();
        }
        return obj.ToJsonString();
    }

    public static JsonRpcResponse Success(JsonNode? id, JsonNode result) =>
        new() { Id = id?.DeepClone(), Result = result };

    public static JsonRpcResponse Failure(JsonNode? id, int code, string message) =>
        new() { Id = id?.DeepClone(), Error = new JsonRpcError(code, message) };
}

public sealed record JsonRpcError(int Code, string Message);

/// <summary>
/// Pipe-level command envelope: relay → plugin.
/// </summary>
public sealed class PipeCommand
{
    public string Id { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public JsonNode? Arguments { get; set; }

    public string Serialize() =>
        new JsonObject
        {
            ["id"] = Id,
            ["tool"] = Tool,
            ["arguments"] = Arguments?.DeepClone() ?? new JsonObject()
        }.ToJsonString();

    public static PipeCommand? Parse(string line)
    {
        try
        {
            var doc = JsonNode.Parse(line);
            if (doc is null) return null;
            return new PipeCommand
            {
                Id = doc["id"]?.GetValue<string>() ?? string.Empty,
                Tool = doc["tool"]?.GetValue<string>() ?? string.Empty,
                Arguments = doc["arguments"]?.DeepClone()
            };
        }
        catch { return null; }
    }
}

/// <summary>
/// Pipe-level response envelope: plugin → relay.
/// </summary>
public sealed class PipeResponse
{
    public string Id { get; set; } = string.Empty;
    public bool IsError { get; set; }
    public string Text { get; set; } = string.Empty;

    public string Serialize() =>
        new JsonObject
        {
            ["id"] = Id,
            ["isError"] = IsError,
            ["text"] = Text
        }.ToJsonString();

    public static PipeResponse? Parse(string line)
    {
        try
        {
            var doc = JsonNode.Parse(line);
            if (doc is null) return null;
            return new PipeResponse
            {
                Id = doc["id"]?.GetValue<string>() ?? string.Empty,
                IsError = doc["isError"]?.GetValue<bool>() ?? false,
                Text = doc["text"]?.GetValue<string>() ?? string.Empty
            };
        }
        catch { return null; }
    }
}
