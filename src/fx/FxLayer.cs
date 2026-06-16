using Godot;
using System;
using System.Collections.Generic;

// FxLayer : ALGO Effects Lab(standalone) の演出を Godot へ移植した視覚パーティクル基盤。
// 当たり判定なしの視覚専用。1インスタンスをMainがWorldに追加し、Instance経由で各所から呼ぶ。
// 加算ブレンドの粒は子ノード(_add)に分けて描画する。
public partial class FxLayer : Node2D
{
    public static FxLayer Instance;

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
    public static readonly Color Magenta = new Color("e072ac");
    public static readonly Color Gold    = new Color("ffd98a");

    public enum T { Spark, Mote, Glow, Shard, Petal, HeartP, Ring, Dmg, Sigil, BombRing }

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

    public void DamageNumber(Vector2 pos, string text, Color col)
    {
        Add0(new P { Type = T.Dmg, X = pos.X, Y = pos.Y, Vy = -26, Drag = 1.5f, Size = 9, Ttl = 0.7f, Text = text, Col = col });
    }

    // ボム：魔法陣 + 光の波（弾→花びら変換と画面効果は Player.TryBomb 側）
    public void Bomb(Vector2 pos)
    {
        Add0(new P { Type = T.Sigil, X = pos.X, Y = pos.Y, Ttl = 0.45f, Col = Sig });
        Add0(new P { Type = T.BombRing, X = pos.X, Y = pos.Y, R0 = 6, Sp = 520, Ttl = 1.1f, Col = Sig2 });
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
