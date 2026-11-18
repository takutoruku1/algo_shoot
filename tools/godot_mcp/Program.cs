using System.Diagnostics;
using GodotSharp.Relay;

// Disable stdout buffering — MCP stdio transport requires line-flushed output.
Console.OutputEncoding = System.Text.Encoding.UTF8;

// Optional logging: if the user passes --log-file <path>, redirect Console.Error
// to that file so logs can be inspected without mixing with MCP stdio.
StreamWriter? logWriter = null;
try
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--log-file" && i + 1 < args.Length)
        {
            var path = args[i + 1];
            var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            logWriter = new StreamWriter(fs) { AutoFlush = true };
            Console.SetError(logWriter);
            Console.Error.WriteLine($"[Relay] Logging to file: {path}");
            i++; // skip value
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[Relay] Failed to open log file: {ex.Message}");
}

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await using var pipeServer = new PipeServer();
var mcpServer = new McpServer(pipeServer);

// Connect to the Godot plugin in the background — reconnects automatically
// whenever the editor closes and reopens without restarting the relay.
_ = pipeServer.ListenLoopAsync(cts.Token).ContinueWith(t =>
{
    if (t.IsFaulted)
        Console.Error.WriteLine($"[Relay] Pipe listener failed: {t.Exception?.GetBaseException().Message}");
}, TaskScheduler.Default);

await mcpServer.RunAsync(cts.Token);

// Ensure log writer is disposed on exit so file handles are flushed.
if (logWriter is not null)
{
    try { logWriter.Dispose(); } catch { }
}

