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
    // godot_get_project_setting
    // -----------------------------------------------------------------

    private static string ProjectSettingGet(JsonNode? args)
    {
        var key = RequireString(args, "key");

        if (!ProjectSettings.HasSetting(key))
            throw new InvalidOperationException($"Project setting '{key}' does not exist.");

        var value = ProjectSettings.GetSetting(key);

        return new JsonObject
        {
            ["key"]   = key,
            ["value"] = NodeSerializer.VariantToJsonNode(value)
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_set_project_setting
    // -----------------------------------------------------------------

    private static string ProjectSettingSet(JsonNode? args)
    {
        var key       = RequireString(args, "key");
        var valueNode = args?["value"];

        ProjectSettings.SetSetting(key, NodeSerializer.JsonNodeToVariant(valueNode));

        var saveErr = ProjectSettings.Save();
        if (saveErr != Error.Ok)
            throw new InvalidOperationException($"ProjectSettings.Save() failed: {saveErr}");

        return new JsonObject
        {
            ["success"] = true,
            ["key"]     = key,
            ["value"]   = valueNode?.DeepClone()
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_list_autoloads
    // -----------------------------------------------------------------

    private static string ProjectListAutoloads()
    {
        var result = new JsonArray();

        // Autoloads are stored as ProjectSettings keys: autoload/<Name>
        var allKeys = ProjectSettings.Singleton.GetPropertyList();
        foreach (var prop in allKeys)
        {
            var key = prop["name"].AsString();
            if (!key.StartsWith("autoload/")) continue;

            var name  = key["autoload/".Length..];
            var raw   = ProjectSettings.GetSetting(key).AsString();

            // Raw value format: "*res://path/to/script.cs"  (* = is singleton)
            var isSingleton = raw.StartsWith('*');
            var path        = isSingleton ? raw[1..] : raw;

            result.Add(new JsonObject
            {
                ["name"]         = name,
                ["script_path"]  = path,
                ["is_singleton"] = isSingleton
            });
        }

        return new JsonObject { ["autoloads"] = result, ["count"] = result.Count }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_add_autoload
    // -----------------------------------------------------------------

    private static string ProjectAddAutoload(McpPlugin plugin, JsonNode? args)
    {
        var name       = RequireString(args, "name");
        var scriptPath = RequireString(args, "script_path");

        // Godot prefixes the path with '*' to mark it as a singleton node (autoload default).
        var key = $"autoload/{name}";
        ProjectSettings.SetSetting(key, $"*{scriptPath}");

        var saveErr = ProjectSettings.Save();
        if (saveErr != Error.Ok)
            throw new InvalidOperationException($"ProjectSettings.Save() failed: {saveErr}");

        return new JsonObject
        {
            ["success"]     = true,
            ["name"]        = name,
            ["script_path"] = scriptPath
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_remove_autoload
    // -----------------------------------------------------------------

    private static string ProjectRemoveAutoload(McpPlugin plugin, JsonNode? args)
    {
        var name = RequireString(args, "name");
        var key  = $"autoload/{name}";

        if (!ProjectSettings.HasSetting(key))
            throw new InvalidOperationException($"No autoload named '{name}' found.");

        ProjectSettings.Clear(key);

        var saveErr = ProjectSettings.Save();
        if (saveErr != Error.Ok)
            throw new InvalidOperationException($"ProjectSettings.Save() failed: {saveErr}");

        return new JsonObject { ["success"] = true, ["name"] = name }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_list_physics_layers
    // -----------------------------------------------------------------

    private static string ProjectListPhysicsLayers()
    {
        var domains = new[] { "2d_physics", "3d_physics", "2d_render", "3d_render", "avoidance" };
        var result  = new JsonObject();

        foreach (var domain in domains)
        {
            var layerObj = new JsonObject();
            for (int i = 1; i <= 32; i++)
            {
                var key = $"layer_names/{domain}/layer_{i}";
                if (ProjectSettings.HasSetting(key))
                {
                    var layerName = ProjectSettings.GetSetting(key).AsString();
                    if (!string.IsNullOrWhiteSpace(layerName))
                        layerObj[i.ToString()] = layerName;
                }
            }
            if (layerObj.Count > 0)
                result[domain] = layerObj;
        }

        return new JsonObject { ["physics_layers"] = result }.ToJsonString();
    }
}

#endif
