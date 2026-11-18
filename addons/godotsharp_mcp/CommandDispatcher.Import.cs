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
    // godot_get_import_settings
    // -----------------------------------------------------------------

    private static string ImportGetSettings(JsonNode? args)
    {
        var assetPath = RequireString(args, "asset_path");
        var absAsset  = ProjectSettings.GlobalizePath(assetPath);
        var importPath = absAsset + ".import";

        if (!File.Exists(importPath))
            throw new InvalidOperationException($"No .import sidecar found for '{assetPath}'. The asset may not have been imported yet.");

        var cfg    = new ConfigFile();
        var err    = cfg.Load(importPath);
        if (err != Error.Ok)
            throw new InvalidOperationException($"Failed to parse .import file: {err}");

        var result = new JsonObject();
        foreach (var section in cfg.GetSections())
        {
            var sectionObj = new JsonObject();
            foreach (var key in cfg.GetSectionKeys(section))
            {
                var val = cfg.GetValue(section, key);
                sectionObj[key] = NodeSerializer.VariantToJsonNode(val);
            }
            result[section] = sectionObj;
        }

        return new JsonObject
        {
            ["asset_path"]   = assetPath,
            ["import_file"]  = importPath,
            ["settings"]     = result
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_set_import_settings
    // -----------------------------------------------------------------

    private static string ImportSetSettings(JsonNode? args)
    {
        var assetPath = RequireString(args, "asset_path");
        var settings  = args?["settings"] as JsonObject
            ?? throw new ArgumentException("Required parameter 'settings' must be a JSON object.");

        var absAsset   = ProjectSettings.GlobalizePath(assetPath);
        var importPath = absAsset + ".import";

        if (!File.Exists(importPath))
            throw new InvalidOperationException($"No .import sidecar found for '{assetPath}'.");

        var cfg = new ConfigFile();
        var loadErr = cfg.Load(importPath);
        if (loadErr != Error.Ok)
            throw new InvalidOperationException($"Failed to parse .import file: {loadErr}");

        // Settings keys use "section/key" format to match the .import INI layout.
        foreach (var (rawKey, valueNode) in settings)
        {
            var slash = rawKey.IndexOf('/');
            string section, key;
            if (slash >= 0)
            {
                section = rawKey[..slash];
                key     = rawKey[(slash + 1)..];
            }
            else
            {
                section = "params";
                key     = rawKey;
            }

            cfg.SetValue(section, key, NodeSerializer.JsonNodeToVariant(valueNode));
        }

        var saveErr = cfg.Save(importPath);
        if (saveErr != Error.Ok)
            throw new InvalidOperationException($"Failed to save .import file: {saveErr}");

        // Trigger reimport of just this asset.
        EditorInterface.Singleton.GetResourceFilesystem().ReimportFiles(new string[] { assetPath });

        return new JsonObject
        {
            ["success"]    = true,
            ["asset_path"] = assetPath,
            ["updated"]    = settings.Count
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_reimport_asset
    // -----------------------------------------------------------------

    private static string ImportReimportAsset(JsonNode? args)
    {
        var assetPath = RequireString(args, "asset_path");

        EditorInterface.Singleton.GetResourceFilesystem().ReimportFiles(new string[] { assetPath });

        return new JsonObject { ["success"] = true, ["asset_path"] = assetPath }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_reimport_all
    // -----------------------------------------------------------------

    private static string ImportReimportAll()
    {
        var fs = EditorInterface.Singleton.GetResourceFilesystem();
        fs.ScanSources();
        return new JsonObject { ["success"] = true, ["message"] = "Full filesystem scan and reimport triggered." }.ToJsonString();
    }
}

#endif
