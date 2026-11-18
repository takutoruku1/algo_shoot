#if TOOLS
using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace GodotSharp.Plugin;

public static partial class CommandDispatcher
{
    // -----------------------------------------------------------------
    // godot_read_output
    // -----------------------------------------------------------------

    private static string ReadOutput(JsonNode? args)
    {
        var maxLines = args?["max_lines"]?.GetValue<int>() ?? 100;
        var text = McpPlugin.ReadOutput(maxLines);
        return new JsonObject
        {
            ["output"] = text,
            ["line_count"] = text.Length > 0 ? text.Count(c => c == '\n') + 1 : 0
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_reload_current_project
    // -----------------------------------------------------------------

    private static string ReloadCurrentProject()
    {
        EditorInterface.Singleton.SaveAllScenes();
        EditorInterface.Singleton.RestartEditor(true);
        return new JsonObject { ["success"] = true, ["message"] = "Project reload triggered. The editor will restart and reopen this project." }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_build_project
    // -----------------------------------------------------------------

    private static string BuildProject()
    {
        var projectDir = ProjectSettings.GlobalizePath("res://");
        var csproj = System.IO.Directory.GetFiles(projectDir, "*.csproj", System.IO.SearchOption.TopDirectoryOnly)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No .csproj found in project root.");

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = "dotnet",
            Arguments              = $"build \"{csproj}\"",
            WorkingDirectory       = projectDir,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet build process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var success = process.ExitCode == 0;

        return new JsonObject
        {
            ["success"]   = success,
            ["exit_code"] = process.ExitCode,
            ["stdout"]    = stdout,
            ["stderr"]    = stderr
        }.ToJsonString();
    }
    // -----------------------------------------------------------------
    // godot_get_selected_nodes
    // -----------------------------------------------------------------

    private static string EditorGetSelectedNodes()
    {
        var selection = EditorInterface.Singleton.GetSelection();
        var nodes     = selection.GetSelectedNodes();
        var arr       = new JsonArray();
        foreach (var node in nodes) arr.Add(node.GetPath().ToString());
        return new JsonObject { ["selected_nodes"] = arr, ["count"] = nodes.Count }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_select_node
    // -----------------------------------------------------------------

    private static string EditorSelectNode(JsonNode? args)
    {
        var nodePath = RequireString(args, "node_path");
        var node     = GetNode(nodePath);

        var selection = EditorInterface.Singleton.GetSelection();
        selection.Clear();
        selection.AddNode(node);

        return new JsonObject { ["success"] = true, ["node_path"] = nodePath }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_get_editor_camera
    // -----------------------------------------------------------------

    private static string EditorGetCamera()
    {
        // The editor camera lives inside the editor's 3D viewport.
        // We navigate: EditorInterface → main screen → SubViewportContainer → SubViewport → Camera3D
        var viewportBase = EditorInterface.Singleton.GetEditorViewport3D();

        Camera3D? camera = null;
        if (viewportBase is SubViewport sv)
        {
            // Walk children to find the Camera3D the editor uses
            camera = FindChildOfType<Camera3D>(sv);
        }

        if (camera is null)
        {
            return new JsonObject
            {
                ["error"] = "Could not locate the editor 3D camera. Make sure a 3D viewport is active."
            }.ToJsonString();
        }

        var t = camera.GlobalTransform;
        return new JsonObject
        {
            ["position"] = new JsonObject
            {
                ["x"] = t.Origin.X,
                ["y"] = t.Origin.Y,
                ["z"] = t.Origin.Z
            },
            ["basis"] = new JsonObject
            {
                ["x"] = new JsonObject { ["x"] = t.Basis.X.X, ["y"] = t.Basis.X.Y, ["z"] = t.Basis.X.Z },
                ["y"] = new JsonObject { ["x"] = t.Basis.Y.X, ["y"] = t.Basis.Y.Y, ["z"] = t.Basis.Y.Z },
                ["z"] = new JsonObject { ["x"] = t.Basis.Z.X, ["y"] = t.Basis.Z.Y, ["z"] = t.Basis.Z.Z }
            }
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_focus_node
    // -----------------------------------------------------------------

    private static string EditorFocusNode(JsonNode? args)
    {
        var nodePath = RequireString(args, "node_path");
        var node     = GetNode(nodePath);

        var selection = EditorInterface.Singleton.GetSelection();
        selection.Clear();
        selection.AddNode(node);

        // Emit the signal that tells the editor to frame the selection in the viewport.
        // The standard way is to use the editor's "Script" shortcut equivalent — not directly
        // available from the C# API, but selecting the node is the reliable path. The editor
        // then auto-frames on the next _Process if the viewport already had focus.
        EditorInterface.Singleton.GetEditorMainScreen().GrabFocus();

        return new JsonObject { ["success"] = true, ["node_path"] = nodePath }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static T? FindChildOfType<T>(Node parent) where T : Node
    {
        for (int i = 0; i < parent.GetChildCount(); i++)
        {
            var child = parent.GetChild(i);
            if (child is T match) return match;
            var found = FindChildOfType<T>(child);
            if (found is not null) return found;
        }
        return null;
    }
}

#endif
