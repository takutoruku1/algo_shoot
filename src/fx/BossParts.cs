using Godot;
using System.Collections.Generic;

// BossParts : ボス本体（Enemy）の子として1個だけぶら下がる「部品の演出層」。
//
//   v3 の素材は「本体（char/v3/boss_<name>_body_*.png）」と「部品（char/v3/fx/<name>/*.png）」に
//   分かれている。焼き込みをやめた代わりに、輪・カード・後光・光の帯といった“動くもの”は
//   ここが実行時に重ねて動かす（絵の発注方針 13「ボス形態＝本体とエフェクトの分離」）。
//
//   作法は PanSlamFx と同じ「テクスチャを読んで自分で動いて自滅する層」：
//     ・当たり判定は一切持たない（Area2D ですらない。純粋な Node2D ＋ _Draw）。
//     ・本体の座標系にぶら下がるので、ボスが徘徊すれば部品も一緒に動く。
//     ・会話中（Hud.BubblePaused）は時間を止める＝他の演出と足並みを揃える。
//
//   層と Z（本体スプライトは ZIndex -1・パネルは 0）:
//     Back(-2) → 本体(-1) → Front(0) → Add(0・加算)
//   ＝部品は弾（Z 0・World 直下で後から積まれる）や自機（Z 10）より前には出ない。
//
//   部品の定義は「人物ごとの表」（PartsOf）で持つ。表の1行＝1部品で、どの層に置くか・公転半径・
//   角速度・脈動・漂い・表示サイズを持つ。ここを触るだけで見え方を調整できる（コードは共通）。
public partial class BossParts : Node2D
{
    // ───── 部品の役割（表の読みやすさ用。挙動の分岐に使う）─────
    //   Orbit  : 本体のまわりを公転する（カード・吹き出し・視線の線）
    //   Fixed  : 本体に対して決まった位置に貼り付き、脈動だけする（スマホの画面・後光・飾り枠）
    //   Drift  : ゆっくり漂う（断片・箱・ペンライト）
    //   Ground : 足元に置く（床の輪・もや）
    //   Rise   : 上へ流れて消え、また下から湧く（粒）
    //   Blink  : 不定期に短時間だけ出る（ノイズの帯）
    private enum Role { Orbit, Fixed, Drift, Ground, Rise, Blink }

    // 描画層。Back は本体の後ろ、Front は前、Add は前かつ加算合成（光り物）。
    private enum Layer { Back, Front, Add }

    // 部品の定義（人物ごとの表の1行）。
    //   File     : char/v3/fx/<name>/<File>.png
    //   Layer    : 置く層
    //   Role     : 動きの型
    //   RadiusK  : 公転半径 ＝ 本体表示高 × RadiusK（Fixed/Ground は基準位置の距離として使う）
    //   Omega    : 角速度(rad/s)。Orbit の公転、Drift の漂いの周期に使う
    //   Phase    : 位相(rad)。同種の部品をずらして配置する
    //   SizeK    : 表示の長辺 ＝ 本体表示高 × SizeK
    //   Alpha    : 基本α
    //   PulseAmp : 脈動の振幅（0.06 なら 0.94〜1.06 倍）
    //   PulseSec : 脈動の周期(s)
    //   BobPx    : 上下の漂い幅(px・画面スケール)
    //   Pos      : Fixed/Ground/Rise/Blink の基準位置。単位は「本体表示高に対する比率」（0.28 なら本体高の28%）。
    //              比率にしてあるので body_display_h を変えても配置の見え方が崩れない。
    //              x は左右反転で符号が反転する。Ground だけは y に Configure の足元 y を使う。
    //   SwapZ    : true なら公転の奥（sin>0）で Back、手前で Front に振り分ける（あかりのカードの一重の輪）
    private readonly struct Def
    {
        public readonly string File;
        public readonly Layer L;
        public readonly Role R;
        public readonly float RadiusK, Omega, Phase, SizeK, Alpha, PulseAmp, PulseSec, BobPx;
        public readonly Vector2 Pos;
        public readonly bool SwapZ;

        public Def(string file, Layer l, Role r, float sizeK, float alpha,
                   float radiusK = 0f, float omega = 0f, float phase = 0f,
                   float pulseAmp = 0f, float pulseSec = 3f, float bobPx = 0f,
                   Vector2 pos = default, bool swapZ = false)
        {
            File = file; L = l; R = r; SizeK = sizeK; Alpha = alpha;
            RadiusK = radiusK; Omega = omega; Phase = phase;
            PulseAmp = pulseAmp; PulseSec = pulseSec; BobPx = bobPx;
            Pos = pos; SwapZ = swapZ;
        }
    }

