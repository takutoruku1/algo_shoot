using Godot;
using System.Collections.Generic;

// BulletArt : 「絵つき敵弾」のテクスチャ置き場（読み込みキャッシュ）。
//
// こはるの弾は推し活グッズ、あかりの弾は仕事の書類で飛んでくる。どちらも「その人が何に
// しがみついているか」を弾そのもので言うための絵で、当たり判定・弾数・弾速には一切関与しない
// （見た目だけ。判定は Bullet の円のまま）。
//
// ・素材は char/v3/bullets/*.png（高さ96pxの透過PNG・アニメ塗り2段・黒線なし）。
//   ゲーム内では Bullet.DrawSprite が「絵の最長辺＝当たり直径×1.35」に縮めて描くので、
//   縦長（ペンライト・クリップ）でも横長（チケット・封筒）でも判定との食い違いが同じに収まる。
// ・ResourceLoader は1回だけ走らせて static に持つ（弾は毎フレーム大量に出るのでロードは禁物）。
// ・素材が欠けている（.import 未生成など）場合は null を返し、呼び出し側は従来の弾形へ落ちる
//   ＝絵が無くてもゲームは成立する（生成前・生成失敗でも弾幕は壊れない）。
public static class BulletArt
{
    private const string Dir = "res://char/v3/bullets/";
    private static readonly Dictionary<string, Texture2D?> _cache = new();

    // 名前（拡張子なし。例 "koharu_badge"）でテクスチャを取る。未存在なら null（＝従来の弾形）。
    public static Texture2D? Get(string name)
    {
        if (_cache.TryGetValue(name, out var t)) return t;
        t = ResourceLoader.Load<Texture2D>(Dir + name + ".png");
        _cache[name] = t;
        return t;
    }

    // ── こはる（我に返るわたし）＝推し活グッズ ──
    // 「推している間だけ、忘れていられる」もの。消灯したペンライトだけが“終わったあと”を指す。
    public static Texture2D? KoharuBadge    => Get("koharu_badge");    // 缶バッジ（円＝判定と一致）
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
