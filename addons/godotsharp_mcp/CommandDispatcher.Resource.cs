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
    // godot_list_resources
    // -----------------------------------------------------------------

    private static string ResourceList(JsonNode? args)
    {
        var resRoot   = ProjectSettings.GlobalizePath("res://");
        var searchIn  = args?["directory"]?.GetValue<string>();
        var extension = args?["extension"]?.GetValue<string>();

        var absSearch = string.IsNullOrWhiteSpace(searchIn)
            ? resRoot
            : ProjectSettings.GlobalizePath(searchIn);

        var pattern = string.IsNullOrWhiteSpace(extension) ? "*.*" : $"*{extension}";

        var files = Directory
            .GetFiles(absSearch, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains(".godot"))
            .Select(f => "res://" + Path.GetRelativePath(resRoot, f).Replace('\\', '/'))
            .OrderBy(p => p)
            .ToArray();

        var arr = new JsonArray();
        foreach (var f in files) arr.Add(f);
        return new JsonObject { ["resources"] = arr, ["count"] = files.Length }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_create_resource
    // -----------------------------------------------------------------

    private static string ResourceCreate(JsonNode? args)
    {
        var resourceType = RequireString(args, "resource_type");
        var savePath     = RequireString(args, "save_path");

        var obj = ClassDB.Instantiate(resourceType).AsGodotObject();
        if (obj is not Resource resource)
            throw new InvalidOperationException($"'{resourceType}' is not a Resource subclass.");

        var absDir = Path.GetDirectoryName(ProjectSettings.GlobalizePath(savePath));
        if (!string.IsNullOrEmpty(absDir)) Directory.CreateDirectory(absDir);

        var err = ResourceSaver.Save(resource, savePath);
        if (err != Error.Ok)
            throw new InvalidOperationException($"ResourceSaver.Save failed: {err}");

        EditorInterface.Singleton.GetResourceFilesystem().Scan();

        return new JsonObject
        {
            ["success"]       = true,
            ["resource_type"] = resourceType,
            ["save_path"]     = savePath
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_list_resource_properties
    // -----------------------------------------------------------------

    private static string ResourceListProperties(JsonNode? args)
    {
        var resourcePath = RequireString(args, "resource_path");

        var resource = ResourceLoader.Load(resourcePath)
            ?? throw new InvalidOperationException($"Could not load resource: '{resourcePath}'");

        var props    = resource.GetPropertyList();
        var result   = new JsonObject();

        foreach (var prop in props)
        {
            var name  = prop["name"].AsString();
            var usage = (PropertyUsageFlags)(long)prop["usage"];

            if (!usage.HasFlag(PropertyUsageFlags.Editor)) continue;
            if (name.StartsWith('_') || name.Contains('/')) continue;

            try
            {
                var value = resource.Get(name);
                result[name] = NodeSerializer.VariantToJsonNode(value);
            }
            catch { /* skip unreadable */ }
        }

        return new JsonObject { ["resource_path"] = resourcePath, ["properties"] = result }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_get_resource_property
    // -----------------------------------------------------------------

    private static string ResourceGetProperty(JsonNode? args)
    {
        var resourcePath = RequireString(args, "resource_path");
        var property     = RequireString(args, "property");

        var resource = ResourceLoader.Load(resourcePath)
            ?? throw new InvalidOperationException($"Could not load resource: '{resourcePath}'");

        var value = resource.Get(property);

        return new JsonObject
        {
            ["resource_path"] = resourcePath,
            ["property"]      = property,
            ["value"]         = NodeSerializer.VariantToJsonNode(value)
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_set_resource_property
    // -----------------------------------------------------------------

    private static string ResourceSetProperty(JsonNode? args)
    {
        var resourcePath = RequireString(args, "resource_path");
        var property     = RequireString(args, "property");
        var valueNode    = args?["value"];

        var resource = ResourceLoader.Load(resourcePath)
            ?? throw new InvalidOperationException($"Could not load resource: '{resourcePath}'");

        resource.Set(property, NodeSerializer.JsonNodeToVariant(valueNode));

        var err = ResourceSaver.Save(resource, resourcePath);
        if (err != Error.Ok)
            throw new InvalidOperationException($"ResourceSaver.Save failed: {err}");

        EditorInterface.Singleton.GetResourceFilesystem().Scan();

        return new JsonObject
        {
            ["success"]       = true,
            ["resource_path"] = resourcePath,
            ["property"]      = property,
            ["value"]         = valueNode?.DeepClone()
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_duplicate_resource
    // -----------------------------------------------------------------

    private static string ResourceDuplicate(JsonNode? args)
    {
        var sourcePath = RequireString(args, "source_path");
        var destPath   = RequireString(args, "dest_path");

        var resource = ResourceLoader.Load(sourcePath)
            ?? throw new InvalidOperationException($"Could not load resource: '{sourcePath}'");

        var duplicate = resource.Duplicate(true);

        var absDir = Path.GetDirectoryName(ProjectSettings.GlobalizePath(destPath));
        if (!string.IsNullOrEmpty(absDir)) Directory.CreateDirectory(absDir);

        var err = ResourceSaver.Save(duplicate, destPath);
        if (err != Error.Ok)
            throw new InvalidOperationException($"ResourceSaver.Save failed: {err}");

        EditorInterface.Singleton.GetResourceFilesystem().Scan();

        return new JsonObject
        {
            ["success"]     = true,
            ["source_path"] = sourcePath,
            ["dest_path"]   = destPath
        }.ToJsonString();
    }
}

#endif