    // 実行時の1部品。Def の値に、状態遷移（吸い寄せ／解放／散り）で動く分を足したもの。
    private sealed class Part
    {
        public Def D;
        public Texture2D Tex = null!;
        public float T;              // 生存時間(s)。公転・脈動の位相の元
        public Vector2 Extra;        // 状態遷移で足す変位（吸い寄せ・前方への流れ・散り）
        public Vector2 Vel;          // Extra の速度(px/s)
        public float Fade = 1f;      // 追加のα（散り・改心で 0 へ）
        public float Spin;           // 追加の回転(rad)
        public float SpinVel;        // 回転速度(rad/s)
        public bool Gone;            // 消え切った（改心で使う）
        public float BlinkT;         // Blink 用の次回までの残り(s)
        public float BlinkOn;        // Blink の点灯残り(s)
    }

    // 状態。EnterIdle / OnAttackStart / OnHit / OnRedeem が切り替える。
    private enum St { Idle, Wind, Release, Hit, Redeem }
    private St _st = St.Idle;
    private float _stT;

    private const float WindDur = 0.15f;    // 攻撃の予備動作（発射点へ吸い寄せる）
    private const float ReleaseDur = 0.9f;  // 解放して前方へ流れる尺（過ぎたら待機へ戻る）
    private const float HitDur = 0.5f;      // 被弾で外へ散って減衰する尺
    private const float RedeemDur = 1.2f;   // 改心で全部消えるまでの尺

    private readonly List<Part> _parts = new List<Part>();
    private Node2D _back = null!, _front = null!, _add = null!;

    private string _name = "";
    private float _bodyH = 72f;             // 本体の表示高(px)。半径・サイズの基準
    private Vector2 _muzzle;                // 発射点（本体中心からの画面 px。左右反転前の値）
    private bool _flip = true;              // 左右反転（本体スプライトの FlipH に追随）
    private float _fireDirX = -1f;          // 部品を流す向き（自機がいる左が既定）

    // 改心の完了コールバック（部品が全部消えてから呼ぶ）。
    private System.Action? _onRedeemed;
    private bool _redeemNotified;

    // ─────────────────────────────────────────────
    // 姿勢ごとのオフセット表（本体スプライトの Offset に入れる値）
    // ─────────────────────────────────────────────
    //   v3 の本体は姿勢ごとに絵の幅が違う（あかり 待機487／攻撃639／被弾553 px）。高さフィットの
    //   中央揃えで置くと、腕を伸ばした分だけ足元が横に滑って見える。各担当が実測した「足元中央」の
    //   画素座標（本体画像の左上原点・720px 高）から、待機の足元中央を基準にした差を引いて、
    //   どの姿勢でも足元が同じ画面位置に来るようにする。
    //
    //   Offset は Sprite2D のテクスチャ座標系（＝Scale が掛かる前）なので、720px 基準の画素差を
    //   そのまま入れてよい。左右反転（FlipH）すると Offset の x も一緒に反転するので、Enemy 側で
    //   反転時に x の符号を戻す（BodyOffsetFor が返すのは「反転前」の値）。
    //
    //   計算: 画像の中心を原点にした足元中央の位置 rel = 足元中央 - (幅/2, 720/2)。
    //         Offset = rel(待機) - rel(その姿勢) ＝ どの姿勢でも足元が待機と同じ画面位置に来る。
    //   実測値（足元中央 x,y／画像幅）:
    //     あかり idle(294,719)/487  attack(282,719)/639  hit(270,719)/553
    //     こはる idle(427,629)/626  attack(346,668)/585  hit(270,633)/530
    //     レイ   idle(255,719)/533  attack(218,719)/556  hit(238,719)/457
    private static readonly Dictionary<string, Vector2[]> BodyOffsets = new Dictionary<string, Vector2[]>
    {
        // 並びは Pose の順（Idle, Attack, Hit）。単位は 720px 基準の画素。
        ["akari"] = new[] { Vector2.Zero, new Vector2(88f, 0f), new Vector2(57f, 0f) },
        ["koharu"] = new[] { Vector2.Zero, new Vector2(60.5f, -39f), new Vector2(109f, -4f) },
        ["rei"] = new[] { Vector2.Zero, new Vector2(48.5f, 0f), new Vector2(-21f, 0f) },
        // 中ボス（v3 のちび・360px 高）は絵の重心が判定中心より約4px上にある（前タスクの実測）。
        // 表示高 50px に対し約8%＝無防備窓の円が胸〜頭に乗る。ここで絵を下げて円の中心へ寄せる。
        // 360px 基準の画素なので、表示高 50px なら 4px 相当は 360/50×4 ≈ 28.8px。
        ["cameo"] = new[] { new Vector2(0f, 28.8f), new Vector2(0f, 28.8f), new Vector2(0f, 28.8f) },
    };

