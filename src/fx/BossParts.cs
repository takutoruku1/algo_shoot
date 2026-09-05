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
//     Back(-3) → 本体(-1) → Front/Add(-1・Add は加算)
//   Front/Add を Z0 に置くと弾（Z 0・World 直下で後から積まれる）と同じ層で、弾の方が後に描かれる
//   はずが本体の子（＝ボスより前）に積まれて弾を隠す場面が出た。-1 まで下げて本体と同じ層に置き、
//   弾・自機（Z 10）は必ず部品より前に来るようにする。
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
    //   MirrorX/Y: 描画時に左右／上下を反転する。1枚の角の絵（レイの frame_corner）を四隅へ回すのに使う
    //   Framed   : 枠の一部（本体の左右反転で位置が左右入れ替わるので、MirrorX も一緒に反転させて
    //              角の向きを保つ）。枠以外の部品は本体の反転で絵の向きを変えない＝既存の見え方が不変
    private readonly struct Def
    {
        public readonly string File;
        public readonly Layer L;
        public readonly Role R;
        public readonly float RadiusK, Omega, Phase, SizeK, Alpha, PulseAmp, PulseSec, BobPx;
        public readonly Vector2 Pos;
        public readonly bool SwapZ;
        public readonly bool MirrorX, MirrorY, Framed;

        public Def(string file, Layer l, Role r, float sizeK, float alpha,
                   float radiusK = 0f, float omega = 0f, float phase = 0f,
                   float pulseAmp = 0f, float pulseSec = 3f, float bobPx = 0f,
                   Vector2 pos = default, bool swapZ = false,
                   bool mirrorX = false, bool mirrorY = false, bool framed = false)
        {
            File = file; L = l; R = r; SizeK = sizeK; Alpha = alpha;
            RadiusK = radiusK; Omega = omega; Phase = phase;
            PulseAmp = pulseAmp; PulseSec = pulseSec; BobPx = bobPx;
            Pos = pos; SwapZ = swapZ; MirrorX = mirrorX; MirrorY = mirrorY; Framed = framed;
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
        public float Focus;          // 集束（0=待機の薄さ／1=発射点に集まって濃い）。こはるの後光だけ使う
    }

    // ───── 一発きりの演出（Burst）─────
    //   公転する常設の部品とは別に、攻撃・被弾・改心の瞬間だけ出して自分で消える薄い層。
    //   ビームの断片の連結、視線の線の扇、光の帯の放射、ひび、ノイズの帯がこれ。
    //   Part と違って状態を持たず、寿命(Life)を使い切ったら消える＝溜まらない。
    private sealed class Burst
    {
        public Texture2D Tex = null!;
        public Vector2 Pos;          // 本体中心からの画面 px
        public Vector2 Vel;          // px/s
        public float Angle;          // 回転(rad)
        public float Size;           // 長辺の表示 px
        public float Alpha;          // 頂点α（Life の残りで乗算する）
        public float T, Life;        // 経過／寿命(s)
        public float Delay;          // 出るまでの待ち(s)。連結を根元から順に伸ばすのに使う
        public float GrowK;          // 1秒あたりの拡大率（0＝一定）
        public bool Additive;        // true＝Add 層、false＝Front 層
    }

    private readonly List<Burst> _bursts = new List<Burst>();

    // Burst で使うテクスチャ（人物ごとに Configure で一度だけ読む。無ければ null＝その演出だけ出ない）。
    private Texture2D? _texBeam, _texGlitch, _texGazeLine,
                      _texRayGold, _texRayViolet, _texCrack, _penlightOff;

    private void LoadBurstTextures(string name)
    {
        Texture2D? L(string file)
        {
            string path = $"res://char/v3/fx/{name}/{file}.png";
            return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        }
        switch (name)
        {
            case "akari": _texBeam = L("beam_segment"); _texGlitch = L("glitch_band"); break;
            case "koharu": _texGazeLine = L("gaze_line"); _penlightOff = L("penlight_off"); break;
            case "rei": _texRayGold = L("ray_gold"); _texRayViolet = L("ray_violet"); _texCrack = L("crack"); break;
        }
    }

    private void AddBurst(Texture2D? tex, Vector2 pos, float angle, float size, float alpha, float life,
                          bool additive = true, Vector2 vel = default, float delay = 0f, float growK = 0f)
    {
        if (tex == null) return;
        _bursts.Add(new Burst
        {
            Tex = tex, Pos = pos, Angle = angle, Size = size, Alpha = alpha,
            Life = Mathf.Max(0.01f, life), Additive = additive, Vel = vel, Delay = delay, GrowK = growK,
        });
    }

    // 状態。EnterIdle / OnAttackStart / OnHit / OnRedeem が切り替える。
    private enum St { Idle, Wind, Release, Hit, Redeem }
    private St _st = St.Idle;
    private float _stT;

    private const float WindDur = 0.15f;    // 攻撃の予備動作（発射点へ吸い寄せる）
    private const float ReleaseDur = 0.9f;  // 解放して前方へ流れる尺（過ぎたら待機へ戻る）
    private const float HitDur = 0.5f;      // 被弾で外へ散って減衰する尺
    private const float RedeemDur = 1.2f;   // 改心で全部消えるまでの尺
    private const float AttackRetrigger = 0.7f; // これより早い再攻撃は同じ一拍として無視（連射で震えない）
    private const float HitRetrigger = 0.22f;   // 同じく連続被弾の間引き

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
            // 後光の放射は本体の真上に置くと顔に白飛びが乗る（実機で確認）ので、
            // 左右へ振ってα も 0.12/0.10 まで落とし、輪郭の外に薄く出るだけにする。
            new Def("gaze_ray", Layer.Add, Role.Fixed, 0.70f, 0.12f,
                    pulseAmp: 0.10f, pulseSec: 3.2f, pos: new Vector2(-0.320f, -0.180f)),
            new Def("gaze_ray", Layer.Add, Role.Fixed, 0.58f, 0.10f,
                    pulseAmp: 0.12f, pulseSec: 2.5f, pos: new Vector2(0.330f, -0.140f)),
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
            new Def("particle_violet", Layer.Add, Role.Rise, 0.09f, 0.40f, omega: 0.9f, phase: 0.0f,
                    pos: new Vector2(-0.360f, 0.000f)),
            new Def("particle_violet", Layer.Add, Role.Rise, 0.07f, 0.36f, omega: 0.7f, phase: 2.1f,
                    pos: new Vector2(0.300f, 0.000f)),
            new Def("particle_violet", Layer.Add, Role.Rise, 0.06f, 0.32f, omega: 0.6f, phase: 4.2f,
                    pos: new Vector2(0.430f, 0.000f)),
            new Def("eye_cross", Layer.Front, Role.Fixed, 0.16f, 0.7f,
                    pulseAmp: 0.08f, pulseSec: 2.3f, pos: new Vector2(-0.306f, -0.333f)),
        },

        // レイ：飾り枠は本体の背後に組む。frame_corner は 256×256 いっぱいに1つの角が描かれた
        //   「四分の一枠」なので、四隅を反転させずに位置だけで置き（絵柄は元から角の形）、
        //   角と角のすき間を frame_edge（256×51 の横長）で埋めると1枚の枠に見える。
        //   枠の内寸は半幅 0.55／半高 0.62 ×本体高。角の1枚は 0.40 ×本体高なので、
        //   角の中心＝(±0.35, ±0.42)、辺は上下のすき間（幅 0.30）を 2 枚で覆う。
        //   前担当の「薄くて小さい」指摘に対し、SizeK 0.42→0.40（辺は 0.42→0.34）でも
        //   α を 0.75→0.90 まで上げ、位置を詰めて枠として閉じたことで見え方をはっきりさせる。
        //   吹き出しは半数を奥・半数を手前で公転。星は Drift で漂い、被弾で外へ飛ぶ。
        //   光の帯（ray_gold/ray_violet）は待機では出さない＝攻撃で手から放射する固有演出に回す。
        "rei" => new[]
        {
            // 素材は左上の角1枚。反転で右上・左下・右下を作る（framed: 本体の反転にも追随させる）。
            new Def("frame_corner", Layer.Back, Role.Fixed, 0.40f, 0.90f, pos: new Vector2(-0.35f, -0.42f),
                    framed: true),
            new Def("frame_corner", Layer.Back, Role.Fixed, 0.40f, 0.90f, pos: new Vector2(0.35f, -0.42f),
                    mirrorX: true, framed: true),
            new Def("frame_corner", Layer.Back, Role.Fixed, 0.40f, 0.90f, pos: new Vector2(-0.35f, 0.42f),
                    mirrorY: true, framed: true),
            new Def("frame_corner", Layer.Back, Role.Fixed, 0.40f, 0.90f, pos: new Vector2(0.35f, 0.42f),
                    mirrorX: true, mirrorY: true, framed: true),
            new Def("frame_edge", Layer.Back, Role.Fixed, 0.34f, 0.85f, pos: new Vector2(-0.15f, -0.60f)),
            new Def("frame_edge", Layer.Back, Role.Fixed, 0.34f, 0.85f, pos: new Vector2(0.15f, -0.60f)),
            new Def("frame_edge", Layer.Back, Role.Fixed, 0.34f, 0.85f, pos: new Vector2(-0.15f, 0.60f),
                    mirrorY: true),
            new Def("frame_edge", Layer.Back, Role.Fixed, 0.34f, 0.85f, pos: new Vector2(0.15f, 0.60f),
                    mirrorY: true),
            new Def("frame_star", Layer.Back, Role.Fixed, 0.26f, 0.95f,
                    pulseAmp: 0.06f, pulseSec: 2.8f, pos: new Vector2(0f, -0.62f)),
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

        _back = NewLayerNode("Back", -3, false);
        _front = NewLayerNode("Front", -1, false);
        _add = NewLayerNode("Add", -1, true);
        LoadBurstTextures(name);

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
                d.PulseAmp, d.PulseSec, d.BobPx, pos, d.SwapZ, d.MirrorX, d.MirrorY, d.Framed);

    // File だけ差し替えた複製（被弾でペンライトを点灯→消灯に差し替えるときに使う。Pos は実 px のまま）。
    private static Def WithFile(Def d, string file) =>
        new Def(file, d.L, d.R, d.SizeK, d.Alpha, d.RadiusK, d.Omega, d.Phase,
                d.PulseAmp, d.PulseSec, d.BobPx, d.Pos, d.SwapZ, d.MirrorX, d.MirrorY, d.Framed);

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
    // 連射（スパイラルは 0.085 秒間隔）で毎発やり直すと部品が発射点に貼り付いて震えるだけになるので、
    // 一度始めたら Wind→Release が終わるまで（AttackRetrigger 秒）新しい攻撃として扱わない。
    public void OnAttackStart()
    {
        if (_st == St.Redeem) return;
        if ((_st == St.Wind || _st == St.Release) && _stT < AttackRetrigger) return;
        _st = St.Wind; _stT = 0f;
        foreach (var p in _parts) { p.Vel = Vector2.Zero; p.SpinVel = 0f; }
    }

    // 被弾。部品を外側へ散らし、HitDur 秒で減衰フェード。
    // 併せて人物ごとの一発演出（こはるはペンライトの消灯、レイは枠のひび）を出す。
    // 無防備窓では毎秒何発も当たるので、散り始めてすぐの再被弾は同じ一拍として無視する
    //（毎フレーム散らし直すと部品が原点で震えるだけになり、当たっている手応えが逆に消える）。
    public void OnHit()
    {
        if (_st == St.Redeem) return;
        if (_st == St.Hit && _stT < HitRetrigger) return;
        _st = St.Hit; _stT = 0f;
        EmitHitFx();
        for (int i = 0; i < _parts.Count; i++)
        {
            var p = _parts[i];
            // 貼り付いているもの（床の輪・もや・スマホ画面・飾り枠）は散らさない。
            // 床の輪が下へ飛ぶ／枠がバラける＝本体の立ち位置が読めなくなるため（実機で確認）。
            if (IsAnchored(p)) continue;
            Vector2 cur = p.Extra + BasePos(p, p.T);
            Vector2 dir = cur.LengthSquared() > 1f ? cur.Normalized()
                                                   : new Vector2(Mathf.Cos(i * 1.7f), Mathf.Sin(i * 1.7f));
            p.Vel = dir * (70f + (i % 5) * 14f);
            p.SpinVel = ((i % 2 == 0) ? 1f : -1f) * (1.4f + (i % 3) * 0.5f);
        }
    }

    // 改心。ひび（レイ）を先に置いてから部品が順に消え、全部消え切ってから done を呼ぶ。
    // ＝呼び出し側は done の中で本体を post へ差し替える（部品が残ったまま中の人にならない）。
    public void OnRedeem(System.Action? done = null)
    {
        _onRedeemed = done;
        _redeemNotified = false;
        _st = St.Redeem; _stT = 0f;
        EmitRedeemFx();
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
        // 会話中は他の演出と同じく時間を止める。ただし改心（Redeem）だけは止めない：
        // 完了コールバックで本体を post へ差し替える＝ここで止めると会話が続く限り中の人が出てこない。
        if (Hud.BubblePaused && _st != St.Redeem) return;
        float dt = (float)delta;
        _stT += dt;

        switch (_st)
        {
            case St.Wind:
                TickWind(dt);
                if (_stT >= WindDur) { Release(); }
                break;
            case St.Release:
                // 抵抗 0.6 では総移動が v/0.6＝150〜230px（画面幅 384px の半分以上）で、
                // カードが HUD まで飛んで戻ってこなかった（実機で確認）。4.0 なら 22〜35px＝
                // ボスの1体ぶん前へ流れて止まる＝「撃った方へ吐き出した」が読める距離に収まる。
                TickFree(dt, drag: 4.0f);
                if (_stT >= ReleaseDur) EnterIdle();
                break;
            case St.Hit:
                TickFree(dt, drag: 3.4f);
                // 散った部品だけ消していく。貼り付いているものは残す（床の輪が消えると足元が読めない）。
                foreach (var p in _parts) if (!IsAnchored(p)) p.Fade = Mathf.Max(0f, 1f - _stT / HitDur);
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
        TickBursts(dt);

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
            p.Focus = Mathf.Max(0f, p.Focus - dt * 2.5f); // 集束は待機へ戻る間にほどける
            if (p.D.R != Role.Blink) continue;
            // ノイズの帯：不定期に 0.1 秒だけ出る。
            if (p.BlinkOn > 0f) { p.BlinkOn -= dt; continue; }
            p.BlinkT -= dt;
            if (p.BlinkT <= 0f) { p.BlinkOn = 0.1f; p.BlinkT = 1.4f + (p.T % 1f) * 2.6f; }
        }
    }

    // 本体に貼り付いている部品か（スマホの画面・後光・飾り枠・床の輪・もや）。
    // これらは攻撃で飛ばさない＝発射点へ寄せると本体の顔の上で重なって白飛びする（実機で確認）。
    private static bool IsAnchored(Part p) => p.D.R == Role.Fixed || p.D.R == Role.Ground;

    // 攻撃の予備動作で例外的に発射点へ寄せる貼り付き部品。
    // こはるの後光（gaze_ray）だけは「杖の先へ集束してから撃つ」＝溜めの合図に使う（13 の演出指定）。
    private static bool IsWindPulled(Part p) => !IsAnchored(p) || p.D.File == "gaze_ray";

    // 予備動作：発射点へ吸い寄せる（WindDur で発射点に集まる）。
    // 集束する後光（gaze_ray）は寄りながら縮み、α 0.12→0.5 まで上げて「溜まっている」を見せる。
    private void TickWind(float dt)
    {
        float k = 1f - Mathf.Exp(-10f * dt);
        Vector2 muzzle = MuzzleScreen();
        float u = Mathf.Clamp(_stT / WindDur, 0f, 1f);
        for (int i = 0; i < _parts.Count; i++)
        {
            var p = _parts[i];
            if (!IsWindPulled(p)) continue;
            // 全部を同じ1点へ寄せると（こはるは 16 部品ある）加算の光が重なって白い塊になる。
            // 部品ごとに発射点まわりへ少しずらした先を狙わせて、まとまりつつ潰れないようにする。
            float a = i * 2.399f;              // 黄金角。並び順に散らばる
            Vector2 spot = muzzle + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (_bodyH * 0.09f);
            Vector2 want = spot - BasePos(p, p.T);
            p.Extra = p.Extra.Lerp(want, k);
            if (p.D.File != "gaze_ray") continue;
            p.Focus = u;                       // 1 に近いほど縮んで濃い（描画側が読む）
        }
    }

    // 解放：発射点から前方（向いている側）へ 90〜140px/s で流す。
    // 併せて人物ごとの固有演出（あかりのビーム連結／こはるの視線の扇／レイの光の帯）を発射点から出す。
    private void Release()
    {
        _st = St.Release; _stT = 0f;
        EmitAttackFx();
        for (int i = 0; i < _parts.Count; i++)
        {
            var p = _parts[i];
            if (IsAnchored(p)) { p.Vel = Vector2.Zero; continue; }
            float sp = 90f + (i % 6) * 10f;                       // 90〜140px/s
            float spread = (i % 5 - 2) * 0.10f;                    // 少し扇に散らす
            p.Vel = new Vector2(_fireDirX, 0f).Rotated(spread * _fireDirX) * sp;
            p.SpinVel = spread * 2.4f;
        }
    }

    // Extra を速度と抵抗で進める（解放・散り・改心で共通）。
    // 予備動作で寄せただけの貼り付き部品（こはるの後光）は速度を持たないので、ここで定位置へ戻す
    //（そうしないと解放の 0.9 秒ぶん杖の先に貼り付いたままになる）。
    private void TickFree(float dt, float drag)
    {
        foreach (var p in _parts)
        {
            if (IsAnchored(p) && p.Vel.LengthSquared() <= 0.01f)
                p.Extra = p.Extra.Lerp(Vector2.Zero, 1f - Mathf.Exp(-8f * dt));
            p.Extra += p.Vel * dt;
            p.Spin += p.SpinVel * dt;
            p.Focus = Mathf.Max(0f, p.Focus - dt * 2.5f);
            float k = Mathf.Exp(-drag * dt);
            p.Vel *= k; p.SpinVel *= k;
        }
    }

    // ─────────────────────────────────────────────
    // 人物ごとの固有演出（Burst）
    // ─────────────────────────────────────────────

    // 攻撃：発射点から前方へ。人物ごとに絵が違うので分ける。
    private void EmitAttackFx()
    {
        Vector2 m = MuzzleScreen();
        float dir = _fireDirX;                 // 向いている側（自機がいる左が既定で -1）
        float baseAng = dir < 0f ? Mathf.Pi : 0f; // テクスチャは右向き＝左へ撃つなら180度回す

        switch (_name)
        {
            case "akari":
            {
                // ビーム：beam_segment（256×64 の横長）を横に連結して前方へ伸ばす。
                // 根元から順に Delay をずらして置く＝1本の光が伸びていくように見え、0.4 秒で減衰。
                const int n = 6;
                float seg = _bodyH * 0.42f;    // 1枚の長さ（少し重ねて継ぎ目を消す）
                for (int i = 0; i < n; i++)
                    AddBurst(_texBeam, m + new Vector2(dir * seg * 0.86f * (i + 0.5f), 0f),
                             baseAng, seg, 0.85f - i * 0.06f, 0.40f - i * 0.02f,
                             delay: i * 0.025f);
                // ノイズの帯：撃った直後に 0.1 秒だけ本体へかぶせる（常設の Blink とは別の一発）。
                AddBurst(_texGlitch, new Vector2(0f, -_bodyH * 0.08f), 0f, _bodyH * 0.95f, 0.5f, 0.10f);
                break;
            }

            case "koharu":
            {
                // 視線の線を5本、発射点から扇状に前方へ。gaze_line は 763×6 の横長＝そのまま光条になる。
                for (int i = 0; i < 5; i++)
                {
                    float spread = (i - 2) * 0.16f;
                    AddBurst(_texGazeLine, m, baseAng + spread * dir, _bodyH * 1.5f,
                             0.75f, 0.34f, additive: true,
                             vel: new Vector2(dir, 0f).Rotated(spread * dir) * 120f,
                             delay: i * 0.02f);
                }
                break;
            }

            case "rei":
            {
                // 光の帯を手（発射点）から4本、角度を変えて放射。金と菫を交互に。
                for (int i = 0; i < 4; i++)
                {
                    float a = (i - 1.5f) * 0.30f;
                    AddBurst(i % 2 == 0 ? _texRayGold : _texRayViolet,
                             m, baseAng + a * dir, _bodyH * (1.15f - i * 0.06f),
                             0.70f, 0.42f, additive: true,
                             vel: new Vector2(dir, 0f).Rotated(a * dir) * 70f,
                             delay: i * 0.03f, growK: 0.5f);
                }
                break;
            }
        }
    }

    // 被弾：こはるはペンライトが落ち（点灯→消灯の差し替え）、レイは枠にひびが入る。
    private void EmitHitFx()
    {
        switch (_name)
        {
            case "koharu":
                // 点いていたペンライトを消灯の絵へ差し替える＝「振る手が止まった」。
                // 待機へ戻っても戻さない（戦いが進むほど暗くなる）。
                foreach (var p in _parts)
                    if (p.D.File == "penlight_lit" && _penlightOff != null)
                    {
                        p.Tex = _penlightOff;
                        p.D = WithFile(p.D, "penlight_off");
                    }
                break;
            case "rei":
                // 飾り枠に重ねてひびを1枚。枠と同じ大きさ（本体の約1.6倍）で 0.5 秒。
                AddBurst(_texCrack, Vector2.Zero, 0f, _bodyH * 1.6f, 0.55f, 0.5f, additive: false);
                break;
        }
    }

    // 改心：レイはひびが大きく走ってからガワ（部品）が落ちる。他は部品が消えるだけ。
    private void EmitRedeemFx()
    {
        if (_name != "rei") return;
        AddBurst(_texCrack, Vector2.Zero, 0f, _bodyH * 1.7f, 0.85f, RedeemDur * 0.7f, additive: false, growK: 0.25f);
    }

    // Burst を進める（Delay を消化してから寿命を減らし、切れたら捨てる）。
    private void TickBursts(float dt)
    {
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            var b = _bursts[i];
            if (b.Delay > 0f) { b.Delay -= dt; continue; }
            b.T += dt;
            b.Pos += b.Vel * dt;
            if (b.GrowK > 0f) b.Size *= 1f + b.GrowK * dt;
            if (b.T >= b.Life) _bursts.RemoveAt(i);
        }
    }

    // Burst の描画（Add / Front のどちらかへ）。寿命の残りをそのままαに掛ける＝素直に減衰する。
    private void DrawBursts(Node2D canvas, bool additive)
    {
        foreach (var b in _bursts)
        {
            if (b.Delay > 0f || b.Additive != additive) continue;
            // 前半は出したαのまま保ち、後半で 0 へ落とす。線形に落とすだけだと出た瞬間しか
            // 濃くならず、明るいボスの上では光条が見えなかった（実機で確認）。
            float u = b.T / b.Life;
            float a = b.Alpha * (u < 0.5f ? 1f : 1f - (u - 0.5f) * 2f);
            if (a <= 0f) continue;
            float longSide = Mathf.Max(b.Tex.GetWidth(), b.Tex.GetHeight());
            float s = b.Size / Mathf.Max(1f, longSide);
            var size = new Vector2(b.Tex.GetWidth(), b.Tex.GetHeight()) * s;
            canvas.DrawSetTransform(b.Pos, b.Angle, Vector2.One);
            canvas.DrawTextureRect(b.Tex, new Rect2(-size * 0.5f, size), false, new Color(1f, 1f, 1f, a));
            canvas.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
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
            // 集束（こはるの後光）：発射点へ寄る間に 0.35 倍まで縮み、α を上げて「溜まった」を見せる。
            // 上限は 0.26。後光は2枚あって加算で重なるので、0.5 まで上げると杖の先が白飛びして
            // ボスの上半身が読めなくなった（実機で確認）。縮小を強くして密度で見せる。
            float alpha = p.D.Alpha;
            if (p.Focus > 0f)
            {
                pulse *= Mathf.Lerp(1f, 0.35f, p.Focus);
                alpha = Mathf.Lerp(p.D.Alpha, 0.26f, p.Focus);
            }
            float longSide = Mathf.Max(p.Tex.GetWidth(), p.Tex.GetHeight());
            float s = p.D.SizeK * _bodyH / Mathf.Max(1f, longSide) * pulse;
            var size = new Vector2(p.Tex.GetWidth(), p.Tex.GetHeight()) * s;

            // 反転（レイの四隅）は Transform の負スケールで出す。本体の左右反転（_flip）が掛かると
            // 枠が左右で入れ替わるので、MirrorX の側も一緒に反転させて枠の向きを保つ。
            bool mx = p.D.Framed ? (p.D.MirrorX != _flip) : p.D.MirrorX;
            var mir = new Vector2(mx ? -1f : 1f, p.D.MirrorY ? -1f : 1f);
            canvas.DrawSetTransform(pos, p.Spin, mir);
            canvas.DrawTextureRect(p.Tex, new Rect2(-size * 0.5f, size), false,
                                   new Color(1f, 1f, 1f, alpha * p.Fade));
            canvas.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        // 一発きりの演出は常設部品より前（＝後）に描く。Back には出さない。
        if (layerName == "Add") DrawBursts(canvas, additive: true);
        else if (layerName == "Front") DrawBursts(canvas, additive: false);
    }
}

// 層ごとの描画ノード（FxLayer.AddDraw と同じ作法＝Z と合成モードを分けるためだけの薄い器）。
public partial class PartsDraw : Node2D
{
    public BossParts Owner2D = null!;
    public string LayerName = "";
    public override void _Draw() => Owner2D?.DrawLayer(this, LayerName);
}
