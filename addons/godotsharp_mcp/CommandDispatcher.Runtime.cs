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
    // godot_run_scene
    // -----------------------------------------------------------------

    private static string RuntimeRunScene(JsonNode? args)
    {
        var mode = args?["mode"]?.GetValue<string>() ?? "current";

        if (mode == "main")
            EditorInterface.Singleton.PlayMainScene();
        else
            EditorInterface.Singleton.PlayCurrentScene();

        return new JsonObject { ["success"] = true, ["mode"] = mode }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_stop_scene
    // -----------------------------------------------------------------

    private static string RuntimeStopScene()
    {
        EditorInterface.Singleton.StopPlayingScene();
        return new JsonObject { ["success"] = true }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_get_runtime_node_property
    // -----------------------------------------------------------------

    private static string RuntimeGetNodeProperty(JsonNode? args)
    {
        var nodePath = RequireString(args, "node_path");
        var property = RequireString(args, "property");

        if (!EditorInterface.Singleton.IsPlayingScene())
            throw new InvalidOperationException("The game is not currently running. Use godot_run_scene first.");

        var root = EditorInterface.Singleton.GetEditedSceneRoot()?.GetTree().Root
            ?? throw new InvalidOperationException("Could not access the scene tree root.");

        var node  = root.GetNode(nodePath)
            ?? throw new InvalidOperationException($"Node not found at path: '{nodePath}'");

        var value = node.Get(property);

        return new JsonObject
        {
            ["node_path"] = nodePath,
            ["property"]  = property,
            ["value"]     = NodeSerializer.VariantToJsonNode(value)
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_set_runtime_node_property
    // -----------------------------------------------------------------

    private static string RuntimeSetNodeProperty(JsonNode? args)
    {
        var nodePath  = RequireString(args, "node_path");
        var property  = RequireString(args, "property");
        var valueNode = args?["value"];

        if (!EditorInterface.Singleton.IsPlayingScene())
            throw new InvalidOperationException("The game is not currently running. Use godot_run_scene first.");

        var root = EditorInterface.Singleton.GetEditedSceneRoot()?.GetTree().Root
            ?? throw new InvalidOperationException("Could not access the scene tree root.");

        var node = root.GetNode(nodePath)
            ?? throw new InvalidOperationException($"Node not found at path: '{nodePath}'");

        node.Set(property, NodeSerializer.JsonNodeToVariant(valueNode));

        return new JsonObject
        {
            ["success"]   = true,
            ["node_path"] = nodePath,
            ["property"]  = property,
            ["value"]     = valueNode?.DeepClone()
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_send_input_action
    // -----------------------------------------------------------------

    private static string RuntimeSendInputAction(JsonNode? args)
    {
        var action  = RequireString(args, "action");
        var pressed = args?["pressed"]?.GetValue<bool>()
            ?? throw new ArgumentException("Required parameter 'pressed' is missing.");

        if (!EditorInterface.Singleton.IsPlayingScene())
            throw new InvalidOperationException("The game is not currently running. Use godot_run_scene first.");

        if (pressed)
            Input.ActionPress(action);
        else
            Input.ActionRelease(action);

        return new JsonObject
        {
            ["success"] = true,
            ["action"]  = action,
            ["pressed"] = pressed
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_get_runtime_scene_tree
    // -----------------------------------------------------------------

    private static string RuntimeGetSceneTree(JsonNode? args)
    {
        var includeProperties = args?["include_properties"]?.GetValue<bool>() ?? false;

        if (!EditorInterface.Singleton.IsPlayingScene())
            throw new InvalidOperationException("The game is not currently running. Use godot_run_scene first.");

        var root = EditorInterface.Singleton.GetEditedSceneRoot()?.GetTree().Root
            ?? throw new InvalidOperationException("Could not access the scene tree root.");

        return NodeSerializer.SerializeTree(root, includeProperties).ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_list_export_presets
    // -----------------------------------------------------------------

    private static string RuntimeListExportPresets()
    {
        var projectDir   = ProjectSettings.GlobalizePath("res://");
        var presetsPath  = Path.Combine(projectDir, "export_presets.cfg");

        if (!File.Exists(presetsPath))
            return new JsonObject { ["presets"] = new JsonArray(), ["count"] = 0 }.ToJsonString();

        var cfg = new ConfigFile();
        var err = cfg.Load(presetsPath);
        if (err != Error.Ok)
            throw new InvalidOperationException($"Failed to parse export_presets.cfg: {err}");

        var presets = new JsonArray();

        // Sections are named "preset.0", "preset.1", etc.
        foreach (var section in cfg.GetSections())
        {
            if (!section.StartsWith("preset.")) continue;

            presets.Add(new JsonObject
            {
                ["name"]       = cfg.GetValue(section, "name", "").AsString(),
                ["platform"]   = cfg.GetValue(section, "platform", "").AsString(),
                ["runnable"]   = cfg.GetValue(section, "runnable", false).AsBool(),
                ["export_path"]= cfg.GetValue(section, "export_path", "").AsString()
            });
        }

        return new JsonObject { ["presets"] = presets, ["count"] = presets.Count }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_export_project
    // -----------------------------------------------------------------

    private static string RuntimeExportProject(JsonNode? args)
    {
        var presetName  = RequireString(args, "preset_name");
        var outputPath  = RequireString(args, "output_path");

        // Godot's C# API doesn't expose export directly from an EditorPlugin.
        // We shell out to the Godot executable with --export-release.
        var godotExe = OS.GetExecutablePath();

        var projectDir = ProjectSettings.GlobalizePath("res://");

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = godotExe,
            Arguments              = $"--headless --export-release \"{presetName}\" \"{outputPath}\"",
            WorkingDirectory       = projectDir,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Godot export process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var success = process.ExitCode == 0;

        return new JsonObject
        {
            ["success"]     = success,
            ["exit_code"]   = process.ExitCode,
            ["preset_name"] = presetName,
            ["output_path"] = outputPath,
            ["stdout"]      = stdout,
            ["stderr"]      = stderr
        }.ToJsonString();
    }
}

#endif