    // 姿勢。BodyOffsetFor の添字。
    public enum Pose { Idle, Attack, Hit }

    // ───── 基準点（待機絵で実測した足元中央・発射点）─────
    //   単位は待機の本体画像（720px 高）の左上原点の画素。AnchorFoot / AnchorMuzzle が
    //   「本体中心からの画面 px」へ直して返す（オフセット表で足元は姿勢によらず待機の位置に来る）。
    private static readonly Dictionary<string, (Vector2 foot, Vector2 muzzle)> Anchors =
        new Dictionary<string, (Vector2, Vector2)>
        {
            ["akari"] = (new Vector2(294f, 719f), new Vector2(631f, 296f)),   // 発射点＝スマホの先端（攻撃絵で実測）
            ["koharu"] = (new Vector2(427f, 629f), new Vector2(573f, 4f)),    // 発射点＝ペンライトの先（攻撃絵で実測）
            ["rei"] = (new Vector2(255f, 719f), new Vector2(555f, 234f)),     // 発射点＝右手（攻撃絵で実測）
        };

    // 待機絵の画像中心を原点にした足元中央（画面 px）。bodyH＝本体の表示高。
    public static Vector2 AnchorFoot(string name, float bodyH, float idleTexW)
        => Anchors.TryGetValue(name, out var a)
            ? new Vector2(a.foot.X - idleTexW * 0.5f, a.foot.Y - 360f) * (bodyH / 720f)
            : new Vector2(0f, bodyH * 0.5f);

    // 同じく発射点（画面 px・左右反転前）。攻撃絵で実測した座標を、姿勢オフセットぶんだけ
    // 待機の座標系へ引き戻してから中心基準に直す＝待機／攻撃のどちらでも同じ点を指す。
    public static Vector2 AnchorMuzzle(string name, float bodyH, float attackTexW)
    {
        if (!Anchors.TryGetValue(name, out var a)) return new Vector2(bodyH * 0.3f, -bodyH * 0.2f);
        Vector2 off = BodyOffsetFor(name, Pose.Attack); // 攻撃姿勢を待機に合わせる補正
        return (new Vector2(a.muzzle.X - attackTexW * 0.5f, a.muzzle.Y - 360f) + off) * (bodyH / 720f);
    }

    // 指定の人物・姿勢の本体スプライト Offset（テクスチャ座標・反転前）を返す。
    // 表に無い人物は Zero＝従来どおり中央揃えのまま（既存ボスが即死しない）。
    public static Vector2 BodyOffsetFor(string name, Pose pose)
    {
        if (!BodyOffsets.TryGetValue(name, out var arr)) return Vector2.Zero;
        int i = (int)pose;
        return i >= 0 && i < arr.Length ? arr[i] : Vector2.Zero;
    }

