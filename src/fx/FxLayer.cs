using Godot;
using System;
using System.Collections.Generic;

// FxLayer : ALGO Effects Lab(standalone) の演出を Godot へ移植した視覚パーティクル基盤。
// 当たり判定なしの視覚専用。1インスタンスをMainがWorldに追加し、Instance経由で各所から呼ぶ。
// 加算ブレンドの粒は子ノード(_add)に分けて描画する。
public partial class FxLayer : Node2D
{
    public static FxLayer Instance = null!;

    // ---- 配色（fx.js の PAL を移植） ----
    public static readonly Color White   = new Color("ffffff");
    public static readonly Color Cyan    = new Color("9fe9ff");
    public static readonly Color PlayerEdge = new Color("52c4f2");
    public static readonly Color EnemyInk = new Color("0e0a16");
    public static readonly Color Edge1   = new Color("ff4d63");
    public static readonly Color Edge2   = new Color("ff5fb0");
    public static readonly Color PetalA  = new Color("ffb3d9");
    public static readonly Color PetalB  = new Color("d9b3ff");
    public static readonly Color Heart   = new Color("ff9ccf");
    public static readonly Color Mote    = new Color("ffd6ee");
    public static readonly Color Sig     = new Color("8a6fd6");
    public static readonly Color Sig2    = new Color("c3a9f0");
    public static readonly Color Gold    = new Color("ffd98a");

    public enum T { Spark, Mote, Glow, Shard, Petal, HeartP, Ring, Dmg, Sigil, BombRing, Rain, Steam, Feather, Sym, AimLine }

    // キャラ別アンビエント（ボス本体の周囲を舞う“特徴物”）。各ボスの UpdateMovement から
    // 自分の種別で BossAura(kind, GlobalPosition, dt) を毎フレーム呼ぶ。
    public enum BossAura { Rei, Akari, Koharu, Mina, Hikage }

    public class P
    {
        public T Type;
        public float X, Y, Vx, Vy, Life, Ttl = 0.5f, Size = 2f, Rot, Spin, Grav, Drag, W = 1.2f, A0 = 1f, Grow, R0, R1, Sp;
        public Color Col = White;
        public Color Edge = Edge1;
        public bool Add;
        public string Text = "";
        public bool Update(float dt)
        {
            Life += dt;
            Vy += Grav * dt;
            if (Drag > 0) { float f = Mathf.Max(0f, 1f - Drag * dt); Vx *= f; Vy *= f; }
            X += Vx * dt; Y += Vy * dt;
            Rot += Spin * dt;
            if (Type == T.BombRing) R0 += Sp * dt;
            return Life < Ttl;
        }
    }

