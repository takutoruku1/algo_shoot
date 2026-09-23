using Godot;
using System;

public enum CosmeticKind { Cursor, Costume }

public sealed record CosmeticItem(string Id, string Name, CosmeticKind Kind, int Price,
    Job? Character, string Art, string Accent)
{
    public string PreviewPath => Kind == CosmeticKind.Cursor ? Art.Replace(".png", "_preview.png") : PosePath("idle");

    public string PosePath(string pose)
    {
        if (Kind == CosmeticKind.Cursor) return Art;
        if (Price > 0) return $"{Art}/{pose}.png";
        string character = Jobs.Get(Character!.Value).CharacterId;
        string suffix = pose == "idle" ? "idle_v2" : pose.Replace("aim_", "aim_v2_").Replace("spin_", "spin_v2_");
        return $"res://char/player/{character}/{character}_{suffix}.png";
    }
}

public static class Cosmetics
{
    public const string DefaultCursor = "cursor_crystal";
    public static readonly CosmeticItem[] All =
    {
        new(DefaultCursor, "クリスタル", CosmeticKind.Cursor, 0, null, "res://char/ui/cursor_refrain_v1.png", "a6dcec"),
        new("cursor_ember", "灯火の手紙", CosmeticKind.Cursor, 300, null, "res://char/ui/cursor_ember_v1.png", "f49b88"),
        new("cursor_star", "星の便り", CosmeticKind.Cursor, 300, null, "res://char/ui/cursor_star_v1.png", "f0cf82"),
        new("mina_default", "いつもの衣装", CosmeticKind.Costume, 0, Job.Tank, "", "a6dcec"),
        new("mina_starway", "星巡りのケープ", CosmeticKind.Costume, 800, Job.Tank, "res://char/player/mina/costume_v1", "a6dcec"),
        new("akari_default", "いつもの衣装", CosmeticKind.Costume, 0, Job.Melee, "", "9ed7bd"),
        new("akari_dayoff", "休日の手紙", CosmeticKind.Costume, 800, Job.Melee, "res://char/player/akari/costume_v1", "9ed7bd"),
        new("koharu_default", "いつもの衣装", CosmeticKind.Costume, 0, Job.Heal, "", "edcf82"),
        new("koharu_rain", "雨上がりの散歩", CosmeticKind.Costume, 800, Job.Heal, "res://char/player/koharu/costume_v1", "edcf82"),
        new("rei_default", "いつもの衣装", CosmeticKind.Costume, 0, Job.Magic, "", "e99acb"),
        new("rei_encore", "アンコール", CosmeticKind.Costume, 800, Job.Magic, "res://char/player/rei/costume_v1", "e99acb"),
    };

    public static CosmeticItem? Find(string id) => Array.Find(All, item => item.Id == id);
    public static CosmeticItem DefaultCostume(Job job) => Array.Find(All,
        item => item.Kind == CosmeticKind.Costume && item.Character == job && item.Price == 0)!;
}