    // ─────────────────────────────────────────────
    // 人物ごとの部品の表
    // ─────────────────────────────────────────────
    // 数値の出どころは 13（絵の発注方針）と各担当の完了報告の推奨値。
    private static Def[] PartsOf(string name) => name switch
    {
        // あかり：カードは半径 0.6×本体高の一重の輪を 8〜12 秒で1周（縦 0.28 倍の楕円は Orbit 側で潰す）。
        //   sin>0 を後ろ・≤0 を前に振り分ける（SwapZ）＝輪が本体を「くぐる」。
        //   スマホ画面3つは位置固定で脈動、断片は小さく漂う、床の円は足元、ノイズの帯は不定期に 0.1 秒。
        "akari" => new[]
        {
            new Def("card_unsent_1", Layer.Back, Role.Orbit, 0.30f, 0.85f, radiusK: 0.60f, omega: 0.63f, phase: 0.0f,
                    pulseAmp: 0.05f, pulseSec: 2.6f, bobPx: 1.5f, swapZ: true),
            new Def("card_unsent_2", Layer.Back, Role.Orbit, 0.28f, 0.85f, radiusK: 0.60f, omega: 0.63f, phase: 2.09f,
                    pulseAmp: 0.05f, pulseSec: 3.0f, bobPx: 1.5f, swapZ: true),
            new Def("card_unsent_3", Layer.Back, Role.Orbit, 0.26f, 0.85f, radiusK: 0.60f, omega: 0.63f, phase: 4.19f,
                    pulseAmp: 0.05f, pulseSec: 3.4f, bobPx: 1.5f, swapZ: true),
            new Def("phone_screen", Layer.Add, Role.Fixed, 0.34f, 0.55f,
                    pulseAmp: 0.07f, pulseSec: 2.2f, pos: new Vector2(0.278f, -0.111f)),
            new Def("phone_screen", Layer.Add, Role.Fixed, 0.22f, 0.35f,
                    pulseAmp: 0.09f, pulseSec: 2.8f, pos: new Vector2(0.417f, -0.306f)),
            new Def("phone_screen", Layer.Add, Role.Fixed, 0.18f, 0.30f,
                    pulseAmp: 0.11f, pulseSec: 3.3f, pos: new Vector2(0.167f, -0.417f)),
            new Def("piece_arm", Layer.Back, Role.Drift, 0.22f, 0.9f, omega: 0.9f, phase: 0.4f,
                    bobPx: 3f, pos: new Vector2(-0.361f, -0.083f)),
            new Def("piece_leg", Layer.Back, Role.Drift, 0.18f, 0.9f, omega: 0.7f, phase: 2.2f,
                    bobPx: 3f, pos: new Vector2(-0.278f, 0.222f)),
            new Def("piece_hem", Layer.Back, Role.Drift, 0.24f, 0.9f, omega: 0.6f, phase: 3.9f,
                    bobPx: 2.5f, pos: new Vector2(0.250f, 0.278f)),
            new Def("ring_floor", Layer.Back, Role.Ground, 0.80f, 0.55f,
                    pulseAmp: 0.06f, pulseSec: 3.0f, pos: new Vector2(0f, 0.46f)),
            new Def("read_dots", Layer.Front, Role.Fixed, 0.22f, 0.8f,
                    pulseAmp: 0.05f, pulseSec: 2.4f, pos: new Vector2(-0.250f, -0.361f)),
            new Def("glitch_band", Layer.Add, Role.Blink, 0.90f, 0.45f, pos: new Vector2(0.000f, -0.083f)),
        },

        // こはる：視線の線3〜4本を檻のように回転（横スケールを sin で伸縮＝Orbit の楕円が担う）。
        //   後光の放射（gaze_ray）はα0.15〜0.2、ペンライト3本と箱2個が漂う、もやは足元、粒は上へ流れる。
        "koharu" => new[]
        {
            new Def("gaze_line", Layer.Front, Role.Orbit, 1.05f, 0.55f, radiusK: 0.10f, omega: 0.42f, phase: 0.0f,
                    pulseAmp: 0.04f, pulseSec: 3.1f),
            new Def("gaze_line", Layer.Front, Role.Orbit, 1.00f, 0.50f, radiusK: 0.14f, omega: 0.42f, phase: 1.57f,
                    pulseAmp: 0.04f, pulseSec: 2.7f),
            new Def("gaze_line", Layer.Back, Role.Orbit, 1.05f, 0.55f, radiusK: 0.12f, omega: 0.42f, phase: 3.14f,
                    pulseAmp: 0.04f, pulseSec: 3.4f),
            new Def("gaze_line", Layer.Back, Role.Orbit, 0.95f, 0.50f, radiusK: 0.16f, omega: 0.42f, phase: 4.71f,
                    pulseAmp: 0.04f, pulseSec: 2.9f),
            new Def("gaze_ray", Layer.Add, Role.Fixed, 0.75f, 0.18f,
                    pulseAmp: 0.10f, pulseSec: 3.2f, pos: new Vector2(0.000f, -0.139f)),
            new Def("gaze_ray", Layer.Add, Role.Fixed, 0.60f, 0.15f,
                    pulseAmp: 0.12f, pulseSec: 2.5f, pos: new Vector2(0.000f, 0.056f)),
            new Def("penlight_lit", Layer.Front, Role.Drift, 0.30f, 0.9f, omega: 0.8f, phase: 0.6f,
                    bobPx: 3f, pos: new Vector2(0.333f, -0.194f)),
            new Def("penlight_off", Layer.Back, Role.Drift, 0.28f, 0.85f, omega: 0.7f, phase: 2.5f,
                    bobPx: 3f, pos: new Vector2(-0.333f, -0.056f)),
            new Def("penlight_off", Layer.Back, Role.Drift, 0.26f, 0.85f, omega: 0.6f, phase: 4.4f,
                    bobPx: 2.5f, pos: new Vector2(-0.417f, 0.194f)),
            new Def("box_small", Layer.Back, Role.Drift, 0.22f, 0.9f, omega: 0.5f, phase: 1.2f,
                    bobPx: 2.5f, pos: new Vector2(0.389f, 0.250f)),
            new Def("box_large", Layer.Back, Role.Drift, 0.28f, 0.9f, omega: 0.45f, phase: 3.6f,
                    bobPx: 2.5f, pos: new Vector2(-0.222f, 0.306f)),
            new Def("mist_dark", Layer.Back, Role.Ground, 0.85f, 0.45f,
                    pulseAmp: 0.07f, pulseSec: 3.3f, pos: new Vector2(0f, 0.44f)),
            new Def("particle_violet", Layer.Add, Role.Rise, 0.10f, 0.55f, omega: 0.9f, phase: 0.0f,
                    pos: new Vector2(-0.139f, 0.000f)),
            new Def("particle_violet", Layer.Add, Role.Rise, 0.08f, 0.50f, omega: 0.7f, phase: 2.1f,
                    pos: new Vector2(0.111f, 0.000f)),
            new Def("particle_violet", Layer.Add, Role.Rise, 0.07f, 0.45f, omega: 0.6f, phase: 4.2f,
                    pos: new Vector2(0.278f, 0.000f)),
            new Def("eye_cross", Layer.Front, Role.Fixed, 0.16f, 0.7f,
                    pulseAmp: 0.08f, pulseSec: 2.3f, pos: new Vector2(-0.306f, -0.333f)),
        },

        // レイ：飾り枠は本体の背後に本体の約1.6倍で四隅＋辺の繰り返し＋上中央の大星。
        //   吹き出しは公転（半数を奥・半数を手前）。星は散る（Drift で漂わせ、被弾で外へ飛ぶ）。
        "rei" => new[]
        {
            new Def("frame_corner", Layer.Back, Role.Fixed, 0.42f, 0.75f, pos: new Vector2(-0.60f, -0.60f)),
            new Def("frame_corner", Layer.Back, Role.Fixed, 0.42f, 0.75f, pos: new Vector2(0.60f, -0.60f)),
            new Def("frame_corner", Layer.Back, Role.Fixed, 0.42f, 0.75f, pos: new Vector2(-0.60f, 0.60f)),
            new Def("frame_corner", Layer.Back, Role.Fixed, 0.42f, 0.75f, pos: new Vector2(0.60f, 0.60f)),
            new Def("frame_edge", Layer.Back, Role.Fixed, 0.42f, 0.70f, pos: new Vector2(-0.20f, -0.62f)),
            new Def("frame_edge", Layer.Back, Role.Fixed, 0.42f, 0.70f, pos: new Vector2(0.20f, -0.62f)),
            new Def("frame_edge", Layer.Back, Role.Fixed, 0.42f, 0.70f, pos: new Vector2(-0.20f, 0.62f)),
            new Def("frame_edge", Layer.Back, Role.Fixed, 0.42f, 0.70f, pos: new Vector2(0.20f, 0.62f)),
            new Def("frame_star", Layer.Back, Role.Fixed, 0.30f, 0.85f,
                    pulseAmp: 0.06f, pulseSec: 2.8f, pos: new Vector2(0f, -0.72f)),
            new Def("bubble_empty_1", Layer.Back, Role.Orbit, 0.26f, 0.85f, radiusK: 0.66f, omega: 0.55f, phase: 0.0f,
                    pulseAmp: 0.05f, pulseSec: 2.9f, bobPx: 1.5f),
            new Def("bubble_empty_2", Layer.Back, Role.Orbit, 0.24f, 0.85f, radiusK: 0.72f, omega: 0.55f, phase: 2.09f,
                    pulseAmp: 0.05f, pulseSec: 3.2f, bobPx: 1.5f),
            new Def("bubble_empty_3", Layer.Front, Role.Orbit, 0.22f, 0.85f, radiusK: 0.66f, omega: 0.55f, phase: 3.14f,
                    pulseAmp: 0.05f, pulseSec: 2.6f, bobPx: 1.5f),
            new Def("bubble_empty_1", Layer.Front, Role.Orbit, 0.20f, 0.80f, radiusK: 0.72f, omega: 0.55f, phase: 5.24f,
                    pulseAmp: 0.05f, pulseSec: 3.4f, bobPx: 1.5f),
            new Def("star_small", Layer.Add, Role.Drift, 0.12f, 0.7f, omega: 0.9f, phase: 0.5f,
                    bobPx: 3f, pos: new Vector2(-0.361f, -0.278f)),
            new Def("star_small", Layer.Add, Role.Drift, 0.10f, 0.6f, omega: 0.7f, phase: 2.4f,
                    bobPx: 3f, pos: new Vector2(0.333f, -0.361f)),
            new Def("star_small", Layer.Add, Role.Drift, 0.09f, 0.55f, omega: 0.6f, phase: 4.3f,
                    bobPx: 2.5f, pos: new Vector2(0.417f, 0.139f)),
            new Def("ray_gold", Layer.Add, Role.Fixed, 0.70f, 0.20f,
                    pulseAmp: 0.10f, pulseSec: 3.0f, pos: new Vector2(0.000f, -0.056f)),
            new Def("ray_violet", Layer.Add, Role.Fixed, 0.60f, 0.16f,
                    pulseAmp: 0.12f, pulseSec: 2.4f, pos: new Vector2(0.000f, 0.111f)),
        },

        _ => System.Array.Empty<Def>(),
    };

