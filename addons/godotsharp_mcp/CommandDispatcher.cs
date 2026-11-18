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

/// <summary>
/// Routes tool commands to domain-specific handlers.
/// MUST be called on the main thread — all partials interact with SceneTree and EditorInterface.
///
/// Partials:
///   CommandDispatcher.Scene.cs          — project info, scene list/open/save, scene tree, packed scenes
///   CommandDispatcher.Node.cs           — add/remove/duplicate/reparent node, properties, signals, groups
///   CommandDispatcher.Script.cs         — get/set/attach scripts
///   CommandDispatcher.Resource.cs       — list/create/get/set/duplicate resources
///   CommandDispatcher.Import.cs         — asset import settings and reimport
///   CommandDispatcher.ProjectSettings.cs— project.godot settings, autoloads, physics layers
///   CommandDispatcher.FileSystem.cs     — create directory, delete/move file, list scripts
///   CommandDispatcher.Animation.cs      — AnimationPlayer clip and track editing
///   CommandDispatcher.EditorState.cs    — selection, editor camera, viewport focus
///   CommandDispatcher.Runtime.cs        — run/stop game, live node inspection, input simulation, export
/// </summary>
public static partial class CommandDispatcher
{
    public static string Dispatch(McpPlugin plugin, string tool, JsonNode? arguments)
    {
        return tool switch
        {
            // Scene / Project
            "get_project_info"        => GetProjectInfo(),
            "list_scenes"             => ListScenes(arguments),
            "open_scene"              => OpenScene(arguments),
            "save_scene"              => SaveScene(),
            "get_scene_tree"          => GetSceneTree(arguments),
            "instantiate_scene"       => SceneInstantiate(plugin, arguments),
            "pack_node_as_scene"      => ScenePackNode(arguments),
            "get_scene_inherited_info"=> SceneGetInheritedInfo(),

            // Node
            "add_node"                => NodeAdd(plugin, arguments),
            "remove_node"             => NodeRemove(plugin, arguments),
            "get_node_property"       => NodeGetProperty(arguments),
            "set_node_property"       => NodeSetProperty(plugin, arguments),
            "duplicate_node"          => NodeDuplicate(plugin, arguments),
            "reparent_node"           => NodeReparent(plugin, arguments),
            "rename_node"             => NodeRename(plugin, arguments),
            "move_node"               => NodeMove(plugin, arguments),
            "get_node_groups"         => NodeGetGroups(arguments),
            "list_signals"            => NodeListSignals(arguments),
            "connect_signal"          => NodeConnectSignal(plugin, arguments),
            "add_node_to_group"       => NodeAddToGroup(plugin, arguments),
            "remove_node_from_group"  => NodeRemoveFromGroup(plugin, arguments),

            // Script
            "get_script"              => ScriptGet(arguments),
            "set_script"              => ScriptSet(arguments),
            "attach_script"           => ScriptAttach(plugin, arguments),

            // Resource
            "list_resources"          => ResourceList(arguments),
            "create_resource"         => ResourceCreate(arguments),
            "list_resource_properties"=> ResourceListProperties(arguments),
            "get_resource_property"   => ResourceGetProperty(arguments),
            "set_resource_property"   => ResourceSetProperty(arguments),
            "duplicate_resource"      => ResourceDuplicate(arguments),

            // Import
            "get_import_settings"     => ImportGetSettings(arguments),
            "set_import_settings"     => ImportSetSettings(arguments),
            "reimport_asset"          => ImportReimportAsset(arguments),
            "reimport_all"            => ImportReimportAll(),

            // Project Settings
            "get_project_setting"     => ProjectSettingGet(arguments),
            "set_project_setting"     => ProjectSettingSet(arguments),
            "list_autoloads"          => ProjectListAutoloads(),
            "add_autoload"            => ProjectAddAutoload(plugin, arguments),
            "remove_autoload"         => ProjectRemoveAutoload(plugin, arguments),
            "list_physics_layers"     => ProjectListPhysicsLayers(),

            // File System
            "create_directory"        => FsCreateDirectory(arguments),
            "delete_file"             => FsDeleteFile(arguments),
            "move_file"               => FsMoveFile(arguments),
            "list_scripts"            => FsListScripts(arguments),

            // Animation
            "list_animations"         => AnimationList(arguments),
            "get_animation_length"    => AnimationGetLength(arguments),
            "create_animation"        => AnimationCreate(plugin, arguments),
            "add_animation_track"     => AnimationAddTrack(plugin, arguments),
            "add_keyframe"            => AnimationAddKeyframe(plugin, arguments),

            // Editor State
            "get_selected_nodes"      => EditorGetSelectedNodes(),
            "select_node"             => EditorSelectNode(arguments),
            "get_editor_camera"       => EditorGetCamera(),
            "focus_node"              => EditorFocusNode(arguments),
            "read_output"             => ReadOutput(arguments),
            "reload_current_project"  => ReloadCurrentProject(),
            "build_project"           => BuildProject(),

            // Runtime
            "run_scene"               => RuntimeRunScene(arguments),
            "stop_scene"              => RuntimeStopScene(),
            "get_runtime_node_property"  => RuntimeGetNodeProperty(arguments),
            "set_runtime_node_property"  => RuntimeSetNodeProperty(arguments),
            "send_input_action"       => RuntimeSendInputAction(arguments),
            "get_runtime_scene_tree"  => RuntimeGetSceneTree(arguments),

            // Export
            "list_export_presets"     => RuntimeListExportPresets(),
            "export_project"          => RuntimeExportProject(arguments),

            _ => throw new InvalidOperationException($"Unknown tool: '{tool}'")
        };
    }

