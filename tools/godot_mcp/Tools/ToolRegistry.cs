using System.Text.Json.Nodes;

namespace GodotSharp.Relay.Tools;

/// <summary>
/// MCP tool schemas — 66 tools across 10 domains.
/// Naming convention: godot_{verb}_{noun}
/// </summary>
public static class ToolRegistry
{
    // Appended to every node_path description so the AI knows which format to use.
    private const string NodePathHint = " Use the 'path' field from get_scene_tree output (e.g. 'Player' or 'Player/Sprite2D'). The scene root itself is '.'.";

    public static JsonArray GetToolList() => new()
    {
        // ---------------------------------------------------------------
        // SCENE / PROJECT  (8 tools)
        // ---------------------------------------------------------------

        BuildTool("get_project_info",
            "Returns the active Godot project's name, absolute directory path, and engine version. Call this first to orient yourself before inspecting scenes or modifying nodes.",
            new JsonObject(), new JsonArray()),

        BuildTool("list_scenes",
            "Returns all .tscn scene files in the project as an array of res:// paths. Use this to discover what scenes exist before calling godot_open_scene.",
            Props(("directory", "string", "Optional res:// subdirectory to search within. Defaults to res:// (entire project).")),
            Req()),

        BuildTool("open_scene",
            "Opens a scene file in the Godot editor by its res:// path, making it the active edited scene. Use godot_list_scenes to find valid paths.",
            Props(("scene_path", "string", "res:// path to the .tscn file, e.g. \"res://scenes/Main.tscn\".")),
            Req("scene_path")),

        BuildTool("save_scene",
            "Saves the scene currently open in the editor to disk. Always call this after making node mutations to persist changes.",
            new JsonObject(), new JsonArray()),

        BuildTool("get_scene_tree",
            "Returns the node hierarchy of the scene currently open in the Godot editor as a JSON tree. Each node includes its name, class type, a scene-root-relative 'path' (e.g. 'Player' or 'Player/Sprite2D'), and attached script path. Pass these path values directly to any node_path parameter. Set include_properties=true to also read Inspector-visible property values.",
            Props(("include_properties", "boolean", "When true, includes exported and editor-visible properties. Defaults to false.")),
            Req()),

        BuildTool("instantiate_scene",
            "Loads a .tscn file as a PackedScene and instantiates it as a child of an existing node in the open scene. Returns the new instance's NodePath.",
            Props(
                ("scene_path", "string", "res:// path of the .tscn to instantiate."),
                ("parent_path", "string", "NodePath of the parent node to attach the instance to." + NodePathHint)),
            Req("scene_path", "parent_path")),

        BuildTool("pack_node_as_scene",
            "Packs a node subtree from the open scene into a new .tscn file, creating a reusable prefab. The node remains in the current scene.",
            Props(
                ("node_path", "string", "NodePath of the root node to pack." + NodePathHint),
                ("save_path", "string", "res:// path where the new .tscn file will be saved, e.g. \"res://scenes/Enemy.tscn\".")),
            Req("node_path", "save_path")),

        BuildTool("get_scene_inherited_info",
            "Reports whether the currently open scene inherits from another scene, and returns the base scene path if so.",
            new JsonObject(), new JsonArray()),

        // ---------------------------------------------------------------
        // NODE  (13 tools)
        // ---------------------------------------------------------------

        BuildTool("add_node",
            "Creates a new node of the specified Godot class and attaches it as a child of an existing node in the open scene. Returns the full NodePath of the newly created node.",
            Props(
                ("parent_path", "string", "NodePath of the parent node. Use godot_get_scene_tree to find valid paths." + NodePathHint),
                ("node_type", "string", "Godot class name, e.g. \"Node3D\", \"CharacterBody2D\", \"Label\"."),
                ("node_name", "string", "Name to assign to the new node. Must be unique among siblings.")),
            Req("parent_path", "node_type", "node_name")),

        BuildTool("remove_node",
            "Removes a node and all of its children from the open scene. Use godot_get_scene_tree to confirm the path first.",
            Props(("node_path", "string", "Full NodePath of the node to remove." + NodePathHint)),
            Req("node_path")),

        BuildTool("get_node_property",
            "Reads a single property from a node in the open scene. Faster than godot_get_scene_tree when you only need one value.",
            Props(
                ("node_path", "string", "Full NodePath of the target node." + NodePathHint),
                ("property", "string", "Property name as shown in the Godot Inspector, e.g. \"position\", \"visible\".")),
            Req("node_path", "property")),

        BuildTool("set_node_property",
            "Sets a single property on a node in the open scene. Use godot_get_scene_tree with include_properties=true to discover available property names first.",
            Props(
                ("node_path", "string", "Full NodePath of the target node." + NodePathHint),
                ("property", "string", "Property name as shown in the Godot Inspector."),

                ("value", null, "New value. Supports boolean, number, string, Vector2 {x,y}, Vector3 {x,y,z}.")),
            Req("node_path", "property", "value")),

        BuildTool("duplicate_node",
            "Duplicates a node and its entire subtree, inserting the copy as a sibling. Returns the new node's NodePath.",
            Props(("node_path", "string", "NodePath of the node to duplicate." + NodePathHint)),
            Req("node_path")),

        BuildTool("reparent_node",
            "Moves a node to a new parent without destroying its children. Equivalent to dragging a node in the Scene dock.",
            Props(
                ("node_path", "string", "NodePath of the node to move." + NodePathHint),
                ("new_parent_path", "string", "NodePath of the new parent." + NodePathHint)),
            Req("node_path", "new_parent_path")),

        BuildTool("rename_node",
            "Renames a node in the open scene.",
            Props(
                ("node_path", "string", "NodePath of the node to rename." + NodePathHint),
                ("new_name", "string", "New name for the node.")),
            Req("node_path", "new_name")),

        BuildTool("move_node",
            "Changes a node's position among its siblings in the scene tree. Affects draw order for 2D nodes and processing order.",
            Props(
                ("node_path", "string", "NodePath of the node to reorder." + NodePathHint),
                ("new_index", "integer", "Zero-based index among siblings to move the node to.")),
            Req("node_path", "new_index")),

        BuildTool("get_node_groups",
            "Returns all groups a node currently belongs to.",
            Props(("node_path", "string", "Full NodePath of the node to inspect." + NodePathHint)),
            Req("node_path")),

        BuildTool("list_signals",
            "Returns all signals defined on a node, including name and parameter types. Use this before calling godot_connect_signal.",
            Props(("node_path", "string", "Full NodePath of the node to inspect." + NodePathHint)),
            Req("node_path")),

        BuildTool("connect_signal",
            "Connects a signal on a source node to a method on a target node. Use godot_list_signals to confirm the signal name first.",
            Props(
                ("source_path", "string", "NodePath of the node that emits the signal."),
                ("signal_name", "string", "Name of the signal, e.g. \"pressed\", \"body_entered\"."),
                ("target_path", "string", "NodePath of the node that receives the signal."),
                ("method_name", "string", "Method on the target node to call, e.g. \"_on_button_pressed\".")),
            Req("source_path", "signal_name", "target_path", "method_name")),

        BuildTool("add_node_to_group",
            "Adds a node to a named group. Groups let you broadcast calls to all members via get_tree().call_group(). The group is created implicitly if it doesn't exist.",
            Props(
                ("node_path", "string", "NodePath of the node to add." + NodePathHint),
                ("group_name", "string", "Group name, e.g. \"enemies\", \"pickups\".")),
            Req("node_path", "group_name")),

        BuildTool("remove_node_from_group",
            "Removes a node from a named group.",
            Props(
                ("node_path", "string", "NodePath of the node to remove." + NodePathHint),
                ("group_name", "string", "Group name to remove the node from.")),
            Req("node_path", "group_name")),

        // ---------------------------------------------------------------
        // SCRIPT  (3 tools)
        // ---------------------------------------------------------------

        BuildTool("get_script",
            "Reads and returns the full source code of a script. Provide either a res:// script path, or a node_path to read the script attached to that node.",
            Props(
                ("script_path", "string", "res:// path to the script file. Takes precedence over node_path."),
                ("node_path", "string", "NodePath of a node in the open scene — reads the attached script." + NodePathHint)),
            Req()),

        BuildTool("set_script",
            "Writes source code to a script file at the given res:// path, creating it if needed. Triggers a filesystem rescan. Use godot_attach_script to wire a new script to a node.",
            Props(
                ("script_path", "string", "res:// path of the script to write, e.g. \"res://scripts/Enemy.cs\"."),
                ("content", "string", "Full source code to write.")),
            Req("script_path", "content")),

        BuildTool("attach_script",
            "Attaches an existing script file to a node in the open scene. The script must exist on disk — use godot_set_script to create it first if needed.",
            Props(
                ("node_path", "string", "NodePath of the node to attach the script to." + NodePathHint),
                ("script_path", "string", "res:// path to the script file.")),
            Req("node_path", "script_path")),

        // ---------------------------------------------------------------
        // RESOURCE  (6 tools)
        // ---------------------------------------------------------------

        BuildTool("list_resources",
            "Returns resource files in the project as an array of res:// paths. Filter by extension to narrow results.",
            Props(
                ("directory", "string", "Optional res:// subdirectory. Defaults to res://."),
                ("extension", "string", "Optional file extension filter including the dot, e.g. \".tres\", \".png\", \".wav\".")),
            Req()),

        BuildTool("create_resource",
            "Creates a new blank .tres resource file of the given Godot resource class and saves it to disk.",
            Props(
                ("resource_type", "string", "Godot class name to instantiate, e.g. \"StandardMaterial3D\", \"AudioStreamOggVorbis\", \"Curve\"."),
                ("save_path", "string", "res:// path where the .tres file will be saved.")),
            Req("resource_type", "save_path")),

        BuildTool("list_resource_properties",
            "Returns all editor-visible properties of a loaded resource, with their current values. Use this before calling godot_set_resource_property.",
            Props(("resource_path", "string", "res:// path to the resource file.")),
            Req("resource_path")),

        BuildTool("get_resource_property",
            "Reads a single property from a Godot resource file (.tres, .res).",
            Props(
                ("resource_path", "string", "res:// path to the resource file."),
                ("property", "string", "Property name, e.g. \"albedo_color\", \"roughness\".")),
            Req("resource_path", "property")),

        BuildTool("set_resource_property",
            "Sets a property on a Godot resource file and saves it to disk. Use godot_list_resource_properties to discover available properties.",
            Props(
                ("resource_path", "string", "res:// path to the resource file."),
                ("property", "string", "Property name to set."),

                ("value", null, "New value. Supports boolean, number, string, Vector2 {x,y}, Vector3 {x,y,z}.")),
            Req("resource_path", "property", "value")),

        BuildTool("duplicate_resource",
            "Copies a resource file to a new path, creating an independent variant.",
            Props(
                ("source_path", "string", "res:// path of the resource to copy."),
                ("dest_path", "string", "res:// path for the new copy.")),
            Req("source_path", "dest_path")),

        // ---------------------------------------------------------------
        // ASSET IMPORT  (4 tools)
        // ---------------------------------------------------------------

        BuildTool("get_import_settings",
            "Reads the import settings for an asset from its .import sidecar file. These settings control texture compression, audio looping, mesh LOD, and other import-time options.",
            Props(("asset_path", "string", "res:// path to the asset, e.g. \"res://textures/hero.png\". The .import file is resolved automatically.")),
            Req("asset_path")),

        BuildTool("set_import_settings",
            "Writes import settings to an asset's .import sidecar and triggers a reimport. Use godot_get_import_settings to read existing values first.",
            Props(
                ("asset_path", "string", "res:// path to the asset."),

                ("settings", "object", "Object of key/value pairs to set, e.g. {\"params/compress/mode\": 2}. Keys are in section/key format matching the .import file.")),
            Req("asset_path", "settings")),

        BuildTool("reimport_asset",
            "Forces a specific asset to re-run its importer. Use this after manually editing an .import file or replacing a source asset on disk.",
            Props(("asset_path", "string", "res:// path of the asset to reimport.")),
            Req("asset_path")),

        BuildTool("reimport_all",
            "Triggers a full project filesystem scan and reimport of all changed assets. Equivalent to clicking the Reimport All button in the Import dock.",
            new JsonObject(), new JsonArray()),

        // ---------------------------------------------------------------
        // PROJECT SETTINGS & AUTOLOADS  (6 tools)
        // ---------------------------------------------------------------

        BuildTool("get_project_setting",
            "Reads a single setting from project.godot by its full key path. Key format: \"category/subcategory/name\", e.g. \"physics/2d/default_gravity\", \"display/window/size/viewport_width\".",
            Props(("key", "string", "Full setting key, e.g. \"application/config/name\".")),
            Req("key")),

        BuildTool("set_project_setting",
            "Writes a setting to project.godot and saves it. Use godot_get_project_setting to verify the key format first.",
            Props(
                ("key", "string", "Full setting key."),
                ("value", "string", "New value.")),
            Req("key", "value")),

        BuildTool("list_autoloads",
            "Returns all autoload singletons registered in the project, with their names, script paths, and singleton flag.",
            new JsonObject(), new JsonArray()),

        BuildTool("add_autoload",
            "Registers a new autoload singleton in the project. The script will be instantiated at startup and accessible globally by name.",
            Props(
                ("name", "string", "Global singleton name, e.g. \"GameManager\". Must be a valid identifier."),
                ("script_path", "string", "res:// path to the script, e.g. \"res://scripts/GameManager.cs\".")),
            Req("name", "script_path")),

        BuildTool("remove_autoload",
            "Removes an autoload singleton from the project by name.",
            Props(("name", "string", "The singleton name to remove.")),
            Req("name")),

        BuildTool("list_physics_layers",
            "Returns all named collision and render layers from the project's Input Map settings. Scripts reference collision layers by number — use this to map human-readable names to layer indices before writing physics code.",
            new JsonObject(), new JsonArray()),

        // ---------------------------------------------------------------
        // FILE SYSTEM  (4 tools)
        // ---------------------------------------------------------------

        BuildTool("create_directory",
            "Creates a directory (and any missing parents) within the project's res:// filesystem.",
            Props(("path", "string", "res:// path of the directory to create, e.g. \"res://scenes/levels\".")),
            Req("path")),

        BuildTool("delete_file",
            "Deletes a file from the project. Requires confirm=true as a safety gate. Does not update scene references — use with care.",
            Props(
                ("path", "string", "res:// path of the file to delete."),
                ("confirm", "boolean", "Must be explicitly set to true to execute the deletion.")),
            Req("path", "confirm")),

        BuildTool("move_file",
            "Moves or renames a file within the project. Does not update scene references that point to the old path.",
            Props(
                ("source_path", "string", "Current res:// path of the file."),
                ("dest_path", "string", "New res:// path.")),
            Req("source_path", "dest_path")),

        BuildTool("list_scripts",
            "Returns all C# script files in the project as res:// paths. Optionally filter to a subdirectory.",
            Props(("directory", "string", "Optional res:// subdirectory. Defaults to res://.")),
            Req()),

        // ---------------------------------------------------------------
        // ANIMATION  (5 tools)
        // ---------------------------------------------------------------

        BuildTool("list_animations",
            "Returns all animation clip names in an AnimationPlayer node.",
            Props(("node_path", "string", "NodePath of the AnimationPlayer node." + NodePathHint)),
            Req("node_path")),

        BuildTool("get_animation_length",
            "Returns the duration in seconds of a specific animation clip.",
            Props(
                ("node_path", "string", "NodePath of the AnimationPlayer node." + NodePathHint),
                ("animation_name", "string", "Name of the animation clip.")),
            Req("node_path", "animation_name")),

        BuildTool("create_animation",
            "Creates a new blank animation clip in an AnimationPlayer node.",
            Props(
                ("node_path", "string", "NodePath of the AnimationPlayer node." + NodePathHint),
                ("animation_name", "string", "Name for the new animation clip."),
                ("length", "number", "Duration of the animation in seconds. Defaults to 1.0.")),
            Req("node_path", "animation_name")),

        BuildTool("add_animation_track",
            "Adds a property track to an animation clip, targeting a specific node property. Returns the track index for use with godot_add_keyframe.",
            Props(
                ("node_path", "string", "NodePath of the AnimationPlayer node." + NodePathHint),
                ("animation_name", "string", "Name of the animation clip to add the track to."),
                ("track_path", "string", "NodePath:property string targeting the animated value, e.g. \"Player:position\" or \"Sprite:modulate\".")),
            Req("node_path", "animation_name", "track_path")),

        BuildTool("add_keyframe",
            "Inserts a keyframe into an animation track at the given time with the given value.",
            Props(
                ("node_path", "string", "NodePath of the AnimationPlayer node." + NodePathHint),
                ("animation_name", "string", "Name of the animation clip."),
                ("track_index", "integer", "Zero-based track index, as returned by godot_add_animation_track."),
                ("time", "number", "Time position in seconds for the keyframe."),

                ("value", "string", "Value at this keyframe. Type must match the track's target property.")),
            Req("node_path", "animation_name", "track_index", "time", "value")),

        // ---------------------------------------------------------------
        // EDITOR STATE  (4 tools)
        // ---------------------------------------------------------------

        BuildTool("get_selected_nodes",
            "Returns the NodePaths of all nodes currently selected in the editor's Scene dock. Useful for reading implicit developer context before acting.",
            new JsonObject(), new JsonArray()),

        BuildTool("select_node",
            "Selects a node in the editor's Scene dock and focuses the Inspector on it.",
            Props(("node_path", "string", "NodePath of the node to select." + NodePathHint)),
            Req("node_path")),

        BuildTool("get_editor_camera",
            "Returns the transform (position and basis) of the active 3D editor viewport camera. Useful for placing new nodes at the current view position.",
            new JsonObject(), new JsonArray()),

        BuildTool("focus_node",
            "Selects a node and frames it in the editor viewport (equivalent to pressing F in the 3D/2D view).",
            Props(("node_path", "string", "NodePath of the node to frame." + NodePathHint)),
            Req("node_path")),

        BuildTool("read_output",
            "Returns recent output from the Godot editor's game/project output log. Useful for checking what happened after running the game, viewing debug prints, or inspecting errors. Captures MCP plugin tool calls, relay messages, and printed output.",
            Props(("max_lines", "integer", "Maximum number of recent lines to return. Defaults to 100.")),
            Req()),

        BuildTool("reload_current_project",
            "Saves all open scenes and reloads the Godot project. Use this when Godot Mono's C# script compilation gets stuck or when newly created scripts don't appear in the editor. Equivalent to clicking Project > Reload Current Project in the menu. The editor will restart and re-open the project.",
            new JsonObject(), new JsonArray()),

        BuildTool("build_project",
            "Runs dotnet build on the C# project in the Godot project root. Use this after creating or modifying C# scripts to compile them into assemblies. Returns build output (stdout/stderr) and exit code.",
            new JsonObject(), new JsonArray()),

        // ---------------------------------------------------------------
        // RUNTIME TELEMETRY  (6 tools)
        // ---------------------------------------------------------------

        BuildTool("run_scene",
            "Launches the game from the editor. Use 'current' to run the open scene, 'main' to run the project's main scene.",
            Props(("mode", "string", "\"current\" to play the open scene, \"main\" to play the main scene. Defaults to \"current\".")),
            Req()),

        BuildTool("stop_scene",
            "Stops the currently running game.",
            new JsonObject(), new JsonArray()),

        BuildTool("get_runtime_node_property",
            "Reads a property from a live node while the game is running. Requires the game to be active (use godot_run_scene first).",
            Props(
                ("node_path", "string", "NodePath of the live node, rooted at the scene tree root." + NodePathHint),
                ("property", "string", "Property name to read.")),
            Req("node_path", "property")),

        BuildTool("set_runtime_node_property",
            "Sets a property on a live node while the game is running. Useful for live-tweaking values without stopping the game.",
            Props(
                ("node_path", "string", "NodePath of the live node." + NodePathHint),
                ("property", "string", "Property name to set."),
                ("value", "string", "New value.")),
            Req("node_path", "property", "value")),

        BuildTool("send_input_action",
            "Synthesizes an input action press/release while the game is running. Use this to test character controllers and input-driven logic.",
            Props(
                ("action", "string", "Input action name as defined in the Input Map, e.g. \"jump\", \"move_left\". Use godot_list_input_actions to find valid names."),
                ("pressed", "boolean", "True to press the action, false to release it.")),
            Req("action", "pressed")),

        BuildTool("get_runtime_scene_tree",
            "Returns the live scene tree as JSON while the game is running. Reflects the actual running state, including dynamically spawned nodes.",
            Props(("include_properties", "boolean", "When true, includes property values on each node.")),
            Req()),

        // ---------------------------------------------------------------
        // BUILD & EXPORT  (2 tools)
        // ---------------------------------------------------------------

        BuildTool("list_export_presets",
            "Returns all export presets configured in export_presets.cfg, with their names, target platforms, and runnable flags.",
            new JsonObject(), new JsonArray()),

        BuildTool("export_project",
            "Triggers a project export using the named preset. Shells out to the Godot executable with --export-release. Returns the output path and any errors.",
            Props(
                ("preset_name", "string", "Name of the export preset to use, as returned by godot_list_export_presets."),
                ("output_path", "string", "Absolute path where the exported build should be written.")),
            Req("preset_name", "output_path")),
    };

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static JsonObject BuildTool(string name, string description, JsonObject properties, JsonArray required)
    => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        }
    };

    /// <summary>Build a properties object from (name, type, description) tuples. type=null means no type constraint.</summary>
    private static JsonObject Props(params (string name, string? type, string description)[] props)
    {
        var obj = new JsonObject();
        foreach (var (name, type, desc) in props)
        {
            var prop = new JsonObject();
            if (type is not null) prop["type"] = type;
            prop["description"] = desc;
            obj[name] = prop;
        }
        return obj;
    }

    /// <summary>Build a required array from a list of property names.</summary>
    private static JsonArray Req(params string[] names)
    {
        var arr = new JsonArray();
        foreach (var n in names) arr.Add(n);
        return arr;
    }
}