    // ─────────────────────────────────────────────
    // 組み立て
    // ─────────────────────────────────────────────
    // name        : "akari" / "koharu" / "rei"（char/v3/fx/<name>/ を読む）
    // anchorFoot  : 足元中央（本体中心からの画面 px。y が正＝下）。床に置く部品の基準
    // anchorMuzzle: 発射点（本体中心からの画面 px・左右反転前）
    // bodyDisplayH: 本体の表示高(px)。半径と部品サイズの基準
    public void Configure(string name, Vector2 anchorFoot, Vector2 anchorMuzzle, float bodyDisplayH)
    {
        _name = name;
        _bodyH = Mathf.Max(1f, bodyDisplayH);
        _muzzle = anchorMuzzle;

        _back = NewLayerNode("Back", -2, false);
        _front = NewLayerNode("Front", 0, false);
        _add = NewLayerNode("Add", 0, true);

        foreach (var d in PartsOf(name))
        {
            string path = $"res://char/v3/fx/{name}/{d.File}.png";
            if (!ResourceLoader.Exists(path)) continue;   // 欠けた部品は黙って飛ばす（他が出る＝事故らない）
            var tex = ResourceLoader.Load<Texture2D>(path);
            if (tex == null) continue;
            // Pos は本体高に対する比率なので、ここで実 px に直して持たせる（描画側は px だけ見る）。
            // 床に置くもの（Ground）だけは y を実測の足元へ合わせる（絵ごとに足元の高さが違うため）。
            var def = WithPos(d, d.R == Role.Ground
                ? new Vector2(anchorFoot.X + d.Pos.X * _bodyH, anchorFoot.Y)
                : d.Pos * _bodyH);

            _parts.Add(new Part
            {
                D = def,
                Tex = tex,
                T = d.Phase,                              // 位相を初期時間にして同種部品をずらす
                BlinkT = 1.2f + (d.Phase % 1f) * 2.0f,    // Blink の初回までの間
            });
        }
    }

