using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

internal static class BossProjectileArt
{
    private static readonly Dictionary<string, ProjectileTurntable> Models = new();
    private static readonly Dictionary<Texture2D, ProjectileTurntable?> Sources = new();
    internal static readonly string[][] Characters = {
        new[] { "akari_docs", "akari_sticky", "akari_reply_plane", "akari_envelope", "akari_clip", "akari_stamp",
            "akari_rain_drop", "akari_splash", "akari_parcel", "akari_outbox", "akari_approval_seal", "akari_address_tag", "akari_crystal",
            "akari_unsent_card", "akari_read_receipt", "akari_memo_strip", "akari_curled_memo" },
        new[] { "koharu_penlight", "koharu_acrylic", "koharu_badge", "koharu_star_pin", "koharu_ticket", "koharu_uchiwa",
            "koharu_petal", "koharu_prayer_tag", "koharu_clap", "koharu_ribbon", "koharu_heart_charm", "koharu_crystal", "koharu_gaze_eye" },
        new[] { "rei_subscriber", "rei_comment", "rei_film", "rei_microphone", "rei_bookmark", "rei_clapper", "rei_open_book", "rei_crystal", "rei_quote_pin" },
        new[] { "mina_eraser", "mina_memory", "mina_unanswered", "mina_butterfly", "mina_folded_note", "mina_photo",
            "mina_memory_drawer", "mina_snapshot_pair", "mina_reply_star", "mina_pendulum", "mina_feather", "mina_crystal" },
    };
    internal static IEnumerable<string> Names => Characters.SelectMany(names => names);
    private static readonly HashSet<string> Known = Names.ToHashSet();
    private static string Path(string name) => $"res://assets/boss_projectiles/{name}.png";

    internal static Texture2D Source(string name) => name switch
    {
        "akari_rain_drop" => GD.Load<Texture2D>("res://char/v3/fx/akari/easy_rain_drop_v1.png"),
        "akari_unsent_card" => GD.Load<Texture2D>("res://char/v3/fx/akari/card_unsent_2.png"),
        "koharu_gaze_eye" => GD.Load<Texture2D>("res://char/v3/fx/koharu/eye_cross.png"),
        "akari_crystal" => BulletArt.CharacterShot(Job.Melee).Texture,
        "koharu_crystal" => BulletArt.CharacterShot(Job.Heal).Texture,
        "rei_crystal" => BulletArt.CharacterShot(Job.Magic).Texture,
        "mina_crystal" => BulletArt.CharacterShot(Job.Tank).Texture,
        "akari_reply_plane" or "akari_splash" or "akari_parcel" or "akari_outbox" or "akari_approval_seal" or "akari_address_tag"
            or "koharu_petal" or "koharu_prayer_tag" or "koharu_clap" or "koharu_ribbon" or "koharu_heart_charm"
            or "akari_read_receipt" or "akari_memo_strip" or "akari_curled_memo" or "rei_quote_pin"
            or "rei_bookmark" or "rei_clapper" or "rei_open_book" or "mina_folded_note" or "mina_photo"
            or "mina_memory_drawer" or "mina_snapshot_pair" or "mina_reply_star" or "mina_pendulum" or "mina_feather"
            => GD.Load<Texture2D>($"res://assets/fx/{name}.svg"),
        _ => BulletArt.Get(name)!,
    };

    private static IEnumerable<string> SceneNames(string scene) => scene switch
    {
        "Akari" => Characters[0], "Koharu" => Characters[1], "Rei" => Characters[2],
        "MinaBattle" or "Final" => Characters[3],
        "BossSolo" or "BossPractice" => Characters[GameManager.Instance!.PendingBossIndex],
        "BossRush" => Names,
        _ => Array.Empty<string>(),
    };
    internal static IEnumerable<string> ResourcePaths(string scene) => SceneNames(scene).Select(Path);
    internal static IEnumerable<float> PrepareAssetSteps(string scene)
    {
        var names = SceneNames(scene).ToArray();
        for (int index = 0; index < names.Length; index++)
            foreach (float progress in Load(names[index]).Prepare()) yield return (index + progress) / names.Length;
    }
    private static ProjectileTurntable Load(string name)
    {
        // Thin wire needs fewer contour vertices to fit the 64-part collision mask.
        if (!Models.TryGetValue(name, out var model))
        {
            Models[name] = model = new(name, Path(name), name == "akari_clip" ? 1.5f : 1f);
            if (name == "rei_film")
            {
                model.DrawMaterial = (ShaderMaterial)ProjectileTurntable.Material.Duplicate();
                model.DrawMaterial.SetShaderParameter("rim_width", new Vector2(.28f, .65f));
                model.DrawMaterial.SetShaderParameter("rim_light", new Vector3(.72f, .76f, .92f));
                model.DrawMaterial.SetShaderParameter("shadow_floor", 0f);
                model.DrawMaterial.SetShaderParameter("shadow_lift", .1f);
            }
        }
        return model;
    }
    internal static ProjectileTurntable Get(string name)
    {
        var model = Load(name);
        foreach (float progress in model.Prepare()) { }
        return model;
    }
    internal static ProjectileTurntable? For(BulletArt.PlayerVisual art)
        => For(art.Texture);

    internal static ProjectileTurntable? For(Texture2D texture)
    {
        if (Sources.TryGetValue(texture, out var model)) return model;
        if (texture.ResourcePath.StartsWith("res://assets/road_projectiles/")) return Sources[texture] = null;
        string name = BulletArt.EnemyKey(texture);
        name = name switch {
            "easy_rain_drop_v1" => "akari_rain_drop",
            "card_unsent_2" => "akari_unsent_card", "eye_cross" => "koharu_gaze_eye",
            "penlight_lit" => "koharu_penlight",
            "akari_shot_v1" => "akari_crystal", "koharu_shot_v1" => "koharu_crystal",
            "rei_shot_v1" => "rei_crystal", "mina_shot_v1" => "mina_crystal", _ => name,
        };
        return Sources[texture] = Known.Contains(name) ? Get(name) : null;
    }
}
