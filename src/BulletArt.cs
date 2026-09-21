using Godot;
using System.Collections.Generic;

// BulletArt : 自機弾・敵弾のテクスチャ置き場（読み込みキャッシュ）。
//
// こはるの弾は推し活グッズ、あかりの弾は仕事の書類で飛んでくる。どちらも「その人が何に
// しがみついているか」を弾そのもので言うための絵で、当たり判定・弾数・弾速には一切関与しない
// （見た目だけ。判定は Bullet の円のまま）。
//
// ・敵弾素材は char/v3/bullets/*.png（透過PNG・アニメ塗り）。
//   ゲーム内では Bullet.DrawSprite が「絵の最長辺＝当たり直径×1.35」に縮めて描くので、
//   縦長（ペンライト・クリップ）でも横長（チケット・封筒）でも判定との食い違いが同じに収まる。
// ・ResourceLoader は1回だけ走らせて static に持つ（弾は毎フレーム大量に出るのでロードは禁物）。
// ・敵弾素材が欠けている（.import 未生成など）場合は null を返し、呼び出し側は従来の弾形へ落ちる
//   ＝絵が無くてもゲームは成立する（生成前・生成失敗でも弾幕は壊れない）。
public static class BulletArt
{
    public sealed record PlayerVisual(Texture2D Texture, Rect2 Region, Vector2 Pivot, Color Accent);
    private static readonly Dictionary<Job, PlayerVisual> _playerShots = new();
    private static readonly Dictionary<Job, Texture2D> _playerMarks = new();

    public static Texture2D PlayerMark(Job job)
    {
        if (_playerMarks.TryGetValue(job, out var mark)) return mark;
        string id = Jobs.Get(job).CharacterId;
        return _playerMarks[job] = GD.Load<Texture2D>($"res://char/player/{id}/{id}_core_v1.png");
    }

    public static PlayerVisual PlayerShot(Job job)
    {
        if (_playerShots.TryGetValue(job, out var art)) return art;
        string id = Jobs.Get(job).CharacterId;
        var texture = GD.Load<Texture2D>($"res://char/player/{id}/{id}_shot_v1.png");
        using var image = texture.GetImage();
        // 尾を含む画像の中心ではなく、結晶の芯を当たり判定の中心へ合わせる。
        Vector2 pivot = job switch
        {
            Job.Melee => new(0.64f, 0.51f),
            Job.Heal => new(0.545f, 0.49f),
            Job.Magic => new(0.65f, 0.51f),
            _ => new(0.48f, 0.5f),
        };
        art = new PlayerVisual(texture, image.GetUsedRect(), pivot * texture.GetSize(), PlayerColor(job));
        _playerShots[job] = art;
        return art;
    }

    public static Color PlayerColor(Job job) => job switch
    {
        Job.Melee => new Color("ffe27a"),
        Job.Heal => new Color("91e5c5"),
        Job.Magic => new Color("c3a4fa"),
        _ => new Color("8de1ff"),
    };

    private const string Dir = "res://char/v3/bullets/";
    private static readonly Dictionary<string, Texture2D?> _cache = new();

    // 名前（拡張子なし。例 "koharu_badge"）でテクスチャを取る。未存在なら null（＝従来の弾形）。
    public static Texture2D? Get(string name)
    {
        if (_cache.TryGetValue(name, out var t)) return t;
        t = name switch
        {
            "rei_comment" => Atlas("rei_projectiles_v1", 0),
            "rei_subscriber" => Atlas("rei_projectiles_v1", 1),
            "rei_microphone" => Atlas("rei_projectiles_v1", 2),
            "rei_film" => Atlas("rei_projectiles_v1", 3),
            "mina_eraser" => Atlas("mina_projectiles_v1", 0),
            "mina_memory" => Atlas("mina_projectiles_v1", 1),
            "mina_unanswered" => Atlas("mina_projectiles_v1", 2),
            "mina_butterfly" => Atlas("mina_projectiles_v1", 3),
            "koharu_star_pin" => Atlas("koharu_star_pin_v1", 0, 1),
            _ => ResourceLoader.Load<Texture2D>(Dir + name + ".png"),
        };
        _cache[name] = t;
        return t;
    }

    private static AtlasTexture Atlas(string name, int index, int columns = 2)
    {
        var texture = Get(name)!;
        using var image = texture.GetImage();
        var size = image.GetSize() / columns;
        var origin = new Vector2I(index % columns, index / columns) * size;
        using var cell = image.GetRegion(new Rect2I(origin, size));
        var used = cell.GetUsedRect();
        return new AtlasTexture
        {
            Atlas = texture,
            Region = new Rect2(origin + used.Position, used.Size),
            FilterClip = true,
        };
    }

    public static Texture2D? PostCore(PostPool.Theme theme) => theme switch
    {
        PostPool.Theme.Akari => AkariEnvelope,
        PostPool.Theme.Koharu => KoharuTicket,
        PostPool.Theme.Rei => Get("rei_comment"),
        PostPool.Theme.Final => Get("mina_butterfly"),
        _ => Get("enemy_rei_anonymous"),
    };

    // ── こはる（我に返るわたし）＝推し活グッズ ──
    // 「推している間だけ、忘れていられる」もの。消灯したペンライトだけが“終わったあと”を指す。
    public static Texture2D? KoharuBadge    => Get("koharu_star_pin");
    public static Texture2D? KoharuAcrylic  => Get("koharu_acrylic");  // アクリルスタンド（視線のように向く）
    public static Texture2D? KoharuTicket   => Get("koharu_ticket");   // チケットの半券（期待）
    public static Texture2D? KoharuPenlight => Get("koharu_penlight"); // 消灯したペンライト（我に返る）
    public static Texture2D? KoharuUchiwa   => Get("koharu_uchiwa");   // うちわ（溢れるグッズ）

    // ── あかり（あふれるわたし）＝仕事の書類 ──
    // 送別会の夜に三秒で取り消した一通。危険な弾ほど赤い付箋側＝彩度が上がる。
    public static Texture2D? AkariSticky   => Get("akari_sticky");   // 赤い付箋（自機を向く＝こっち見て）
    public static Texture2D? AkariEnvelope => Get("akari_envelope"); // 封筒（未送信の一通）
    public static Texture2D? AkariClip     => Get("akari_clip");     // クリップ（鎖のように連なる）
    public static Texture2D? AkariDocs     => Get("akari_docs");     // 赤い付箋つきA4書類の束
    public static Texture2D? AkariStamp    => Get("akari_stamp");    // 承認印（離さない＝追尾）
}