    // Def の Pos だけ差し替えた複製（readonly struct なので作り直す）。
    private static Def WithPos(Def d, Vector2 pos) =>
        new Def(d.File, d.L, d.R, d.SizeK, d.Alpha, d.RadiusK, d.Omega, d.Phase,
                d.PulseAmp, d.PulseSec, d.BobPx, pos, d.SwapZ);

    private Node2D NewLayerNode(string name, int z, bool additive)
    {
        var n = new PartsDraw { Owner2D = this, Name = name, ZIndex = z, LayerName = name };
        if (additive) n.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        AddChild(n);
        return n;
    }

    // 本体スプライトの左右反転に追随させる（Enemy.ApplyBossMotion と同じ faceLeft を渡す）。
    public void SetFlip(bool flipH)
    {
        _flip = flipH;
        _fireDirX = flipH ? -1f : 1f; // 向いている側へ流す
    }

    // ───── 状態遷移の口 ─────

    // 待機へ戻す（公転・脈動・漂い）。
    public void EnterIdle()
    {
        if (_st == St.Redeem) return; // 改心中は戻さない
        _st = St.Idle; _stT = 0f;
    }

    // 攻撃開始。予備動作 0.15 秒で発射点へ吸い寄せ、解放で前方へ 90〜140px/s で流す。
    public void OnAttackStart()
    {
        if (_st == St.Redeem) return;
        _st = St.Wind; _stT = 0f;
        foreach (var p in _parts) { p.Vel = Vector2.Zero; p.SpinVel = 0f; }
    }

    // 被弾。部品を外側へ散らし、HitDur 秒で減衰フェード。
    public void OnHit()
    {
        if (_st == St.Redeem) return;
        _st = St.Hit; _stT = 0f;
        for (int i = 0; i < _parts.Count; i++)
        {
            var p = _parts[i];
            Vector2 cur = p.Extra + BasePos(p, p.T);
            Vector2 dir = cur.LengthSquared() > 1f ? cur.Normalized()
                                                   : new Vector2(Mathf.Cos(i * 1.7f), Mathf.Sin(i * 1.7f));
            p.Vel = dir * (70f + (i % 5) * 14f);
            p.SpinVel = ((i % 2 == 0) ? 1f : -1f) * (1.4f + (i % 3) * 0.5f);
        }
    }

