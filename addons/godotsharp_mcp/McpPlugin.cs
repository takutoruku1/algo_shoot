#if TOOLS
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GodotSharp.Plugin;

public enum LogLevel { Info, Success, Warning, Error, Relay, Tool }

[Tool]
public partial class McpPlugin : EditorPlugin
{
    private PipeListener? _listener;
    private Label? _status;
    private EditorDock? _dock;
    private static readonly Queue<string> Output = new();
    private static readonly object OutputLock = new();

    public override void _EnterTree()
    {
        Log("Editor bridge enabled.");
        _status = new Label { Text = "Godot MCP: Codex の接続を待っています。", CustomMinimumSize = new Vector2(0, 60) };
        _dock = new EditorDock { Title = "Godot MCP", DefaultSlot = EditorDock.DockSlot.Bottom };
        _dock.AddChild(_status);
        AddDock(_dock);
        _listener = new PipeListener(this);
        _listener.Start();
    }

    public override void _ExitTree()
    {
        _listener?.Stop();
        _listener = null;
        if (_dock != null)
        {
            RemoveDock(_dock);
            _dock.QueueFree();
            _dock = null;
            _status = null;
        }
    }

    internal void Log(string message, LogLevel level = LogLevel.Info)
    {
        lock (OutputLock)
        {
            Output.Enqueue($"[{level}] {message}");
            while (Output.Count > 200) Output.Dequeue();
        }
        GD.Print($"[Godot MCP] {message}");
        if (_status != null) _status.Text = $"Godot MCP: {message}";
    }

    internal void LogDeferred(string message, LogLevel level = LogLevel.Info) =>
        Callable.From(() => { if (IsInstanceValid(this) && IsInsideTree()) Log(message, level); }).CallDeferred();

    internal static string ReadOutput(int maxLines = 100)
    {
        lock (OutputLock) return string.Join("\n", Output.TakeLast(Math.Clamp(maxLines, 0, 200)));
    }
}
#endif
