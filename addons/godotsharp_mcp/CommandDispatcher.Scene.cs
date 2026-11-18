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
using System.Text.RegularExpressions;

namespace GodotSharp.Plugin;

public static partial class CommandDispatcher
{
    // -----------------------------------------------------------------
    // godot_get_project_info
    // -----------------------------------------------------------------

    private static string GetProjectInfo()
    {
        var projectDir  = ProjectSettings.GlobalizePath("res://");
        var projectName = ProjectSettings.GetSetting("application/config/name").AsString();
        var ver         = Engine.GetVersionInfo();

        return new JsonObject
        {
            ["project_name"]      = projectName,
            ["project_directory"] = projectDir,
            ["godot_version"]     = $"{ver["major"]}.{ver["minor"]}.{ver["patch"]} {ver["status"]}"
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_list_scenes
    // -----------------------------------------------------------------

    private static string ListScenes(JsonNode? args)
    {
        var resRoot   = ProjectSettings.GlobalizePath("res://");
        var searchIn  = args?["directory"]?.GetValue<string>();
        var absSearch = string.IsNullOrWhiteSpace(searchIn)
            ? resRoot
            : ProjectSettings.GlobalizePath(searchIn);

        var files = Directory
            .GetFiles(absSearch, "*.tscn", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".godot"))
            .Select(f => "res://" + Path.GetRelativePath(resRoot, f).Replace('\\', '/'))
            .OrderBy(p => p)
            .ToArray();

        var arr = new JsonArray();
        foreach (var f in files) arr.Add(f);
        return new JsonObject { ["scenes"] = arr, ["count"] = files.Length }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_open_scene
    // -----------------------------------------------------------------

    private static string OpenScene(JsonNode? args)
    {
        var scenePath = RequireString(args, "scene_path");
        EditorInterface.Singleton.OpenSceneFromPath(scenePath);
        return new JsonObject { ["success"] = true, ["scene_path"] = scenePath }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_save_scene
    // -----------------------------------------------------------------

    private static string SaveScene()
    {
        var err = EditorInterface.Singleton.SaveScene();
        if (err != Error.Ok)
            throw new InvalidOperationException($"SaveScene failed with error: {err}");

        var path = EditorInterface.Singleton.GetEditedSceneRoot()?.SceneFilePath ?? "unknown";
        return new JsonObject { ["success"] = true, ["saved_path"] = path }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_get_scene_tree
    // -----------------------------------------------------------------

    private static string GetSceneTree(JsonNode? args)
    {
        var includeProperties = args?["include_properties"]?.GetValue<bool>() ?? false;
        var sceneRoot = EditorInterface.Singleton.GetEditedSceneRoot();
        if (sceneRoot is null)
            return new JsonObject { ["error"] = "No scene is currently open in the editor." }.ToJsonString();

        return NodeSerializer.SerializeTree(sceneRoot, includeProperties).ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_instantiate_scene
    // -----------------------------------------------------------------

    private static string SceneInstantiate(McpPlugin plugin, JsonNode? args)
    {
        var scenePath  = RequireString(args, "scene_path");
        var parentPath = RequireString(args, "parent_path");

        var packed = ResourceLoader.Load<PackedScene>(scenePath)
            ?? throw new InvalidOperationException($"Could not load PackedScene: '{scenePath}'");

        var sceneRoot = GetSceneRoot();
        var instance  = packed.Instantiate();
        var parent    = GetNode(parentPath);

        var undo = plugin.GetUndoRedo();
        undo.CreateAction($"MCP: Instantiate '{scenePath}'",
            mergeMode: UndoRedo.MergeMode.Disable, customContext: sceneRoot);
        undo.AddDoMethod(parent, Node.MethodName.AddChild, instance);
        undo.AddDoMethod(instance, "set_owner", sceneRoot);  // required for scene save
        undo.AddDoReference(instance);
        undo.AddUndoMethod(parent, Node.MethodName.RemoveChild, instance);
        undo.AddUndoReference(instance);
        undo.CommitAction();

        return new JsonObject
        {
            ["success"]    = true,
            ["node_path"]  = $"{parent.GetPath()}/{instance.Name}",
            ["scene_path"] = scenePath
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_pack_node_as_scene
    // -----------------------------------------------------------------

    private static string ScenePackNode(JsonNode? args)
    {
        var nodePath = RequireString(args, "node_path");
        var savePath = RequireString(args, "save_path");

        var node   = GetNode(nodePath);
        var packed = new PackedScene();
        var err    = packed.Pack(node);
        if (err != Error.Ok)
            throw new InvalidOperationException($"PackedScene.Pack failed: {err}");

        var absDir = Path.GetDirectoryName(ProjectSettings.GlobalizePath(savePath));
        if (!string.IsNullOrEmpty(absDir)) Directory.CreateDirectory(absDir);

        var saveErr = ResourceSaver.Save(packed, savePath);
        if (saveErr != Error.Ok)
            throw new InvalidOperationException($"ResourceSaver.Save failed: {saveErr}");

        EditorInterface.Singleton.GetResourceFilesystem().Scan();

        return new JsonObject { ["success"] = true, ["save_path"] = savePath }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_get_scene_inherited_info
    // -----------------------------------------------------------------

    private static string SceneGetInheritedInfo()
    {
        var root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (root is null)
            return new JsonObject { ["error"] = "No scene is currently open." }.ToJsonString();

        var sceneFile = root.SceneFilePath;
        if (string.IsNullOrEmpty(sceneFile))
            return new JsonObject { ["inherited"] = false }.ToJsonString();

        // Inspect the .tscn header — the second line contains [ext_resource ... type="PackedScene"]
        // immediately after [gd_scene ...] when the scene uses inheritance.
        var absPath = ProjectSettings.GlobalizePath(sceneFile);
        string? basePath = null;

        if (File.Exists(absPath))
        {
            foreach (var line in File.ReadLines(absPath).Take(10))
            {
                if (line.Contains("type=\"PackedScene\"") && line.Contains("path="))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"path=""([^""]+)""");
                    if (match.Success) basePath = match.Groups[1].Value;
                    break;
                }
            }
        }

        return new JsonObject
        {
            ["scene_path"] = sceneFile,
            ["inherited"]  = basePath is not null,
            ["base_scene"] = basePath
        }.ToJsonString();
    }
}

#endif