    // 改心。部品が順に消え、全部消え切ってから done を呼ぶ（本体の差し替えはその後）。
    public void OnRedeem(System.Action? done = null)
    {
        _onRedeemed = done;
        _redeemNotified = false;
        _st = St.Redeem; _stT = 0f;
        for (int i = 0; i < _parts.Count; i++)
        {
            var p = _parts[i];
            Vector2 cur = p.Extra + BasePos(p, p.T);
            Vector2 dir = cur.LengthSquared() > 1f ? cur.Normalized() : Vector2.Up;
            p.Vel = dir * (18f + (i % 4) * 6f); // ゆっくり離れながら消える（散りより穏やか）
        }
    }

    // 部品が全部消えているか（改心の順番を守るための問い合わせ）。
    public bool AllGone
    {
        get
        {
            foreach (var p in _parts) if (!p.Gone) return false;
            return true;
        }
    }

    // ───── 進行 ─────

    public override void _Process(double delta)
    {
        if (Hud.BubblePaused) return; // 会話中は他の演出と同じく時間を止める
        float dt = (float)delta;
        _stT += dt;

        switch (_st)
        {
            case St.Wind:
                TickWind(dt);
                if (_stT >= WindDur) { Release(); }
                break;
            case St.Release:
                TickFree(dt, drag: 0.6f);
                if (_stT >= ReleaseDur) EnterIdle();
                break;
            case St.Hit:
                TickFree(dt, drag: 3.4f);
                foreach (var p in _parts) p.Fade = Mathf.Max(0f, 1f - _stT / HitDur);
                if (_stT >= HitDur) { foreach (var p in _parts) { p.Fade = 1f; p.Extra = Vector2.Zero; p.Vel = Vector2.Zero; p.Spin = 0f; } EnterIdle(); }
                break;
            case St.Redeem:
                TickFree(dt, drag: 1.2f);
                TickRedeemFade();
                break;
            default:
                TickIdle(dt);
                break;
        }

        foreach (var p in _parts) p.T += dt;

        _back.QueueRedraw(); _front.QueueRedraw(); _add.QueueRedraw();
    }

    // 待機：Extra を 0 へ戻し、Blink の点滅だけ回す（公転・脈動・漂いは描画時に位相から算出する）。
    private void TickIdle(float dt)
    {
        foreach (var p in _parts)
        {
            p.Extra = p.Extra.Lerp(Vector2.Zero, 1f - Mathf.Exp(-6f * dt));
            p.Spin = Mathf.Lerp(p.Spin, 0f, 1f - Mathf.Exp(-6f * dt));
            p.Fade = Mathf.Min(1f, p.Fade + dt * 2f);
            if (p.D.R != Role.Blink) continue;
            // ノイズの帯：不定期に 0.1 秒だけ出る。
            if (p.BlinkOn > 0f) { p.BlinkOn -= dt; continue; }
            p.BlinkT -= dt;
            if (p.BlinkT <= 0f) { p.BlinkOn = 0.1f; p.BlinkT = 1.4f + (p.T % 1f) * 2.6f; }
        }
    }

    // 予備動作：発射点へ吸い寄せる（WindDur で発射点に集まる）。
    private void TickWind(float dt)
    {
        float k = 1f - Mathf.Exp(-10f * dt);
        Vector2 muzzle = MuzzleScreen();
        foreach (var p in _parts)
        {
            if (p.D.R == Role.Ground) continue; // 床の輪・もやは吸い寄せない（足元に残す）
            Vector2 want = muzzle - BasePos(p, p.T);
            p.Extra = p.Extra.Lerp(want, k);
        }
    }

    // 解放：発射点から前方（向いている側）へ 90〜140px/s で流す。
    private void Release()
    {
        _st = St.Release; _stT = 0f;
        Vector2 muzzle = MuzzleScreen();
        for (int i = 0; i < _parts.Count; i++)
        {
            var p = _parts[i];
            if (p.D.R == Role.Ground) { p.Vel = Vector2.Zero; continue; }
            float sp = 90f + (i % 6) * 10f;                       // 90〜140px/s
            float spread = (i % 5 - 2) * 0.10f;                    // 少し扇に散らす
            p.Vel = new Vector2(_fireDirX, 0f).Rotated(spread * _fireDirX) * sp;
            p.SpinVel = spread * 2.4f;
            // あかりのビームは発射点から前方へ連結（この表では beam_segment を持たないので
            // 段の連結は配線側 = ボスの弾幕演出に任せ、ここでは同じ向きへ流すだけにする）。
            if (p.D.R == Role.Fixed) p.Extra = muzzle - BasePos(p, p.T);
        }
    }

    // Extra を速度と抵抗で進める（解放・散り・改心で共通）。
    private void TickFree(float dt, float drag)
    {
        foreach (var p in _parts)
        {
            p.Extra += p.Vel * dt;
            p.Spin += p.SpinVel * dt;
            float k = Mathf.Exp(-drag * dt);
            p.Vel *= k; p.SpinVel *= k;
        }
    }

