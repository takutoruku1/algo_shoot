using Godot;
using System.Collections.Generic;

internal sealed class PlayerProjectileArt : ProjectileTurntable
{
    private static readonly Dictionary<(Job, bool), PlayerProjectileArt> Cache = new();
    private static readonly ShaderMaterial PlayerMaterial = CreateMaterial();
    internal BulletArt.PlayerVisual Front { get; }

    private static ShaderMaterial CreateMaterial()
    {
        var material = new ShaderMaterial { Shader = Material.Shader };
        material.SetShaderParameter("rim_width", new Vector2(.15f, .35f));
        material.SetShaderParameter("shadow_floor", .18f);
        return material;
    }

    private static string Key(Job job, bool second) => Jobs.Get(job).CharacterId + (second ? "_plane" : "_shot");
    private static string Path(string key) => $"res://assets/player_projectiles/{key}.png";

    private PlayerProjectileArt(Job job, bool second) : base(Key(job, second), Path(Key(job, second)))
    {
        DrawMaterial = PlayerMaterial;
        var texture = GD.Load<Texture2D>(Path(Key(job, second) + "_front"));
        using var image = texture.GetImage();
        Front = new(texture, image.GetUsedRect(), Vector2.One * CellSize / 2, BulletArt.PlayerColor(job));
    }

    internal static PlayerProjectileArt Get(Job job, bool second = false)
    {
        if (!Cache.TryGetValue((job, second), out var art)) Cache[(job, second)] = art = new(job, second);
        return art;
    }

    internal static IEnumerable<string> ResourcePaths(Job job)
    {
        foreach (bool second in new[] { false, true })
        {
            yield return Path(Key(job, second));
            yield return Path(Key(job, second) + "_front");
        }
    }
}