    // -----------------------------------------------------------------
    // Tool registry — used by the panel's Tools tab
    // -----------------------------------------------------------------

    public static readonly System.Collections.Generic.IReadOnlyList<(string Tool, string Category, string Description)> AllTools =
    [
        // Scene / Project
        ("get_project_info",          "Scene",      "Returns the active project's name, directory path, and engine version."),
        ("list_scenes",               "Scene",      "Returns all .tscn scene files in the project as res:// paths."),
        ("open_scene",                "Scene",      "Opens a scene file in the editor by its res:// path."),
        ("save_scene",                "Scene",      "Saves the scene currently open in the editor to disk."),
        ("get_scene_tree",            "Scene",      "Returns the node hierarchy of the open scene as a JSON tree."),
        ("instantiate_scene",         "Scene",      "Instantiates a .tscn file as a child of an existing node."),
        ("pack_node_as_scene",        "Scene",      "Packs a node subtree into a new .tscn file."),
        ("get_scene_inherited_info",  "Scene",      "Reports whether the open scene inherits from another scene."),

        // Node
        ("add_node",                  "Node",       "Creates a new node and attaches it as a child of an existing node."),
        ("remove_node",               "Node",       "Removes a node and all its children from the open scene."),
        ("get_node_property",         "Node",       "Reads a single property from a node in the open scene."),
        ("set_node_property",         "Node",       "Sets a single property on a node in the open scene."),
        ("duplicate_node",            "Node",       "Duplicates a node and its subtree, inserting the copy as a sibling."),
        ("reparent_node",             "Node",       "Moves a node to a new parent without destroying its children."),
        ("rename_node",               "Node",       "Renames a node in the open scene."),
        ("move_node",                 "Node",       "Changes a node's position among its siblings in the scene tree."),
        ("get_node_groups",           "Node",       "Returns all groups a node currently belongs to."),
        ("list_signals",              "Node",       "Returns all signals defined on a node, including parameter types."),
        ("connect_signal",            "Node",       "Connects a signal on a source node to a method on a target node."),
        ("add_node_to_group",         "Node",       "Adds a node to a named group."),
        ("remove_node_from_group",    "Node",       "Removes a node from a named group."),

        // Script
        ("get_script",                "Script",     "Reads and returns the full source code of a script file."),
        ("set_script",                "Script",     "Writes source code to a script file, creating it if needed."),
        ("attach_script",             "Script",     "Attaches an existing script file to a node in the open scene."),

        // Resource
        ("list_resources",            "Resource",   "Returns resource files in the project as res:// paths."),
        ("create_resource",           "Resource",   "Creates a new blank .tres resource file of a given Godot class."),
        ("list_resource_properties",  "Resource",   "Returns all editor-visible properties of a loaded resource."),
        ("get_resource_property",     "Resource",   "Reads a single property from a Godot resource file."),
        ("set_resource_property",     "Resource",   "Sets a property on a Godot resource file and saves it to disk."),
        ("duplicate_resource",        "Resource",   "Copies a resource file to a new path as an independent variant."),

        // Import
        ("get_import_settings",       "Import",     "Reads the import settings for an asset from its .import sidecar."),
        ("set_import_settings",       "Import",     "Writes import settings to an asset's .import sidecar and reimports."),
        ("reimport_asset",            "Import",     "Forces a specific asset to re-run its importer."),
        ("reimport_all",              "Import",     "Triggers a full project filesystem scan and reimport of all assets."),

        // Project Settings
        ("get_project_setting",       "Settings",   "Reads a single setting from project.godot by its full key path."),
        ("set_project_setting",       "Settings",   "Writes a setting to project.godot and saves it."),
        ("list_autoloads",            "Settings",   "Returns all autoload singletons registered in the project."),
        ("add_autoload",              "Settings",   "Registers a new autoload singleton in the project."),
        ("remove_autoload",           "Settings",   "Removes an autoload singleton from the project by name."),
        ("list_physics_layers",       "Settings",   "Returns all named collision and render layers from the project."),

        // File System
        ("create_directory",          "FileSystem", "Creates a directory (and any missing parents) in res://."),
        ("delete_file",               "FileSystem", "Deletes a file from the project. Requires confirm=true."),
        ("move_file",                 "FileSystem", "Moves or renames a file within the project."),
        ("list_scripts",              "FileSystem", "Returns all C# script files in the project as res:// paths."),

        // Animation
        ("list_animations",           "Animation",  "Returns all animation clip names in an AnimationPlayer node."),
        ("get_animation_length",      "Animation",  "Returns the duration in seconds of a specific animation clip."),
        ("create_animation",          "Animation",  "Creates a new blank animation clip in an AnimationPlayer node."),
        ("add_animation_track",       "Animation",  "Adds a property track to an animation clip."),
        ("add_keyframe",              "Animation",  "Inserts a keyframe into an animation track at a given time."),

        // Editor State
        ("get_selected_nodes",        "Editor",     "Returns the NodePaths of all nodes currently selected in the editor."),
        ("select_node",               "Editor",     "Selects a node in the editor's Scene dock and focuses the Inspector."),
        ("get_editor_camera",         "Editor",     "Returns the transform of the active 3D editor viewport camera."),
        ("focus_node",                "Editor",     "Selects a node and frames it in the editor viewport."),
        ("read_output",               "Editor",     "Returns recent editor output log text (tool calls, errors, relay messages)."),
        ("reload_current_project",    "Editor",     "Saves all scenes and reloads the entire Godot project (equivalent to Project > Reload Current Project). Fixes Mono/C# script issues without restarting the editor."),
        ("build_project",             "Editor",     "Builds the C# solution via dotnet build. Returns build output and exit code. Call this after editing scripts to compile changes."),

        // Runtime
        ("run_scene",                 "Runtime",    "Launches the game from the editor."),
        ("stop_scene",                "Runtime",    "Stops the currently running game."),
        ("get_runtime_node_property", "Runtime",    "Reads a property from a live node while the game is running."),
        ("set_runtime_node_property", "Runtime",    "Sets a property on a live node while the game is running."),
        ("send_input_action",         "Runtime",    "Synthesizes an input action press/release while the game is running."),
        ("get_runtime_scene_tree",    "Runtime",    "Returns the live scene tree as JSON while the game is running."),

        // Export
        ("list_export_presets",       "Export",     "Returns all export presets configured in export_presets.cfg."),
        ("export_project",            "Export",     "Triggers a project export using the named preset."),
    ];

    // -----------------------------------------------------------------
    // Shared helpers used across all partials
    // -----------------------------------------------------------------

    private static string RequireString(JsonNode? args, string key)
    {
        var value = args?[key]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Required parameter '{key}' is missing or empty.");
        return value;
    }

    private static Node GetSceneRoot() =>
        EditorInterface.Singleton.GetEditedSceneRoot()
            ?? throw new InvalidOperationException("No scene is currently open in the editor.");

    private static Node GetNode(string rawPath)
    {
        var root = GetSceneRoot();
        var rootPathStr = root.GetPath().ToString();

        // Normalise: accept the absolute paths get_scene_tree emits
        // (e.g. "/root/World/Player") and scene-root-relative paths ("Player").
        if (rawPath == rootPathStr || rawPath == ".")
            return root;

        string relPath = rawPath;
        var prefix = rootPathStr + "/";
        if (relPath.StartsWith(prefix, StringComparison.Ordinal))
            relPath = relPath[prefix.Length..];
        else
            relPath = relPath.TrimStart('/');

        if (string.IsNullOrEmpty(relPath))
            return root;

        return root.GetNode(relPath)
            ?? throw new InvalidOperationException($"Node not found at path: '{rawPath}'");
    }
}

#endif