    // 改心：部品が順に（並び順で時間差をつけて）消え、全部消え切ってから完了コールバック。
    private void TickRedeemFade()
    {
        bool all = true;
        for (int i = 0; i < _parts.Count; i++)
        {
            var p = _parts[i];
            float start = i * (RedeemDur * 0.5f / Mathf.Max(1, _parts.Count)); // 順に消え始める
            float k = Mathf.Clamp((_stT - start) / (RedeemDur * 0.5f), 0f, 1f);
            p.Fade = 1f - k;
            p.Gone = p.Fade <= 0f;
            if (!p.Gone) all = false;
        }
        if (all && !_redeemNotified)
        {
            _redeemNotified = true;
            _onRedeemed?.Invoke();
            _onRedeemed = null;
        }
    }

    // 発射点（左右反転を反映した画面 px）。
    private Vector2 MuzzleScreen() => new Vector2(_flip ? -_muzzle.X : _muzzle.X, _muzzle.Y);

    // 待機時の位置（公転・漂い・固定の基準）。Extra はこれに足す。
    private Vector2 BasePos(Part p, float t)
    {
        var d = p.D;
        float sx = _flip ? -1f : 1f;
        switch (d.R)
        {
            case Role.Orbit:
            {
                float a = t * d.Omega + d.Phase;
                float r = d.RadiusK * _bodyH;
                // 縦は 0.28 倍の楕円（床に沿って回る輪に見せる）。
                return new Vector2(Mathf.Cos(a) * r * sx, Mathf.Sin(a) * r * 0.28f + Bob(d, t));
            }
            case Role.Drift:
            {
                float a = t * d.Omega + d.Phase;
                return new Vector2(d.Pos.X * sx + Mathf.Cos(a) * 3f, d.Pos.Y + Mathf.Sin(a * 0.8f) * d.BobPx);
            }
            case Role.Rise:
            {
                // 下から湧いて上へ流れ、上端で戻る（周期 2.6 秒）。
                float u = Mathf.PosMod(t * 0.38f + d.Phase * 0.16f, 1f);
                return new Vector2(d.Pos.X * sx + Mathf.Sin(t * d.Omega + d.Phase) * 4f,
                                   _bodyH * 0.42f - u * _bodyH * 0.9f);
            }
            case Role.Ground:
                return new Vector2(d.Pos.X * sx, d.Pos.Y);
            default: // Fixed / Blink
                return new Vector2(d.Pos.X * sx, d.Pos.Y + Bob(d, t));
        }
    }

    private static float Bob(Def d, float t) =>
        d.BobPx <= 0f ? 0f : Mathf.Sin(t * Mathf.Tau / Mathf.Max(0.1f, d.PulseSec)) * d.BobPx;

    // 公転の奥／手前の振り分け（SwapZ）。sin>0＝奥（Back）に置く。
    private bool IsBackNow(Part p) =>
        !p.D.SwapZ ? p.D.L == Layer.Back
                   : Mathf.Sin(p.T * p.D.Omega + p.D.Phase) > 0f;

    // 層ノードからの描画要求。name は "Back"/"Front"/"Add"。
    public void DrawLayer(Node2D canvas, string layerName)
    {
        foreach (var p in _parts)
        {
            if (p.Fade <= 0f) continue;
            if (p.D.R == Role.Blink && p.BlinkOn <= 0f) continue;

            bool back = IsBackNow(p);
            string want = p.D.L == Layer.Add ? "Add" : (back ? "Back" : "Front");
            if (want != layerName) continue;

            Vector2 pos = BasePos(p, p.T) + p.Extra;
            float pulse = p.D.PulseAmp <= 0f ? 1f
                : 1f + p.D.PulseAmp * Mathf.Sin(p.T * Mathf.Tau / Mathf.Max(0.1f, p.D.PulseSec));
            float longSide = Mathf.Max(p.Tex.GetWidth(), p.Tex.GetHeight());
            float s = p.D.SizeK * _bodyH / Mathf.Max(1f, longSide) * pulse;
            var size = new Vector2(p.Tex.GetWidth(), p.Tex.GetHeight()) * s;

            canvas.DrawSetTransform(pos, p.Spin, Vector2.One);
            canvas.DrawTextureRect(p.Tex, new Rect2(-size * 0.5f, size), false,
                                   new Color(1f, 1f, 1f, p.D.Alpha * p.Fade));
            canvas.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
    }
}

// 層ごとの描画ノード（FxLayer.AddDraw と同じ作法＝Z と合成モードを分けるためだけの薄い器）。
public partial class PartsDraw : Node2D
{
    public BossParts Owner2D = null!;
    public string LayerName = "";
    public override void _Draw() => Owner2D?.DrawLayer(this, LayerName);
}
