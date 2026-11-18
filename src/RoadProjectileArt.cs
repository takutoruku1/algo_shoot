using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class RoadProjectileArt : ProjectileTurntable
{
    private static readonly Dictionary<string, RoadProjectileArt> Cache = new();
    private static readonly string[] Akari = { "akari_docs", "akari_sticky", "akari_reply_plane",
        "enemy_akari_deadline", "enemy_akari_unsent", "enemy_akari_vacant" };
    private static readonly string[] Koharu = { "koharu_glow_arrow", "koharu_ticket", "koharu_prayer_tag",
        "enemy_koharu_cheer", "enemy_koharu_comparison", "enemy_koharu_parcel" };
    private static readonly string[] Rei = { "rei_subscriber", "rei_comment", "enemy_rei_anonymous",
        "enemy_rei_clipper", "enemy_rei_metrics" };
    private static readonly string[] Mina = { "mina_eraser", "mina_folded_note", "mina_unanswered" };
    internal static IEnumerable<string> Names => Akari.Concat(Koharu).Concat(Rei).Concat(Mina);
    private static IEnumerable<string> SceneNames(string scene) => scene switch
    {
        "Akari" => Akari,
        "Koharu" => Koharu,
        "Rei" => Rei,
        "MinaBattle" or "Final" => Mina,
        "Stage0" or "Training" or "Main" => new[] { "enemy_rei_anonymous" },
        _ => Array.Empty<string>(),
    };

    private static string Path(string name) => $"res://assets/road_projectiles/{name}.png";
    internal static IEnumerable<string> ResourcePaths(string scene) => SceneNames(scene).Select(Path);

    private RoadProjectileArt(string name) : base(name, Path(name)) { }

    private static RoadProjectileArt Load(string name)
    {
        if (!Cache.TryGetValue(name, out var art)) Cache[name] = art = new(name);
        return art;
    }

    internal static RoadProjectileArt Get(Texture2D source) => Get(BulletArt.EnemyKey(source));
    internal static RoadProjectileArt Get(string name)
    {
        var art = Load(name);
        foreach (float progress in art.Prepare()) { }
        return art;
    }

    internal static IEnumerable<float> PrepareAssetSteps(string scene)
    {
        var names = SceneNames(scene).ToArray();
        for (int index = 0; index < names.Length; index++)
            foreach (float progress in Load(names[index]).Prepare()) yield return (index + progress) / names.Length;
    }

}
