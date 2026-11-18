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
    // godot_create_directory
    // -----------------------------------------------------------------

    private static string FsCreateDirectory(JsonNode? args)
    {
        var path    = RequireString(args, "path");
        var absPath = ProjectSettings.GlobalizePath(path);

        Directory.CreateDirectory(absPath);
        EditorInterface.Singleton.GetResourceFilesystem().Scan();

        return new JsonObject { ["success"] = true, ["path"] = path }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_delete_file
    // -----------------------------------------------------------------

    private static string FsDeleteFile(JsonNode? args)
    {
        var path    = RequireString(args, "path");
        var confirm = args?["confirm"]?.GetValue<bool>() ?? false;

        if (!confirm)
            throw new InvalidOperationException("Deletion requires 'confirm' to be explicitly set to true.");

        var absPath = ProjectSettings.GlobalizePath(path);

        if (!File.Exists(absPath))
            throw new InvalidOperationException($"File not found: '{path}'");

        File.Delete(absPath);

        // Also remove .import sidecar if present
        var importPath = absPath + ".import";
        if (File.Exists(importPath)) File.Delete(importPath);

        EditorInterface.Singleton.GetResourceFilesystem().Scan();

        return new JsonObject { ["success"] = true, ["deleted_path"] = path }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_move_file
    // -----------------------------------------------------------------

    private static string FsMoveFile(JsonNode? args)
    {
        var sourcePath = RequireString(args, "source_path");
        var destPath   = RequireString(args, "dest_path");

        var absSource = ProjectSettings.GlobalizePath(sourcePath);
        var absDest   = ProjectSettings.GlobalizePath(destPath);

        if (!File.Exists(absSource))
            throw new InvalidOperationException($"Source file not found: '{sourcePath}'");

        var destDir = Path.GetDirectoryName(absDest);
        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

        File.Move(absSource, absDest);

        // Move .import sidecar if present
        var importSource = absSource + ".import";
        var importDest   = absDest + ".import";
        if (File.Exists(importSource)) File.Move(importSource, importDest);

        EditorInterface.Singleton.GetResourceFilesystem().Scan();

        return new JsonObject
        {
            ["success"]     = true,
            ["source_path"] = sourcePath,
            ["dest_path"]   = destPath
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_list_scripts
    // -----------------------------------------------------------------

    private static string FsListScripts(JsonNode? args)
    {
        var resRoot  = ProjectSettings.GlobalizePath("res://");
        var searchIn = args?["directory"]?.GetValue<string>();
        var absSearch = string.IsNullOrWhiteSpace(searchIn)
            ? resRoot
            : ProjectSettings.GlobalizePath(searchIn);

        var files = Directory
            .GetFiles(absSearch, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".godot"))
            .Select(f => "res://" + Path.GetRelativePath(resRoot, f).Replace('\\', '/'))
            .OrderBy(p => p)
            .ToArray();

        var arr = new JsonArray();
        foreach (var f in files) arr.Add(f);
        return new JsonObject { ["scripts"] = arr, ["count"] = files.Length }.ToJsonString();
    }
}

#endif
