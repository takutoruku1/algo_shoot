using System.IO.Pipes;
using GodotSharp.Relay.Protocol;

namespace GodotSharp.Relay;

/// <summary>
/// Named pipe server (relay side). Accepts sequential connections from the Godot editor
/// plugin — one at a time — and automatically re-listens after each disconnect so the
/// editor can close/reopen without restarting the relay.
///
/// Thread-safety: SendCommandAsync serialises concurrent callers with a semaphore so
/// only one in-flight pipe request exists at a time.
/// </summary>
public sealed class PipeServer : IAsyncDisposable
{
    public static readonly string PipeName = "GodotSharpMCP_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(Environment.CurrentDirectory).TrimEnd('/', '\\').ToUpperInvariant())))[..16];

    // Current live connection — replaced each time the editor reconnects.
    private NamedPipeServerStream? _pipe;
    private StreamReader?          _reader;
    private StreamWriter?          _writer;

    // Guards _reader/_writer during a command exchange AND during reconnect teardown.
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _pipe?.IsConnected ?? false;

    // ------------------------------------------------------------------
    // Reconnect loop — replaces the old single WaitForConnectionAsync call.
    // Run this fire-and-forget from Program.cs.
    // ------------------------------------------------------------------

    public async Task ListenLoopAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                Console.Error.WriteLine("[Relay] Waiting for Godot plugin to connect...");
                await pipe.WaitForConnectionAsync(ct);
                Console.Error.WriteLine("[Relay] Godot plugin connected via named pipe.");

                // Swap in the new live connection under the lock so SendCommandAsync
                // can't start a request against a half-initialised state.
                await _lock.WaitAsync(ct);
                try
                {
                    _pipe   = pipe;
                    _reader = new StreamReader(pipe, leaveOpen: true);
                    _writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                }
                finally { _lock.Release(); }

                // Poll until the editor disconnects (no event API for NamedPipeServerStream).
                while (pipe.IsConnected && !ct.IsCancellationRequested)
                    await Task.Delay(500, ct);

                Console.Error.WriteLine("[Relay] Godot plugin disconnected. Re-listening...");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Relay] Pipe error: {ex.Message}. Retrying in 1s...");
                try { await Task.Delay(1000, ct); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                // Clear the shared state under lock, but only if it still points at
                // the pipe we just finished with (avoids stomping a newer connection).
                await _lock.WaitAsync(CancellationToken.None);
                try
                {
                    if (_pipe == pipe)
                    {
                        _writer?.Dispose(); _writer = null;
                        _reader?.Dispose(); _reader = null;
                        _pipe = null;
                    }
                }
                finally { _lock.Release(); }

                if (pipe is not null)
                {
                    try { if (pipe.IsConnected) pipe.Disconnect(); } catch { }
                    await pipe.DisposeAsync();
                }
            }
        }
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// Sends a tool command to the plugin and waits for its response.
    /// Returns null if the pipe is not connected or the plugin returns a malformed response.
    /// </summary>
    public async Task<PipeResponse?> SendCommandAsync(PipeCommand command, CancellationToken ct = default)
    {
        if (!IsConnected) return null;

        await _lock.WaitAsync(ct);
        try
        {
            // Re-check inside the lock — connection may have dropped while we waited.
            if (!IsConnected || _writer is null || _reader is null)
                return null;

            await _writer.WriteLineAsync(command.Serialize().AsMemory(), ct);

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var line = await _reader.ReadLineAsync(ct);
                if (line is null) return null;  // pipe closed mid-request
                var response = PipeResponse.Parse(line);
                if (response?.Id == command.Id)
                    return response;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        if (_pipe is not null)
        {
            try { if (_pipe.IsConnected) _pipe.Disconnect(); } catch { }
            await _pipe.DisposeAsync();
        }
        _lock.Dispose();
    }
}

