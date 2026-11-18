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
    // Helpers shared by animation tools
    // -----------------------------------------------------------------

    private static AnimationPlayer GetAnimationPlayer(string nodePath)
    {
        var node = GetNode(nodePath);
        return node as AnimationPlayer
            ?? throw new InvalidOperationException($"Node at '{nodePath}' is not an AnimationPlayer (got {node.GetClass()}).");
    }

    private static Animation GetAnimation(AnimationPlayer player, string animationName)
    {
        // Godot 4: animations are stored in AnimationLibraries
        var anim = player.GetAnimation(animationName);
        if (anim is null)
            throw new InvalidOperationException($"Animation '{animationName}' not found in AnimationPlayer at '{player.GetPath()}'.");
        return anim;
    }

    // -----------------------------------------------------------------
    // godot_list_animations
    // -----------------------------------------------------------------

    private static string AnimationList(JsonNode? args)
    {
        var nodePath = RequireString(args, "node_path");
        var player   = GetAnimationPlayer(nodePath);

        var names = player.GetAnimationList();
        var arr   = new JsonArray();
        foreach (var name in names) arr.Add(name);

        return new JsonObject { ["node_path"] = nodePath, ["animations"] = arr, ["count"] = names.Length }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_get_animation_length
    // -----------------------------------------------------------------

    private static string AnimationGetLength(JsonNode? args)
    {
        var nodePath      = RequireString(args, "node_path");
        var animationName = RequireString(args, "animation_name");

        var player = GetAnimationPlayer(nodePath);
        var anim   = GetAnimation(player, animationName);

        return new JsonObject
        {
            ["node_path"]       = nodePath,
            ["animation_name"]  = animationName,
            ["length_seconds"]  = anim.Length
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_create_animation
    // -----------------------------------------------------------------

    private static string AnimationCreate(McpPlugin plugin, JsonNode? args)
    {
        var nodePath      = RequireString(args, "node_path");
        var animationName = RequireString(args, "animation_name");
        var length        = (float)(args?["length"]?.GetValue<double>() ?? 1.0);

        var player = GetAnimationPlayer(nodePath);

        if (player.HasAnimation(animationName))
            throw new InvalidOperationException($"Animation '{animationName}' already exists.");

        var anim = new Animation { Length = length };

        // Get or create the default library ("" = global library)
        AnimationLibrary library;
        if (player.HasAnimationLibrary(""))
        {
            library = player.GetAnimationLibrary("");
        }
        else
        {
            library = new AnimationLibrary();
            var undo = plugin.GetUndoRedo();
            undo.CreateAction($"MCP: Create AnimationLibrary on '{player.Name}'",
                mergeMode: UndoRedo.MergeMode.Disable, customContext: GetSceneRoot());
            undo.AddDoMethod(player, AnimationPlayer.MethodName.AddAnimationLibrary, "", library);
            undo.AddUndoMethod(player, AnimationPlayer.MethodName.RemoveAnimationLibrary, "");
            undo.CommitAction();
        }

        var addErr = library.AddAnimation(animationName, anim);
        if (addErr != Error.Ok)
            throw new InvalidOperationException($"Failed to add animation: {addErr}");

        return new JsonObject
        {
            ["success"]        = true,
            ["node_path"]      = nodePath,
            ["animation_name"] = animationName,
            ["length_seconds"] = length
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_add_animation_track
    // -----------------------------------------------------------------

    private static string AnimationAddTrack(McpPlugin plugin, JsonNode? args)
    {
        var nodePath      = RequireString(args, "node_path");
        var animationName = RequireString(args, "animation_name");
        var trackPath     = RequireString(args, "track_path");

        var player     = GetAnimationPlayer(nodePath);
        var anim       = GetAnimation(player, animationName);
        var trackIndex = anim.AddTrack(Animation.TrackType.Value);
        anim.TrackSetPath(trackIndex, trackPath);

        return new JsonObject
        {
            ["success"]        = true,
            ["node_path"]      = nodePath,
            ["animation_name"] = animationName,
            ["track_index"]    = trackIndex,
            ["track_path"]     = trackPath
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_add_keyframe
    // -----------------------------------------------------------------

    private static string AnimationAddKeyframe(McpPlugin plugin, JsonNode? args)
    {
        var nodePath      = RequireString(args, "node_path");
        var animationName = RequireString(args, "animation_name");
        var trackIndex    = args?["track_index"]?.GetValue<int>()
            ?? throw new ArgumentException("Required parameter 'track_index' is missing.");
        var time      = (float)(args?["time"]?.GetValue<double>()
            ?? throw new ArgumentException("Required parameter 'time' is missing."));
        var valueNode = args?["value"];

        var player = GetAnimationPlayer(nodePath);
        var anim   = GetAnimation(player, animationName);

        if (trackIndex < 0 || trackIndex >= anim.GetTrackCount())
            throw new InvalidOperationException($"Track index {trackIndex} is out of range (track count: {anim.GetTrackCount()}).");

        var keyIndex = anim.TrackInsertKey(trackIndex, time, NodeSerializer.JsonNodeToVariant(valueNode));

        return new JsonObject
        {
            ["success"]        = true,
            ["node_path"]      = nodePath,
            ["animation_name"] = animationName,
            ["track_index"]    = trackIndex,
            ["key_index"]      = keyIndex,
            ["time"]           = time
        }.ToJsonString();
    }
}

#endif
