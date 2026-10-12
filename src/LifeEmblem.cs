using Godot;
using System.Collections.Generic;

internal static class LifeEmblem
{
    internal const float DecorationScale = 0.58f;
    internal const float DecorationAlpha = 0.28f;
    private static readonly Dictionary<(string, bool), Texture2D> Textures = new();

    internal static Color GemColor(string character) => character switch
    {
        "akari" => new Color("339cff"),
        "koharu" => new Color("28e68b"),
        "rei" => new Color("65edff"),
        _ => new Color("ff3864"),
    };

    internal static Vector2 GemCenter(string character) => character switch
    {
        "akari" => new Vector2(49, 82),
        "koharu" => new Vector2(79, 63),
        "rei" => new Vector2(67, 70),
        _ => new Vector2(87, 59),
    };

    internal static Vector2 GemRadius(string character) => character switch
    {
        "akari" => new Vector2(21, 23),
        "koharu" => new Vector2(23, 23),
        "rei" => new Vector2(22, 22),
        _ => new Vector2(24, 25),
    };

    internal static Rect2 PlayerRect(string character, float hitRadius)
    {
        var texture = Texture(character);
        var radius = GemRadius(character);
        float scale = hitRadius / Mathf.Min(radius.X, radius.Y);
        return new Rect2(-GemCenter(character) * scale, texture.GetSize() * scale);
    }

    internal static Texture2D Texture(string character, bool player = false)
    {
        if (Textures.TryGetValue((character, player), out var texture)) return texture;
        var source = GD.Load<Texture2D>($"res://char/player/{character}/{character}_core_v1.png");
        var material = new ShaderMaterial
        {
            Shader = new Shader { Code = """
                shader_type canvas_item;
                render_mode unshaded;
                uniform vec2 gem_center;
                uniform vec2 gem_radius;
                uniform vec4 gem_color;
                uniform float black_level;
                uniform bool player_marker;
                uniform float decoration_scale;
                uniform float decoration_alpha;
                void fragment() {
                    vec4 source = texture(TEXTURE, UV);
                    vec2 pixel = UV / TEXTURE_PIXEL_SIZE;
                    float distance = length((pixel - gem_center) / gem_radius);
                    float mask = 1.0 - smoothstep(0.96, 1.02, distance);
                    float light = dot(source.rgb, vec3(0.299, 0.587, 0.114));
                    float facet = clamp((light - black_level) / (1.0 - black_level), 0.0, 1.0);
                    vec3 jewel = mix(gem_color.rgb * 0.58, gem_color.rgb, smoothstep(0.0, 0.75, facet));
                    jewel = mix(jewel, mix(gem_color.rgb, vec3(1.0), 0.82), smoothstep(0.76, 1.0, facet));
                    vec4 gem = vec4(mix(source.rgb, jewel, mask), source.a);
                    if (player_marker) {
                        vec2 ornament_uv = ((pixel - gem_center) / decoration_scale + gem_center) * TEXTURE_PIXEL_SIZE;
                        vec4 ornament = texture(TEXTURE, ornament_uv);
                        if (any(lessThan(ornament_uv, vec2(0.0))) || any(greaterThan(ornament_uv, vec2(1.0)))) {
                            ornament.a = 0.0;
                        }
                        ornament.a *= decoration_alpha;
                        float frame = length((pixel - gem_center) / (gem_radius + vec2(4.0)));
                        gem.a *= 1.0 - smoothstep(0.98, 1.02, frame);
                        float alpha = gem.a + ornament.a * (1.0 - gem.a);
                        vec3 color = (gem.rgb * gem.a + ornament.rgb * ornament.a * (1.0 - gem.a)) / max(alpha, 0.001);
                        COLOR = vec4(color, alpha);
                    } else {
                        COLOR = gem;
                    }
                }
                """ },
        };
        material.SetShaderParameter("gem_center", GemCenter(character));
        material.SetShaderParameter("gem_radius", GemRadius(character));
        material.SetShaderParameter("gem_color", GemColor(character));
        material.SetShaderParameter("black_level", character == "akari" ? 0.48f : 0.2f);
        material.SetShaderParameter("player_marker", player);
        material.SetShaderParameter("decoration_scale", DecorationScale);
        material.SetShaderParameter("decoration_alpha", DecorationAlpha);
        // Only the ornament shrinks; the opaque gem still matches the collision radius.
        var viewport = new SubViewport
        {
            Name = $"LifeEmblem_{character}_{(player ? "Player" : "Hud")}", Size = new Vector2I(source.GetWidth(), source.GetHeight()),
            TransparentBg = true, Disable3D = true, World2D = new World2D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        viewport.AddChild(new Sprite2D { Texture = source, Centered = false, Material = material });
        GameManager.Instance!.AddChild(viewport);
        texture = viewport.GetTexture();
        Textures.Add((character, player), texture);
        return texture;
    }
}
