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

namespace GodotSharp.Plugin;

public static partial class CommandDispatcher
{
    // -----------------------------------------------------------------
    // godot_get_script
    // -----------------------------------------------------------------

    private static string ScriptGet(JsonNode? args)
    {
        var scriptPath = args?["script_path"]?.GetValue<string>();
        var nodePath   = args?["node_path"]?.GetValue<string>();

        string absPath;
        string resPath;

        if (!string.IsNullOrWhiteSpace(scriptPath))
        {
            resPath = scriptPath;
            absPath = ProjectSettings.GlobalizePath(scriptPath);
        }
        else if (!string.IsNullOrWhiteSpace(nodePath))
        {
            var node   = GetNode(nodePath);
            var script = node.GetScript().As<Script>();
            if (script is null || string.IsNullOrEmpty(script.ResourcePath))
                return new JsonObject { ["error"] = $"Node '{nodePath}' has no script attached." }.ToJsonString();
            resPath = script.ResourcePath;
            absPath = ProjectSettings.GlobalizePath(resPath);
        }
        else
        {
            throw new ArgumentException("Either 'script_path' or 'node_path' is required.");
        }

        if (!File.Exists(absPath))
            return new JsonObject { ["error"] = $"Script file not found: '{resPath}'" }.ToJsonString();

        var content = File.ReadAllText(absPath);
        return new JsonObject
        {
            ["script_path"] = resPath,
            ["content"]     = content
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_set_script
    // -----------------------------------------------------------------

    private static string ScriptSet(JsonNode? args)
    {
        var scriptPath = RequireString(args, "script_path");
        var content    = args?["content"]?.GetValue<string>()
            ?? throw new ArgumentException("Required parameter 'content' is missing.");

        var absPath = ProjectSettings.GlobalizePath(scriptPath);

        // Ensure directory exists
        var dir = Path.GetDirectoryName(absPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(absPath, content);

        // Notify Godot's filesystem to pick up the new/changed file
        EditorInterface.Singleton.GetResourceFilesystem().Scan();

        return new JsonObject { ["success"] = true, ["script_path"] = scriptPath }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_attach_script
    // -----------------------------------------------------------------

    private static string ScriptAttach(McpPlugin plugin, JsonNode? args)
    {
        var nodePath   = RequireString(args, "node_path");
        var scriptPath = RequireString(args, "script_path");

        var node   = GetNode(nodePath);
        var script = ResourceLoader.Load<Script>(scriptPath)
            ?? throw new InvalidOperationException($"Could not load script: '{scriptPath}'. Make sure the file exists — use godot_set_script to create it first.");

        var oldScript = node.GetScript();

        var undo = plugin.GetUndoRedo();
        undo.CreateAction($"MCP: Attach script '{scriptPath}' to '{node.Name}'",
            mergeMode: UndoRedo.MergeMode.Disable, customContext: GetSceneRoot());
        undo.AddDoMethod(node, "set_script", script);
        undo.AddUndoMethod(node, "set_script", oldScript);
        undo.CommitAction();

        return new JsonObject
        {
            ["success"]     = true,
            ["node_path"]   = nodePath,
            ["script_path"] = scriptPath
        }.ToJsonString();
    }
}

#endif
