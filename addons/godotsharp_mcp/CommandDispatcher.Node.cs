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
    // Sets the owner of a node and all its descendants to sceneRoot.
    // Required so that nodes added programmatically are saved with the scene.
    private static void SetOwnerRecursive(Node node, Node sceneRoot)
    {
        node.Owner = sceneRoot;
        foreach (var child in node.GetChildren(includeInternal: true))
            if (child is Node childNode)
                SetOwnerRecursive(childNode, sceneRoot);
    }

    // -----------------------------------------------------------------
    // godot_add_node
    // -----------------------------------------------------------------

    private static string NodeAdd(McpPlugin plugin, JsonNode? args)
    {
        var parentPath = RequireString(args, "parent_path");
        var nodeType   = RequireString(args, "node_type");
        var nodeName   = RequireString(args, "node_name");

        var sceneRoot = GetSceneRoot();
        var parent    = GetNode(parentPath);
        var newNode   = (Node)ClassDB.Instantiate(nodeType).AsGodotObject();
        newNode.Name  = nodeName;

        var undo = plugin.GetUndoRedo();
        // customContext scopes the action to the scene's own undo history — required to
        // avoid native assertions when CommitAction is called from a deferred context.
        undo.CreateAction($"MCP: Add {nodeType} '{nodeName}'",
            mergeMode: UndoRedo.MergeMode.Disable, customContext: sceneRoot);
        undo.AddDoMethod(parent, Node.MethodName.AddChild, newNode);
        undo.AddDoMethod(newNode, "set_owner", sceneRoot);   // must be set for scene save
        undo.AddDoReference(newNode);
        undo.AddUndoMethod(parent, Node.MethodName.RemoveChild, newNode);
        undo.AddUndoReference(newNode);
        undo.CommitAction();

        return new JsonObject
        {
            ["success"]   = true,
            ["node_path"] = $"{parent.GetPath()}/{nodeName}",
            ["node_type"] = nodeType
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_remove_node
    // -----------------------------------------------------------------

    private static string NodeRemove(McpPlugin plugin, JsonNode? args)
    {
        var nodePath  = RequireString(args, "node_path");
        var sceneRoot = GetSceneRoot();
        var node      = GetNode(nodePath);
        var parent    = node.GetParent()
            ?? throw new InvalidOperationException($"Node '{nodePath}' has no parent and cannot be removed.");
        var oldOwner  = node.Owner;

        var undo = plugin.GetUndoRedo();
        undo.CreateAction($"MCP: Remove '{node.Name}'",
            mergeMode: UndoRedo.MergeMode.Disable, customContext: sceneRoot);
        undo.AddDoMethod(parent, Node.MethodName.RemoveChild, node);
        undo.AddUndoMethod(parent, Node.MethodName.AddChild, node);
        if (oldOwner is not null)
            undo.AddUndoMethod(node, "set_owner", oldOwner);   // restore owner on undo
        undo.AddUndoReference(node);
        undo.CommitAction();

        return new JsonObject { ["success"] = true, ["node_path"] = nodePath }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_get_node_property
    // -----------------------------------------------------------------

    private static string NodeGetProperty(JsonNode? args)
    {
        var nodePath = RequireString(args, "node_path");
        var property = RequireString(args, "property");

        var node  = GetNode(nodePath);
        var value = node.Get(property);

        return new JsonObject
        {
            ["node_path"] = nodePath,
            ["property"]  = property,
            ["value"]     = NodeSerializer.VariantToJsonNode(value)
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_set_node_property
    // -----------------------------------------------------------------

    private static string NodeSetProperty(McpPlugin plugin, JsonNode? args)
    {
        var nodePath  = RequireString(args, "node_path");
        var property  = RequireString(args, "property");
        var valueNode = args?["value"];

        var sceneRoot = GetSceneRoot();
        var node      = GetNode(nodePath);
        var oldValue  = node.Get(property);
        var newValue  = NodeSerializer.JsonNodeToVariant(valueNode);

        var undo = plugin.GetUndoRedo();
        undo.CreateAction($"MCP: Set '{property}' on '{node.Name}'",
            mergeMode: UndoRedo.MergeMode.Disable, customContext: sceneRoot);
        undo.AddDoProperty(node, property, newValue);
        undo.AddUndoProperty(node, property, oldValue);
        undo.CommitAction();

        return new JsonObject
        {
            ["success"]   = true,
            ["node_path"] = nodePath,
            ["property"]  = property,
            ["value"]     = valueNode?.DeepClone()
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_duplicate_node
    // -----------------------------------------------------------------

    private static string NodeDuplicate(McpPlugin plugin, JsonNode? args)
    {
        var nodePath  = RequireString(args, "node_path");
        var sceneRoot = GetSceneRoot();
        var node      = GetNode(nodePath);
        var parent    = node.GetParent()
            ?? throw new InvalidOperationException($"Node '{nodePath}' has no parent; cannot duplicate.");

        var clone = node.Duplicate();

        var undo = plugin.GetUndoRedo();
        undo.CreateAction($"MCP: Duplicate '{node.Name}'",
            mergeMode: UndoRedo.MergeMode.Disable, customContext: sceneRoot);
        undo.AddDoMethod(parent, Node.MethodName.AddChild, clone);
        undo.AddDoMethod(clone, "set_owner", sceneRoot);   // owner for scene save
        undo.AddDoReference(clone);
        undo.AddUndoMethod(parent, Node.MethodName.RemoveChild, clone);
        undo.AddUndoReference(clone);
        undo.CommitAction();

        return new JsonObject
        {
            ["success"]        = true,
            ["original_path"]  = nodePath,
            ["duplicate_path"] = $"{parent.GetPath()}/{clone.Name}"
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_reparent_node
    // -----------------------------------------------------------------

    private static string NodeReparent(McpPlugin plugin, JsonNode? args)
    {
        var nodePath      = RequireString(args, "node_path");
        var newParentPath = RequireString(args, "new_parent_path");

        var sceneRoot = GetSceneRoot();
        var node      = GetNode(nodePath);
        var oldParent = node.GetParent()
            ?? throw new InvalidOperationException($"Node '{nodePath}' has no parent.");
        var newParent = GetNode(newParentPath);

        var undo = plugin.GetUndoRedo();
        undo.CreateAction($"MCP: Reparent '{node.Name}' → '{newParentPath}'",
            mergeMode: UndoRedo.MergeMode.Disable, customContext: sceneRoot);
        undo.AddDoMethod(node, Node.MethodName.Reparent, newParent);
        undo.AddUndoMethod(node, Node.MethodName.Reparent, oldParent);
        undo.CommitAction();

        return new JsonObject
        {
            ["success"]         = true,
            ["node_path"]       = $"{newParent.GetPath()}/{node.Name}",
            ["new_parent_path"] = newParentPath
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_rename_node
    // -----------------------------------------------------------------

    private static string NodeRename(McpPlugin plugin, JsonNode? args)
    {
        var nodePath = RequireString(args, "node_path");
        var newName  = RequireString(args, "new_name");

        var sceneRoot = GetSceneRoot();
        var node      = GetNode(nodePath);
        var oldName   = node.Name.ToString();

        var undo = plugin.GetUndoRedo();
        undo.CreateAction($"MCP: Rename '{oldName}' → '{newName}'",
            mergeMode: UndoRedo.MergeMode.Disable, customContext: sceneRoot);
        undo.AddDoProperty(node, "name", newName);
        undo.AddUndoProperty(node, "name", oldName);
        undo.CommitAction();

        return new JsonObject
        {
            ["success"]      = true,
            ["old_name"]     = oldName,
            ["new_name"]     = newName,
            ["new_node_path"]= $"{node.GetParent()?.GetPath()}/{newName}"
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_move_node
    // -----------------------------------------------------------------

    private static string NodeMove(McpPlugin plugin, JsonNode? args)
    {
        var nodePath = RequireString(args, "node_path");
        var newIndex = args?["new_index"]?.GetValue<int>()
            ?? throw new ArgumentException("Required parameter 'new_index' is missing.");

        var sceneRoot = GetSceneRoot();
        var node      = GetNode(nodePath);
        var parent    = node.GetParent()
            ?? throw new InvalidOperationException($"Node '{nodePath}' has no parent.");
        var oldIndex  = node.GetIndex();

        var undo = plugin.GetUndoRedo();
        undo.CreateAction($"MCP: Move '{node.Name}' to index {newIndex}",
            mergeMode: UndoRedo.MergeMode.Disable, customContext: sceneRoot);
        undo.AddDoMethod(parent, Node.MethodName.MoveChild, node, newIndex);
        undo.AddUndoMethod(parent, Node.MethodName.MoveChild, node, oldIndex);
        undo.CommitAction();

        return new JsonObject
        {
            ["success"]   = true,
            ["node_path"] = nodePath,
            ["old_index"] = oldIndex,
            ["new_index"] = newIndex
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_get_node_groups
    // -----------------------------------------------------------------

    private static string NodeGetGroups(JsonNode? args)
    {
        var node   = GetNode(RequireString(args, "node_path"));
        var groups = node.GetGroups();
        var arr    = new JsonArray();
        foreach (var g in groups) arr.Add(g.ToString());
        return new JsonObject { ["node_path"] = node.GetPath().ToString(), ["groups"] = arr }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_list_signals
    // -----------------------------------------------------------------

    private static string NodeListSignals(JsonNode? args)
    {
        var node    = GetNode(RequireString(args, "node_path"));
        var signals = node.GetSignalList();
        var result  = new JsonArray();

        foreach (var sig in signals)
        {
            var sigName = sig["name"].AsString();
            var sigArgs = sig["args"].AsGodotArray();
            var argArr  = new JsonArray();

            foreach (var a in sigArgs)
            {
                var d = a.AsGodotDictionary();
                argArr.Add(new JsonObject
                {
                    ["name"] = d["name"].AsString(),
                    ["type"] = d["type"].AsInt32().ToString()
                });
            }

            result.Add(new JsonObject { ["name"] = sigName, ["args"] = argArr });
        }

        return new JsonObject { ["signals"] = result }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_connect_signal
    // -----------------------------------------------------------------

    private static string NodeConnectSignal(McpPlugin plugin, JsonNode? args)
    {
        var sourcePath = RequireString(args, "source_path");
        var signalName = RequireString(args, "signal_name");
        var targetPath = RequireString(args, "target_path");
        var methodName = RequireString(args, "method_name");

        var sceneRoot = GetSceneRoot();
        var source    = GetNode(sourcePath);
        var target    = GetNode(targetPath);
        var callable  = new Callable(target, methodName);

        var undo = plugin.GetUndoRedo();
        undo.CreateAction($"MCP: Connect {source.Name}.{signalName} → {target.Name}.{methodName}",
            mergeMode: UndoRedo.MergeMode.Disable, customContext: sceneRoot);
        undo.AddDoMethod(source, "connect", signalName, callable);
        undo.AddUndoMethod(source, "disconnect", signalName, callable);
        undo.CommitAction();

        return new JsonObject
        {
            ["success"]     = true,
            ["source_path"] = sourcePath,
            ["signal_name"] = signalName,
            ["target_path"] = targetPath,
            ["method_name"] = methodName
        }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_add_node_to_group
    // -----------------------------------------------------------------

    private static string NodeAddToGroup(McpPlugin plugin, JsonNode? args)
    {
        var nodePath  = RequireString(args, "node_path");
        var groupName = RequireString(args, "group_name");
        var sceneRoot = GetSceneRoot();
        var node      = GetNode(nodePath);

        var undo = plugin.GetUndoRedo();
        undo.CreateAction($"MCP: Add '{node.Name}' to group '{groupName}'",
            mergeMode: UndoRedo.MergeMode.Disable, customContext: sceneRoot);
        undo.AddDoMethod(node, "add_to_group", groupName, true);
        undo.AddUndoMethod(node, "remove_from_group", groupName);
        undo.CommitAction();

        return new JsonObject { ["success"] = true, ["node_path"] = nodePath, ["group"] = groupName }.ToJsonString();
    }

    // -----------------------------------------------------------------
    // godot_remove_node_from_group
    // -----------------------------------------------------------------

    private static string NodeRemoveFromGroup(McpPlugin plugin, JsonNode? args)
    {
        var nodePath  = RequireString(args, "node_path");
        var groupName = RequireString(args, "group_name");
        var sceneRoot = GetSceneRoot();
        var node      = GetNode(nodePath);

        var undo = plugin.GetUndoRedo();
        undo.CreateAction($"MCP: Remove '{node.Name}' from group '{groupName}'",
            mergeMode: UndoRedo.MergeMode.Disable, customContext: sceneRoot);
        undo.AddDoMethod(node, "remove_from_group", groupName);
        undo.AddUndoMethod(node, "add_to_group", groupName, true);
        undo.CommitAction();

        return new JsonObject { ["success"] = true, ["node_path"] = nodePath, ["group"] = groupName }.ToJsonString();
    }
}

#endif