    private readonly List<P> _p = new List<P>();
    private AddDraw _add = null!;
    private Font _font = null!;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    public override void _Ready()
    {
        Instance = this;
        ZIndex = 20;
        _rng.Randomize();
        _font = UiKit.Mono; // 非ピクセル（演出の数値・ラベル）

        _add = new AddDraw { Owner2D = this, ZIndex = 21, ZAsRelative = false };
        _add.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        AddChild(_add);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = _p.Count - 1; i >= 0; i--)
            if (!_p[i].Update(dt)) _p.RemoveAt(i);
        QueueRedraw();
        _add.QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var p in _p) if (!p.Add) DrawP(this, p, _font);
    }

    // 加算ブレンド用の子ノードから呼ばれる
    public void DrawAddParticles(Node2D c)
    {
        foreach (var p in _p) if (p.Add) DrawP(c, p, _font);
    }

    private float R(float a, float b) => (float)_rng.RandfRange(a, b);
    private int Ri(int a, int b) => _rng.RandiRange(a, b);

    // ===== スポナー（fx.js Fx.* を移植） =====

    public void Muzzle(Vector2 pos)
    {
        Add0(new P { Type = T.Ring, X = pos.X, Y = pos.Y, R0 = 1, R1 = 7, Ttl = 0.10f, Col = Cyan, W = 1.4f, A0 = 0.95f, Add = true });
        Add0(new P { Type = T.Glow, X = pos.X, Y = pos.Y, Size = 5, Ttl = 0.10f, Col = White, Add = true, Grow = 0.4f });
        for (int i = 0; i < 4; i++)
        {
            float a = R(-0.5f, 0.5f), sp = R(60, 150);
            Add0(new P { Type = T.Spark, X = pos.X, Y = pos.Y, Vx = Mathf.Cos(a) * sp, Vy = Mathf.Sin(a) * sp, Size = 4, W = 1, Ttl = R(0.10f, 0.2f), Col = Cyan, Drag = 6, Add = true });
        }
    }

    // モード別マズル：弾本体は触らず、発砲の“手元”だけでモード（連射/拡散/ホーミング/加速球）と全開を描き分ける。
    //   色は水色ロック（§2）の範囲、金は全開の一瞬だけ（浄化色を「フルパワーの手元」で使う）。
    //   全て加算プール（Add=true→ZIndex21）＝弾レイヤー(Z0)には何も足さない＝ヒエラルキー維持。
    public void Muzzle(Vector2 pos, GameManager.ShotMode mode, int spreadWays, bool overload)
    {
        // 共通の芯グロー（連射ベース）。
        Add0(new P { Type = T.Glow, X = pos.X, Y = pos.Y, Size = 5, Ttl = 0.10f, Col = White, Add = true, Grow = 0.4f });

        switch (mode)
        {
            case GameManager.ShotMode.Spread:
            {
                // 拡散：リングを一回り大きく開き、各way角へ粒を散らして「広がる」を予感させる。
                Add0(new P { Type = T.Ring, X = pos.X, Y = pos.Y, R0 = 1, R1 = 9, Ttl = 0.10f, Col = Cyan, W = 1.4f, A0 = 0.95f, Add = true });
                int n = Mathf.Clamp(spreadWays, 3, 9);
                for (int i = 0; i < n; i++)
                {
                    float t = n == 1 ? 0f : (float)i / (n - 1) - 0.5f;
                    float a = t * Mathf.DegToRad(70f), sp = R(90, 150);
                    Add0(new P { Type = T.Spark, X = pos.X, Y = pos.Y, Vx = Mathf.Cos(a) * sp, Vy = Mathf.Sin(a) * sp, Size = 4, W = 1, Ttl = R(0.08f, 0.16f), Col = Cyan, Drag = 6, Add = true });
                }
                break;
            }
            case GameManager.ShotMode.Homing:
            {
                // ホーミング：中心グローを淡ピンク（Sig2）に＝「追って届ける」優しさ。接線方向の粒で「曲がる弾」を予告。
                Add0(new P { Type = T.Glow, X = pos.X, Y = pos.Y, Size = 6, Ttl = 0.11f, Col = Sig2, Add = true, Grow = 0.6f });
                Add0(new P { Type = T.Ring, X = pos.X, Y = pos.Y, R0 = 1, R1 = 7, Ttl = 0.10f, Col = Sig2, W = 1.3f, A0 = 0.9f, Add = true });
                for (int i = 0; i < 3; i++)
                {
                    float a = R(0f, Mathf.Tau), sp = R(50, 100);
                    // 接線方向（周回感）＝直進でなく“曲がって届く”気配。
                    var tan = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * sp;
                    Add0(new P { Type = T.Spark, X = pos.X + Mathf.Cos(a) * 3f, Y = pos.Y + Mathf.Sin(a) * 3f, Vx = tan.X, Vy = tan.Y, Size = 3.5f, W = 1, Ttl = R(0.10f, 0.18f), Col = Sig2, Drag = 5, Add = true });
                }
                break;
            }
            case GameManager.ShotMode.Accel:
            {
                // 加速球：タメて撃つロケット弾。連射よりリングを一回り大きく・厚く・長めに開いて「力を溜めて放つ」重さを出す。
                // 粒は連射より少なく・大きく・遅く（Drag弱め）で、飛び出すというより押し出されるような尾を引かせる。
                Add0(new P { Type = T.Ring, X = pos.X, Y = pos.Y, R0 = 2, R1 = 10, Ttl = 0.14f, Col = Cyan, W = 2.0f, A0 = 0.95f, Add = true });
                for (int i = 0; i < 3; i++)
                {
                    float a = R(-0.15f, 0.15f), sp = R(40, 90);
                    Add0(new P { Type = T.Spark, X = pos.X, Y = pos.Y, Vx = Mathf.Cos(a) * sp, Vy = Mathf.Sin(a) * sp, Size = 5.5f, W = 1.4f, Ttl = R(0.14f, 0.22f), Col = Cyan, Drag = 3, Add = true });
                }
                break;
            }
            default: // Rapid（連射）
            {
                // 連射：リングを ease-out で開き、前方Sparkを銃口方向±0.25radに絞って「射線」を出す。
                Add0(new P { Type = T.Ring, X = pos.X, Y = pos.Y, R0 = 1, R1 = 6, Ttl = 0.08f, Col = Cyan, W = 1.6f, A0 = 0.95f, Add = true });
                for (int i = 0; i < 2; i++)
                {
                    float a = R(-0.25f, 0.25f), sp = R(90, 150);
                    Add0(new P { Type = T.Spark, X = pos.X, Y = pos.Y, Vx = Mathf.Cos(a) * sp, Vy = Mathf.Sin(a) * sp, Size = 4, W = 1, Ttl = R(0.08f, 0.14f), Col = Cyan, Drag = 6, Add = true });
                }
                break;
            }
        }

        // 全開：やさしさ全開＝攻撃も浄化色（金）に染まる。既存の全開オーラ（金二重リング）と語彙統一。
        if (overload)
            Add0(new P { Type = T.Ring, X = pos.X, Y = pos.Y, R0 = 2, R1 = 12, Ttl = 0.12f, Col = Gold, W = 1.8f, A0 = 0.9f, Add = true });
    }

    public void Shatter(Vector2 pos)
    {
        int n = Ri(5, 8);
        for (int i = 0; i < n; i++)
        {
            float a = R(0, Mathf.Tau), sp = R(40, 130);
            Add0(new P { Type = T.Shard, X = pos.X, Y = pos.Y, Vx = Mathf.Cos(a) * sp, Vy = Mathf.Sin(a) * sp - 20, Size = R(1.6f, 3.4f), Rot = R(0, Mathf.Tau), Spin = R(-9, 9), Grav = 240, Drag = 1.2f, Ttl = R(0.35f, 0.55f), Col = EnemyInk, Edge = _rng.Randf() < 0.5f ? Edge1 : Edge2 });
        }
        KindnessMote(pos + new Vector2(R(-3, 3), 0));
    }

    public void KindnessMote(Vector2 pos)
    {
        Add0(new P { Type = T.Mote, X = pos.X, Y = pos.Y, Vx = R(-8, 8), Vy = R(-34, -22), Size = R(2.2f, 3.2f), Drag = 0.8f, Ttl = R(0.4f, 0.55f), Col = Mote, Add = true });
    }

    public void Graze(Vector2 pos)
    {
        Add0(new P { Type = T.Ring, X = pos.X, Y = pos.Y, R0 = 4, R1 = 13, Ttl = 0.26f, Col = Cyan, W = 1.2f, A0 = 0.9f, Add = true });
        Add0(new P { Type = T.Glow, X = pos.X, Y = pos.Y, Size = 4, Ttl = 0.2f, Col = Cyan, Add = true });
        for (int i = 0; i < 3; i++)
        {
            float a = R(0, Mathf.Tau), sp = R(30, 70);
            Add0(new P { Type = T.Spark, X = pos.X, Y = pos.Y, Vx = Mathf.Cos(a) * sp, Vy = Mathf.Sin(a) * sp, Size = 3, Ttl = 0.22f, Col = White, Drag = 5, Add = true });
        }
    }

    public void PlayerHit(Vector2 pos)
    {
        Add0(new P { Type = T.Glow, X = pos.X, Y = pos.Y, Size = 14, Ttl = 0.18f, Col = White, Add = true, Grow = 0.3f });
        Add0(new P { Type = T.Ring, X = pos.X, Y = pos.Y, R0 = 3, R1 = 22, Ttl = 0.4f, Col = new Color("ffd0d8"), W = 1.4f, A0 = 0.85f, Add = true });
        Add0(new P { Type = T.Ring, X = pos.X, Y = pos.Y, R0 = 1, R1 = 14, Ttl = 0.3f, Col = White, W = 1, A0 = 0.7f, Add = true });
        GameCamera.Instance?.Shake(3.2f, 0.18f);
    }

    public void PurifyBurst(Vector2 pos)
    {
        Add0(new P { Type = T.Glow, X = pos.X, Y = pos.Y, Size = 16, Ttl = 0.45f, Col = Sig2, Add = true, Grow = 0.5f });
        Add0(new P { Type = T.Ring, X = pos.X, Y = pos.Y, R0 = 2, R1 = 30, Ttl = 0.6f, Col = Sig2, W = 1.4f, A0 = 0.9f, Add = true });
        int n = Ri(10, 16);
        for (int i = 0; i < n; i++)
        {
            float a = -Mathf.Pi / 2 + R(-1.2f, 1.2f), sp = R(45, 110);
            bool heart = _rng.Randf() < 0.35f;
            Add0(new P { Type = heart ? T.HeartP : T.Petal, X = pos.X, Y = pos.Y, Vx = Mathf.Cos(a) * sp, Vy = Mathf.Sin(a) * sp, Size = R(2.2f, 4f), Rot = R(0, Mathf.Tau), Spin = R(-5, 5), Grav = 70, Drag = 0.7f, Ttl = R(0.6f, 1.0f), Col = heart ? Heart : (_rng.Randf() < 0.5f ? PetalA : PetalB) });
        }
        for (int i = 0; i < 6; i++)
        {
            float a = R(0, Mathf.Tau), sp = R(40, 90);
            Add0(new P { Type = T.Mote, X = pos.X, Y = pos.Y, Vx = Mathf.Cos(a) * sp, Vy = Mathf.Sin(a) * sp - 15, Size = R(1.6f, 2.6f), Drag = 1.2f, Ttl = R(0.5f, 0.8f), Col = Mote, Add = true });
        }
    }

    public void BulletToPetal(Vector2 pos)
    {
        Add0(new P { Type = T.Petal, X = pos.X, Y = pos.Y, Vx = R(-20, 20), Vy = R(-50, -15), Size = R(2.4f, 4f), Rot = R(0, Mathf.Tau), Spin = R(-6, 6), Grav = 65, Drag = 0.7f, Ttl = R(0.7f, 1.1f), Col = _rng.Randf() < 0.5f ? PetalA : PetalB });
        Add0(new P { Type = T.Glow, X = pos.X, Y = pos.Y, Size = 4, Ttl = 0.2f, Col = Mote, Add = true });
    }

    // 道中ザコの攻撃予告（テレグラフ）。
    // AimLine : 発射源 pos から dir 方向へ伸びる細い照準線（ロックオン連射の予告）。短尺。
    public void AimLine(Vector2 pos, Vector2 dir, float ttl, Color col)
    {
        float len = 220f; // 画面を貫く長さ（はみ出しは画面外で見えないだけ）
        var d = dir.LengthSquared() > 0.0001f ? dir.Normalized() : new Vector2(-1, 0);
        Add0(new P { Type = T.AimLine, X = pos.X, Y = pos.Y, Vx = d.X, Vy = d.Y,
            Size = len, W = 1.0f, Ttl = ttl, Col = col, A0 = 0.85f, Add = true });
        // 発射源の小さな点滅グロー（“狙っている”合図）。
        Add0(new P { Type = T.Glow, X = pos.X, Y = pos.Y, Size = 4, Ttl = Mathf.Min(0.25f, ttl), Col = col, Add = true });
    }

    // AimFlash : 発射源で短く弾ける小白フラッシュ＋リング（高速鋭3WAY の溜め予告）。
    public void AimFlash(Vector2 pos, Color col)
    {
        Add0(new P { Type = T.Glow, X = pos.X, Y = pos.Y, Size = 6, Ttl = 0.4f, Col = White, Add = true, Grow = 0.5f });
        Add0(new P { Type = T.Ring, X = pos.X, Y = pos.Y, R0 = 2, R1 = 12, Ttl = 0.4f, Col = col, W = 1.2f, A0 = 0.9f, Add = true });
    }

    // AOE着弾スパーク（AreaStrike の DrawStrike から1回だけ呼ぶ）。
    //   加算プール（ZIndex21）＝弾レイヤーには何も足さない。全部 0.28s 未満の短命＝視認性を長く侵さない。
    //   dirBias: ビーム系は軸方向へ飛ばす（Vector2.Zero で全方位）。scale: 形状の大きさに応じた飛距離。
    //   色は AreaStrike の tint/hot（ボス色）をそのまま受け取る＝新色を作らない（§9 色語彙の統一）。
    public void AoeImpact(Vector2 pos, Color tint, Color hot, Vector2 dirBias, float scale, int count = 9)
    {
        float s = Mathf.Clamp(scale, 8f, 90f);
        // 芯の一瞬の白熱（本動作の「止め」）。
        Add0(new P { Type = T.Glow, X = pos.X, Y = pos.Y, Size = 4f + s * 0.10f, Ttl = 0.12f, Col = White, Add = true, Grow = 0.5f });
        // 衝撃波リング2本（速い白＝一撃、遅いボス色＝誰の攻撃かの余韻）。
        Add0(new P { Type = T.Ring, X = pos.X, Y = pos.Y, R0 = s * 0.25f, R1 = s * 1.05f, Ttl = 0.18f, Col = White, W = 1.5f, A0 = 0.85f, Add = true });
        Add0(new P { Type = T.Ring, X = pos.X, Y = pos.Y, R0 = s * 0.15f, R1 = s * 1.45f, Ttl = 0.30f, Col = tint,  W = 1.1f, A0 = 0.55f, Add = true });
        bool biased = dirBias.LengthSquared() > 0.0001f;
        float baseA = biased ? Mathf.Atan2(dirBias.Y, dirBias.X) : 0f;
        for (int i = 0; i < count; i++)
        {
            // ビーム系は軸±0.5rad の扇（＝走った方向へ散る）、円/矩形は全方位。
            float a = biased ? baseA + R(-0.55f, 0.55f) + (_rng.Randf() < 0.5f ? 0f : Mathf.Pi)
                             : R(0f, Mathf.Tau);
            float sp = R(70f, 70f + s * 2.4f);
            Add0(new P { Type = T.Spark, X = pos.X, Y = pos.Y,
                Vx = Mathf.Cos(a) * sp, Vy = Mathf.Sin(a) * sp,
                Size = R(4f, 8f), W = 1.1f, Ttl = R(0.14f, 0.28f), Drag = 5.5f,
                Col = _rng.Randf() < 0.55f ? hot : White, Add = true });
        }
    }

    public void DamageNumber(Vector2 pos, string text, Color col)
    {
        DamageNumber(pos, text, col, 9);
    }

    // size 付き：密着クリティカルなど「一回り大きく」見せたい数字向け（既定9は通常被弾）。
    public void DamageNumber(Vector2 pos, string text, Color col, float size)
    {
        Add0(new P { Type = T.Dmg, X = pos.X, Y = pos.Y, Vy = -26, Drag = 1.5f, Size = size, Ttl = 0.7f, Text = text, Col = col });
    }

    // ボム：魔法陣 + 光の波（弾→花びら変換と画面効果は Player.TryBomb 側）
    public void Bomb(Vector2 pos)
    {
        Add0(new P { Type = T.Sigil, X = pos.X, Y = pos.Y, Ttl = 0.45f, Col = Sig });
        Add0(new P { Type = T.BombRing, X = pos.X, Y = pos.Y, R0 = 6, Sp = 520, Ttl = 1.1f, Col = Sig2 });
    }

    // ===== ボス別アンビエント・オーラ =====
    // 中心 c（＝ボス GlobalPosition）の周囲に、そのキャラの特徴物をゆっくり舞わせる。
    // 毎フレーム少量だけ確率スポーンして飽和させない。視覚専用＝当たり判定は一切触らない。
    // 立ち絵の表示高さは ~50px 想定。半径 R はその少し外（弾の視認を妨げない範囲）。

    // 各キャラ色（既存 PAL の流儀＝各ボスのスペル tint に寄せる）。
    private static readonly Color AuraReiHot  = new Color("ff5a5a"); // レイ：刺さる赤
    private static readonly Color AuraReiGold = new Color("ffd06a"); // 順位の金
    private static readonly Color AuraRain    = new Color("8fc4ff"); // あかり：雨青
    private static readonly Color AuraRainPale= new Color("cfe6ff"); // 水しぶき
    private static readonly Color AuraSteam   = new Color("ffe2b0"); // こはる：湯気の温かい白橙
    private static readonly Color AuraKoharu  = new Color("ffb15a"); // 台所の灯
    private static readonly Color AuraSilver  = new Color("dfe6f5"); // ミナ：銀の羽根
    private static readonly Color AuraGlitch  = new Color("a9c6ff"); // データの文字
    private static readonly Color AuraEmber   = new Color("ff7a3c"); // ヒカゲ：火の粉
    private static readonly Color AuraSmoke   = new Color("3a3038"); // くすぶる煙

    // ボス周囲のアンビエント発生（種別ディスパッチ）。radius = オーラ半径（立ち絵相当）。
    public void EmitBossAura(BossAura kind, Vector2 c, float dt, float radius = 30f)
    {
        switch (kind)
        {
            case BossAura.Rei:    AuraRei(c, dt, radius); break;
            case BossAura.Akari:  AuraAkari(c, dt, radius); break;
            case BossAura.Koharu: AuraKoharu_(c, dt, radius); break;
            case BossAura.Mina:   AuraMina(c, dt, radius); break;
            case BossAura.Hikage: AuraHikage(c, dt, radius); break;
        }
    }

    // dt に応じた確率スポーン（rate=1秒あたりの期待発生数）。
    private bool Spawn(float rate, float dt) => _rng.Randf() < rate * dt;

    // ── レイ：順位・競争。鋭い赤火花が周囲を周回＋順位記号(#1/▲/▼)がふわっと立ち上る。
    private void AuraRei(Vector2 c, float dt, float rr)
    {
        // 周回する赤い火花（接線方向＝回っているように見せる）。
        if (Spawn(10f, dt))
        {
            float a = R(0f, Mathf.Tau);
            var rad = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (rr + R(-2f, 6f));
            var tan = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * R(36f, 64f); // 接線で旋回感
            Add0(new P { Type = T.Spark, X = c.X + rad.X, Y = c.Y + rad.Y, Vx = tan.X, Vy = tan.Y,
                Size = R(3f, 5f), W = 1.2f, Ttl = R(0.3f, 0.5f), Col = AuraReiHot, Drag = 1.4f, Add = true });
        }
        // ランキングの記号（#1 / ▲ / ▼）が下からゆっくり昇る。
        if (Spawn(1.4f, dt))
        {
            string[] syms = { "#1", "▲", "▼", "#2" };
            string s = syms[Ri(0, syms.Length - 1)];
            bool top = s == "#1" || s == "▲";
            Add0(new P { Type = T.Sym, X = c.X + R(-rr, rr), Y = c.Y + R(2f, rr * 0.7f),
                Vy = R(-20f, -12f), Size = R(8f, 11f), Ttl = R(0.9f, 1.3f), Drag = 0.5f,
                Text = s, Col = top ? AuraReiGold : AuraReiHot, Add = true });
        }
    }

    // ── あかり：雨。青い雨粒が斜めに降り、足元で小さな波紋。
    private void AuraAkari(Vector2 c, float dt, float rr)
    {
        // 斜めに降る雨の線（本体の上方から下へ）。
        if (Spawn(22f, dt))
        {
            float x = c.X + R(-rr * 1.3f, rr * 1.3f);
            Add0(new P { Type = T.Rain, X = x, Y = c.Y - R(rr * 0.6f, rr * 1.4f),
                Vx = -26f, Vy = R(150f, 200f), Size = R(5f, 9f), W = 1.2f,
                Ttl = R(0.35f, 0.6f), Col = AuraRain, Add = true });
        }
        // 体の下あたりで小さな波紋（雨が当たる音色）。
        if (Spawn(3.0f, dt))
        {
            Add0(new P { Type = T.Ring, X = c.X + R(-rr, rr), Y = c.Y + R(rr * 0.3f, rr * 0.8f),
                R0 = 1f, R1 = R(5f, 8f), Ttl = R(0.4f, 0.6f), Col = AuraRainPale, W = 1f, A0 = 0.8f, Add = true });
        }
    }

    // ── こはる：台所。湯気がゆらゆら立ち上り、温かい灯の粒がことこと。
    private void AuraKoharu_(Vector2 c, float dt, float rr)
    {
        // 湯気（左右にゆらぎながら上昇）。
        if (Spawn(8f, dt))
        {
            Add0(new P { Type = T.Steam, X = c.X + R(-rr * 0.7f, rr * 0.7f), Y = c.Y + R(-2f, rr * 0.5f),
                Vy = R(-26f, -16f), Size = R(5f, 9f), Ttl = R(1.0f, 1.6f), Sp = R(1.6f, 3.0f),
                Rot = R(0f, Mathf.Tau), Col = AuraSteam, Add = true });
        }
        // ことこと泡＝温かい灯の粒。
        if (Spawn(3.5f, dt))
        {
            Add0(new P { Type = T.Mote, X = c.X + R(-rr, rr), Y = c.Y + R(-rr * 0.3f, rr * 0.5f),
                Vy = R(-22f, -12f), Size = R(2f, 3f), Drag = 0.7f, Ttl = R(0.5f, 0.8f), Col = AuraKoharu, Add = true });
        }
        // たまに湯気の輪。
        if (Spawn(0.8f, dt))
            Add0(new P { Type = T.Ring, X = c.X + R(-rr * 0.4f, rr * 0.4f), Y = c.Y - R(2f, 8f),
                R0 = 2f, R1 = R(7f, 11f), Ttl = R(0.7f, 1.0f), Col = AuraSteam, W = 1f, A0 = 0.5f, Add = true });
    }

    // ── ミナ：データ/グリッチ・銀の羽根・浄化色の粒。
    private void AuraMina(Vector2 c, float dt, float rr)
    {
        // 銀の羽根（ゆっくり舞い落ちる、回転しながら）。
        if (Spawn(5f, dt))
            Add0(new P { Type = T.Feather, X = c.X + R(-rr, rr), Y = c.Y - R(0f, rr),
                Vx = R(-10f, 6f), Vy = R(8f, 18f), Size = R(3.5f, 5.5f), Rot = R(0f, Mathf.Tau),
                Spin = R(-2.4f, 2.4f), Grav = 6f, Drag = 0.4f, Ttl = R(1.2f, 1.8f), Col = AuraSilver, Add = true });
        // データのグリッチ文字（0/1/記号が一瞬またたく）。
        if (Spawn(4f, dt))
        {
            string[] g = { "0", "1", "01", "</>", "{ }", "AI" };
            Add0(new P { Type = T.Sym, X = c.X + R(-rr, rr), Y = c.Y + R(-rr, rr),
                Size = R(7f, 9f), Ttl = R(0.25f, 0.45f), Text = g[Ri(0, g.Length - 1)], Col = AuraGlitch, Add = true });
        }
        // 浄化色の粒（既存 Mote/Sig 系の余韻）。
        if (Spawn(4f, dt))
            Add0(new P { Type = T.Mote, X = c.X + R(-rr, rr), Y = c.Y + R(-rr * 0.5f, rr),
                Vy = R(-18f, -8f), Size = R(1.8f, 2.8f), Drag = 0.9f, Ttl = R(0.6f, 0.9f), Col = Sig2, Add = true });
    }

    // ── ヒカゲ：炎上/影。火の粉が舞い上がり、くすぶる煙がにじむ。
    private void AuraHikage(Vector2 c, float dt, float rr)
    {
        // 火の粉（赤橙のスパークが下から上へ、ゆらぎながら）。
        if (Spawn(16f, dt))
        {
            Add0(new P { Type = T.Spark, X = c.X + R(-rr * 0.8f, rr * 0.8f), Y = c.Y + R(-2f, rr * 0.6f),
                Vx = R(-16f, 16f), Vy = R(-60f, -34f), Size = R(2.5f, 4.5f), W = 1.1f,
                Ttl = R(0.4f, 0.7f), Col = _rng.Randf() < 0.4f ? AuraReiGold : AuraEmber, Drag = 0.8f, Add = true });
        }
        // くすぶる煙（暗い、非加算でにじむ）。
        if (Spawn(5f, dt))
            Add0(new P { Type = T.Steam, X = c.X + R(-rr * 0.6f, rr * 0.6f), Y = c.Y + R(-4f, rr * 0.4f),
                Vy = R(-22f, -12f), Size = R(6f, 10f), Ttl = R(0.9f, 1.4f), Sp = R(1.2f, 2.4f),
                Rot = R(0f, Mathf.Tau), Col = AuraSmoke, Add = false });
        // たまに小さな炎のグロー。
        if (Spawn(2.5f, dt))
            Add0(new P { Type = T.Glow, X = c.X + R(-rr * 0.6f, rr * 0.6f), Y = c.Y + R(-2f, rr * 0.5f),
                Size = R(3f, 5f), Ttl = R(0.25f, 0.45f), Col = AuraEmber, Grow = 0.3f, Add = true });
    }

    private void Add0(P p) { _p.Add(p); }

    // ===== 描画 =====
    private static void GlowDot(Node2D c, float x, float y, float r, Color col, float a)
    {
        if (a <= 0f) return;
        var p = new Vector2(x, y);
        c.DrawCircle(p, r, new Color(col.R, col.G, col.B, a * 0.25f));
        c.DrawCircle(p, r * 0.6f, new Color(col.R, col.G, col.B, a * 0.5f));
        c.DrawCircle(p, r * 0.3f, new Color(col.R, col.G, col.B, a * 0.95f));
    }

    private static void DrawP(Node2D c, P p, Font font)
    {
        float k = p.Life / p.Ttl, inv = 1f - k;
        var pos = new Vector2(p.X, p.Y);
        switch (p.Type)
        {
            case T.Spark:
            {
                float a = inv, len = p.Size * (0.6f + inv), sp = Mathf.Max(1f, Mathf.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy));
                var n = new Vector2(p.Vx / sp, p.Vy / sp);
                c.DrawLine(pos, pos - n * len, new Color(p.Col.R, p.Col.G, p.Col.B, a), p.W);
                break;
            }
            case T.Mote:
            {
                float a = Mathf.Sin(Mathf.Min(1f, k * 1.1f) * Mathf.Pi) * 0.95f;
                GlowDot(c, p.X, p.Y, p.Size * (1f + k * 0.6f), p.Col, a);
                GlowDot(c, p.X, p.Y, p.Size * 0.45f, White, a * 0.9f);
                break;
            }
            case T.Glow:
                GlowDot(c, p.X, p.Y, p.Size * (1f + k * p.Grow), p.Col, inv * p.A0);
                break;
            case T.Shard:
            {
                float a = Mathf.Min(1f, inv * 1.4f), s = p.Size;
                Vector2[] pts = { new(-s, -s * 0.6f), new(s * 0.8f, -s), new(s, s * 0.7f), new(-s * 0.5f, s) };
                RotTranslate(pts, p.Rot, pos);
                c.DrawColoredPolygon(pts, new Color(p.Col.R, p.Col.G, p.Col.B, a));
                var ec = new Color(p.Edge.R, p.Edge.G, p.Edge.B, a * 0.7f);
                c.DrawPolyline(Close(pts), ec, 0.6f);
                break;
            }
            case T.Petal:
            {
                float a = Mathf.Min(1f, inv * 1.6f), s = p.Size;
                Vector2[] pts = { new(0, -s), new(s * 0.85f, 0), new(0, s), new(-s * 0.85f, 0) };
                RotTranslate(pts, p.Rot, pos);
                c.DrawColoredPolygon(pts, new Color(p.Col.R, p.Col.G, p.Col.B, a));
                c.DrawPolyline(Close(pts), new Color(1, 1, 1, a * 0.7f), 0.5f);
                break;
            }
            case T.HeartP:
            {
                float a = Mathf.Min(1f, inv * 1.6f), s = p.Size;
                GlowDot(c, p.X, p.Y, s * 1.6f, p.Col, a * 0.5f);
                var col = new Color(p.Col.R, p.Col.G, p.Col.B, a);
                // 簡易ハート：2つの円＋三角
                c.DrawCircle(pos + Rot(new Vector2(-s * 0.45f, -s * 0.25f), p.Rot), s * 0.5f, col);
                c.DrawCircle(pos + Rot(new Vector2(s * 0.45f, -s * 0.25f), p.Rot), s * 0.5f, col);
                Vector2[] tri = { new(-s * 0.85f, -s * 0.05f), new(s * 0.85f, -s * 0.05f), new(0, s) };
                RotTranslate(tri, p.Rot, pos);
                c.DrawColoredPolygon(tri, col);
                break;
            }
            case T.Ring:
            {
                float r = p.R0 + (p.R1 - p.R0) * k;
                c.DrawArc(pos, r, 0, Mathf.Tau, 40, new Color(p.Col.R, p.Col.G, p.Col.B, inv * p.A0), p.W);
                break;
            }
            case T.Dmg:
            {
                if (font == null) break;
                float a = k < 0.15f ? k / 0.15f : inv / 0.85f;
                a = Mathf.Min(1f, a);
                var off = pos - new Vector2(p.Text.Length * p.Size * 0.28f, 0);
                font.DrawString(c.GetCanvasItem(), off + new Vector2(0.6f, 0.6f), p.Text, HorizontalAlignment.Left, -1, (int)p.Size, new Color(0.08f, 0.03f, 0.08f, a * 0.85f));
                font.DrawString(c.GetCanvasItem(), off, p.Text, HorizontalAlignment.Left, -1, (int)p.Size, new Color(p.Col.R, p.Col.G, p.Col.B, a));
                break;
            }
            case T.Sigil:
                DrawSigil(c, pos, k, p.Life, p.Ttl);
                break;
            case T.BombRing:
            {
                float a = Mathf.Max(0f, 1f - k);
                c.DrawArc(pos, p.R0, 0, Mathf.Tau, 48, new Color(Sig2.R, Sig2.G, Sig2.B, a * 0.9f), 2.4f);
                c.DrawArc(pos, p.R0 - 2, 0, Mathf.Tau, 48, new Color(1, 1, 1, a * 0.5f), 1f);
                break;
            }
            case T.Rain:
            {
                // 速度方向に伸びる雨の線。フェードは寿命に沿って。
                float a = Mathf.Min(1f, inv * 1.6f) * 0.85f;
                float sp = Mathf.Max(1f, Mathf.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy));
                var n = new Vector2(p.Vx / sp, p.Vy / sp);
                c.DrawLine(pos, pos - n * p.Size, new Color(p.Col.R, p.Col.G, p.Col.B, a), p.W);
                break;
            }
            case T.Steam:
            {
                // ふわっと現れ消える柔らかい円。左右にゆらぎ（Sp=ゆらぎ強さ）。
                float a = Mathf.Sin(Mathf.Min(1f, k * 1.05f) * Mathf.Pi) * 0.5f;
                float wob = Mathf.Sin(p.Life * 4f + p.Rot) * p.Sp;
                float gr = p.Size * (1f + k * 1.2f);
                var sp = new Vector2(p.X + wob, p.Y);
                if (p.Add) GlowDot(c, sp.X, sp.Y, gr, p.Col, a);
                else c.DrawCircle(sp, gr, new Color(p.Col.R, p.Col.G, p.Col.B, a * 0.6f));
                break;
            }
            case T.Feather:
            {
                // 銀の羽根＝細い菱形＋中央の軸線。ゆっくり回転して舞う。
                float a = Mathf.Sin(Mathf.Min(1f, k * 1.1f) * Mathf.Pi) * 0.9f;
                float s = p.Size;
                Vector2[] pts = { new(0, -s), new(s * 0.42f, 0), new(0, s * 1.1f), new(-s * 0.42f, 0) };
                RotTranslate(pts, p.Rot, pos);
                c.DrawColoredPolygon(pts, new Color(p.Col.R, p.Col.G, p.Col.B, a));
                var tip = pos + Rot(new Vector2(0, -s), p.Rot);
                var bot = pos + Rot(new Vector2(0, s * 1.1f), p.Rot);
                c.DrawLine(tip, bot, new Color(1, 1, 1, a * 0.7f), 0.6f);
                break;
            }
            case T.AimLine:
            {
                // 照準線（ロックオン予告）。dir=(Vx,Vy)、length=Size。終了直前ほど明滅を速め＝発射が近い合図。
                var dir = new Vector2(p.Vx, p.Vy);
                float hz = k > 0.6f ? 14f : 6f;
                float pulse = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(p.Life * hz * Mathf.Pi));
                float a = p.A0 * pulse * Mathf.Min(1f, inv * 2.2f + 0.3f);
                var end = pos + dir * p.Size;
                c.DrawLine(pos, end, new Color(p.Col.R, p.Col.G, p.Col.B, a), p.W);
                c.DrawLine(pos, end, new Color(1, 1, 1, a * 0.4f), p.W * 0.4f); // 芯の白
                break;
            }
            case T.Sym:
            {
                // 記号/文字（順位記号・グリッチ文字）。Dmg と同じ流儀で font 描画＋影。
                if (font == null) break;
                float a = k < 0.18f ? k / 0.18f : inv / 0.82f;
                a = Mathf.Min(1f, a) * 0.92f;
                var off = pos - new Vector2(p.Text.Length * p.Size * 0.28f, 0);
                font.DrawString(c.GetCanvasItem(), off + new Vector2(0.6f, 0.6f), p.Text, HorizontalAlignment.Left, -1, (int)p.Size, new Color(0.05f, 0.03f, 0.06f, a * 0.7f));
                font.DrawString(c.GetCanvasItem(), off, p.Text, HorizontalAlignment.Left, -1, (int)p.Size, new Color(p.Col.R, p.Col.G, p.Col.B, a));
                break;
            }
        }
    }

    // ボム魔法陣（紫十字・回転）。game.js drawSigil を移植
    private static void DrawSigil(Node2D c, Vector2 pos, float k, float life, float ttl)
    {
        float scale = Mathf.Sin(Mathf.Min(1f, k * 1.2f) * Mathf.Pi) + 0.0001f;
        float Rr = 34f * scale, rot = k * 3.2f;
        float a = Mathf.Sin(Mathf.Min(1f, k * 1.1f) * Mathf.Pi);
        var purple = new Color(Sig.R, Sig.G, Sig.B, a);
        c.DrawArc(pos, Rr, 0, Mathf.Tau, 48, purple, 1.6f);
        c.DrawArc(pos, Rr * 0.74f, 0, Mathf.Tau, 40, purple, 1.0f);
        c.DrawArc(pos, Rr * 0.5f, 0, Mathf.Tau, 32, purple, 1.0f);
        for (int i = 0; i < 12; i++)
        {
            float ang = (i / 12f) * Mathf.Tau + rot;
            var p1 = pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Rr * 0.74f;
            var p2 = pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Rr;
            c.DrawLine(p1, p2, purple, 1f);
        }
        var cross = new Color(0.44f, 0.31f, 0.75f, a);
        var ux = Rot(new Vector2(Rr, 0), rot); var uy = Rot(new Vector2(0, Rr), rot);
        c.DrawLine(pos - ux, pos + ux, cross, 1.8f);
        c.DrawLine(pos - uy, pos + uy, cross, 1.8f);
        GlowDot(c, pos.X, pos.Y, Rr * 0.4f, Sig2, a * 0.6f);
    }

    private static Vector2 Rot(Vector2 v, float ang)
    {
        float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
        return new Vector2(v.X * cs - v.Y * sn, v.X * sn + v.Y * cs);
    }
    private static void RotTranslate(Vector2[] pts, float ang, Vector2 o)
    {
        for (int i = 0; i < pts.Length; i++) pts[i] = o + Rot(pts[i], ang);
    }
    private static Vector2[] Close(Vector2[] pts)
    {
        var r = new Vector2[pts.Length + 1];
        Array.Copy(pts, r, pts.Length); r[pts.Length] = pts[0];
        return r;
    }
}

// 加算ブレンドの粒だけを描く子ノード。
public partial class AddDraw : Node2D
{
    public FxLayer Owner2D = null!;
    public override void _Draw()
    {
        Owner2D?.DrawAddParticles(this);
    }
}
