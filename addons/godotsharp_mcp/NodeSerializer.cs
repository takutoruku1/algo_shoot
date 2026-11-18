#if TOOLS
using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;

namespace GodotSharp.Plugin;

/// <summary>
/// Serializes a Godot node subtree to a JSON representation suitable for AI consumption.
/// Must be called on the main thread.
/// </summary>
public static class NodeSerializer
{
    public static JsonObject SerializeTree(Node sceneRoot, bool includeProperties = false)
    {
        return SerializeNode(sceneRoot, sceneRoot, includeProperties);
    }

    private static JsonObject SerializeNode(Node node, Node sceneRoot, bool includeProperties)
    {
        // Emit scene-root-relative paths so the AI can feed them back directly into
        // node_path parameters without any transformation.
        //   scene root  → "."
        //   direct child → "Player"
        //   nested       → "Player/Sprite2D"
        var absPath  = node.GetPath().ToString();
        var rootPath = sceneRoot.GetPath().ToString();
        var relPath  = node == sceneRoot ? "."
                     : absPath.StartsWith(rootPath + "/", StringComparison.Ordinal)
                         ? absPath[(rootPath.Length + 1)..]
                         : absPath;

        var obj = new JsonObject
        {
            ["name"] = node.Name.ToString(),
            ["type"] = node.GetClass(),
            ["path"] = relPath
        };

        // Always include the script path — agents need this to know what logic a node has
        // without needing include_properties=true for the whole tree.
        var script = node.GetScript().As<Script>();
        if (script is not null && !string.IsNullOrEmpty(script.ResourcePath))
            obj["script"] = script.ResourcePath;

        if (includeProperties)
        {
            obj["properties"] = SerializeProperties(node);
        }

        var children = new JsonArray();
        for (int i = 0; i < node.GetChildCount(); i++)
        {
            var child = node.GetChild(i);
            children.Add(SerializeNode(child, sceneRoot, includeProperties));
        }

        if (children.Count > 0)
            obj["children"] = children;

        return obj;
    }

    private static JsonObject SerializeProperties(Node node)
    {
        var props = new JsonObject();
        var propertyList = node.GetPropertyList();

        foreach (var prop in propertyList)
        {
            var name = prop["name"].AsString();
            var usage = (PropertyUsageFlags)(long)prop["usage"];

            // Only exported, non-internal properties
            if (!usage.HasFlag(PropertyUsageFlags.Editor)) continue;
            if (name.StartsWith('_') || name.Contains('/')) continue;

            try
            {
                var value = node.Get(name);
                var node_value = VariantToJsonNode(value);
                if (node_value is not null)
                    props[name] = node_value;
            }
            catch { /* skip unreadable properties */ }
        }

        return props;
    }

    public static JsonNode? VariantToJsonNode(Variant value)
    {
        return value.VariantType switch
        {
            Variant.Type.Bool    => JsonValue.Create(value.AsBool()),
            Variant.Type.Int     => JsonValue.Create(value.AsInt64()),
            Variant.Type.Float   => JsonValue.Create(value.AsDouble()),
            Variant.Type.String  => JsonValue.Create(value.AsString()),
            Variant.Type.Vector2 => new JsonObject
            {
                ["x"] = value.AsVector2().X,
                ["y"] = value.AsVector2().Y
            },
            Variant.Type.Vector3 => new JsonObject
            {
                ["x"] = value.AsVector3().X,
                ["y"] = value.AsVector3().Y,
                ["z"] = value.AsVector3().Z
            },
            Variant.Type.Color => new JsonObject
            {
                ["r"] = value.AsColor().R,
                ["g"] = value.AsColor().G,
                ["b"] = value.AsColor().B,
                ["a"] = value.AsColor().A
            },
            Variant.Type.NodePath => JsonValue.Create(value.AsNodePath().ToString()),
            Variant.Type.Nil     => null,
            _                    => JsonValue.Create(value.ToString())
        };
    }

    /// <summary>Converts a JSON node back to a Godot Variant for property setting.</summary>
    public static Variant JsonNodeToVariant(JsonNode? node, Variant.Type hint = Variant.Type.Nil)
    {
        if (node is null) return default;

        // Vector2 object
        if (node is JsonObject obj && obj.ContainsKey("x") && obj.ContainsKey("y") && !obj.ContainsKey("z"))
        {
            return new Vector2(
                obj["x"]?.GetValue<float>() ?? 0f,
                obj["y"]?.GetValue<float>() ?? 0f);
        }
        // Vector3 object
        if (node is JsonObject obj3 && obj3.ContainsKey("x") && obj3.ContainsKey("y") && obj3.ContainsKey("z"))
        {
            return new Vector3(
                obj3["x"]?.GetValue<float>() ?? 0f,
                obj3["y"]?.GetValue<float>() ?? 0f,
                obj3["z"]?.GetValue<float>() ?? 0f);
        }

        return node switch
        {
            JsonValue jv when jv.TryGetValue<bool>(out var b)    => b,
            JsonValue jv when jv.TryGetValue<long>(out var l)    => l,
            JsonValue jv when jv.TryGetValue<double>(out var d)  => d,
            JsonValue jv when jv.TryGetValue<string>(out var s)  => s,
            _ => node.ToString()
        };
    }
}

#endif
