using Godot;

// AreaStrike : 範囲攻撃テレグラフ（RefrainArenaHTML 移植）。ボスの範囲技と道中ドローンのビームで共用。
//   予兆（点滅輪郭＋範囲を囲う）→ 充填（範囲が満ちる）→ 着弾（白熱フラッシュ・このフレームのみ判定）の3段。
//   形状＝予測線（横/縦/任意向きビーム）・予測エリア（円/矩形）。色はキャラの心象色（tint）＋着弾の明色（hot）。
//   弾の下・背景の上（ZIndex -10）の“床マーカー”として描き、弾の視認を妨げない。
//   予兆中は当たり判定なし、着弾の瞬間にだけ範囲内の自機を被弾させる（必ず予告＝§7 理不尽回避）。
//
// 使い方（軸ビーム/円/矩形）：
//   var z = new AreaStrike();
//   z.Configure(AreaStrike.Shape.BeamH, halfW, halfH, warn, tint, hot);
//   world.AddChild(z); z.GlobalPosition = center;
//
// 使い方（任意向きビーム＝ドローンのロックオンビーム等）：自機方向へ伸びる線分。
//   var z = new AreaStrike();
//   z.ConfigureBeam(dir, length, halfThick, warn, tint, hot);
//   world.AddChild(z); z.GlobalPosition = origin;
public partial class AreaStrike : Node2D
{
    public enum Shape { BeamH, BeamV, Circle, Rect, BeamSeg }

    private const float PlayerHit = 2.5f;     // 自機の被弾半径ぶんの寄せ
    private const double StrikeFlash = 0.20;  // 着弾フラッシュの尺

    private Shape _shape;
    private float _hw, _hh;                    // 矩形/ビームの半幅・半高（円は _hw を半径に使う）
    private float Radius => _hw;
    private double _warn = 1.2;
    private Color _tint = new Color(1f, 0.34f, 0.30f);
    private Color _hot = new Color(1f, 0.92f, 0.7f);

    // BeamSeg（任意向きビーム）専用：原点(=GlobalPosition)から _segDir 方向へ _segLen 伸びる線分。
    // 半太さ _hh で当たり判定／描画する（線分なので原点から片方向に伸びる＝矩形ビームの軸非依存版）。
    private Vector2 _segDir = new Vector2(-1, 0);
    private float _segLen;

    private double _t;
    private bool _struck;

    // 発生源（任意）。設定すると、着弾前に発生源が消滅/浄化された時点で予測線ごとキャンセルする。
    //   ＝「予兆中に倒せば攻撃も消える」。道中ザコのロックオンビームで使う（ボスの範囲技は未設定＝従来どおり完遂）。
    private Node2D? _owner;
    private bool _cancelOnOwnerLoss;
    public void SetOwner(Node2D owner) { _owner = owner; _cancelOnOwnerLoss = true; }

    public void Configure(Shape shape, float halfW, float halfH, double warn, Color tint, Color hot)
    {
        _shape = shape; _hw = halfW; _hh = halfH;
        _warn = Mathf.Max(0.35, warn);
        _tint = tint; _hot = hot;
        ZIndex = -10; ZAsRelative = false;
    }

    // 任意向きビーム（線分）。dir 方向へ length 伸び、半太さ halfThick で判定する。
    // 予兆中は予測線（細い危険色ライン）を出すだけで当たらず、着弾フレームだけ線分上の自機を被弾させる。
    // 位置は他形状と同じく AddChild 後に GlobalPosition=発射源 を設定して使う。
    public void ConfigureBeam(Vector2 dir, float length, float halfThick,
        double warn, Color tint, Color hot)
    {
        _shape = Shape.BeamSeg;
        _segDir = dir.LengthSquared() > 0.0001f ? dir.Normalized() : new Vector2(-1, 0);
        _segLen = Mathf.Max(8f, length);
        _hh = Mathf.Max(1f, halfThick);
        _hw = _segLen; // 描画/便宜用（未使用経路の保険）
        _warn = Mathf.Max(0.35, warn);
        _tint = tint; _hot = hot;
        ZIndex = -10; ZAsRelative = false;
    }

    public override void _Process(double delta)
    {
        // 発生源が着弾前に消えた／浄化されたら、予測線ごとキャンセル（倒せば攻撃も消える）。
        if (!_struck && _cancelOnOwnerLoss
            && (_owner == null || !IsInstanceValid(_owner) || (_owner is Enemy e && e.IsPurified)))
        {
            QueueFree();
            return;
        }

        // 会話中（吹き出し表示中）は弾と同じく時間を止める＝動けない自機に着弾させない。
        if (Hud.BubblePaused) return;

        _t += delta;
        if (!_struck && _t >= _warn) { _struck = true; Strike(); }
        if (_t >= _warn + StrikeFlash) { QueueFree(); return; }
        QueueRedraw();
    }

    private bool Inside(Vector2 p)
    {
        Vector2 d = p - GlobalPosition;
        if (_shape == Shape.Circle) return d.Length() <= Radius + PlayerHit;
        // 任意向きビーム：原点→_segLen の線分への最短距離が（半太さ＋自機半径）以内なら被弾。
        if (_shape == Shape.BeamSeg)
        {
            float along = Mathf.Clamp(d.Dot(_segDir), 0f, _segLen); // 線分上に投影（端でクランプ）
            Vector2 nearest = _segDir * along;
            return (d - nearest).Length() <= _hh + PlayerHit;
        }
        return Mathf.Abs(d.X) <= _hw + PlayerHit && Mathf.Abs(d.Y) <= _hh + PlayerHit;
    }

    // 着弾：範囲内に自機がいれば被弾（無敵判定は Player 側）。閃光＋軽いシェイク。
    private void Strike()
    {
        if (GetTree().GetFirstNodeInGroup("player") is Player p && Inside(p.GlobalPosition))
            p.TakeHit();
        GameCamera.Instance?.Shake(3.4f, 0.16f);
    }

    public override void _Draw()
    {
        if (!_struck) DrawTelegraph();
        else DrawStrike();
    }

    // 予兆＋充填（HTML準拠）：破線のマーチング輪郭＋面のベタ塗り（着弾へ濃く）＋警告マーカーで“範囲”を明示。
    private void DrawTelegraph()
    {
        float k = Mathf.Clamp((float)(_t / _warn), 0f, 1f);    // 0→1（着弾へのカウントダウン）
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_t * 9f);
        float phase = (float)_t * 46f;                          // 破線のマーチング
        Color fill = new Color(_tint.R, _tint.G, _tint.B, 0.12f + 0.26f * k); // 面のベタ塗り＝範囲を面で示す
        Color edge = new Color(_tint.R, _tint.G, _tint.B, 0.6f + 0.4f * pulse);
        Color core = new Color(_hot.R, _hot.G, _hot.B, 0.08f + 0.14f * k);

        if (_shape == Shape.Circle)
        {
            DrawCircle(Vector2.Zero, Radius, fill);
            DrawCircle(Vector2.Zero, Radius * Mathf.Lerp(0.12f, 1f, k), core); // 中心から満ちる白熱核
            DashedRing(Radius, 28, edge, 2f, phase * 0.012f);
            DrawWarn(Vector2.Zero, k);
            return;
        }

        if (_shape == Shape.BeamSeg)
        {
            Vector2 tip = _segDir * _segLen;
            DrawLine(Vector2.Zero, tip, fill, _hh * 2f);                        // 帯（面）
            DashedLine(Vector2.Zero, tip, edge, 1.6f + 1.2f * k, 7f, 5f, phase); // 破線の中心ガイド
            DrawCircle(Vector2.Zero, 3.2f, new Color(_tint.R, _tint.G, _tint.B, 0.85f * pulse)); // 発射源
            DrawWarn(_segDir * (_segLen * 0.5f), k);
            return;
        }

        var r = new Rect2(-_hw, -_hh, _hw * 2f, _hh * 2f);
        if (_shape == Shape.Rect)
        {
            // 矩形は角丸（他UIと同じ Clean Glass 調＝角ばらせない）。
            float rad = Mathf.Min(7f, Mathf.Min(_hw, _hh) * 0.8f);
            RoundFill(r, rad, fill);
            RoundOutline(r, rad, edge, 2f);
            DrawWarn(Vector2.Zero, k);
            return;
        }
        // ビーム：細い帯＋両端の予測線（破線）。線に沿って警告バッジ。
        DrawRect(r, fill);
        DashedBoxBorder(edge, 2f, phase);
        if (_shape == Shape.BeamH)
        { DrawWarn(new Vector2(-_hw * 0.5f, 0), k); DrawWarn(new Vector2(_hw * 0.5f, 0), k); }
        else
        { DrawWarn(new Vector2(0, -_hh * 0.5f), k); DrawWarn(new Vector2(0, _hh * 0.5f), k); }
    }

    // 着弾：白熱フラッシュ（短く強く）＋輪郭バースト。
    private void DrawStrike()
    {
        float st = Mathf.Clamp((float)((_t - _warn) / StrikeFlash), 0f, 1f);
        float f = 1f - st;
        Color core = new Color(_hot.R, _hot.G, _hot.B, 0.6f * f);
        Color rim = new Color(1f, 1f, 1f, f);
        if (_shape == Shape.Circle)
        {
            DrawCircle(Vector2.Zero, Radius, core);
            DrawArc(Vector2.Zero, Radius, 0f, Mathf.Tau, 56, rim, 2.6f);
            DrawArc(Vector2.Zero, Radius * (1f + 0.55f * st), 0f, Mathf.Tau, 48,
                new Color(_hot.R, _hot.G, _hot.B, 0.5f * f), 2f); // 広がるバースト
            return;
        }
        if (_shape == Shape.BeamSeg)
        {
            Vector2 tip = _segDir * _segLen;
            DrawLine(Vector2.Zero, tip, core, _hh * 2f);
            DrawLine(Vector2.Zero, tip, rim, 2.6f);
            return;
        }
        var rr = new Rect2(-_hw, -_hh, _hw * 2f, _hh * 2f);
        if (_shape == Shape.Rect)
        {
            float rad = Mathf.Min(7f, Mathf.Min(_hw, _hh) * 0.8f);
            RoundFill(rr, rad, core);
            RoundOutline(rr, rad, rim, 2.6f);
            return;
        }
        DrawRect(rr, core);
        DrawBoxBorder(rim, 2.6f);
    }

    // ビーム＝範囲の両端を線で囲う（予測線）／矩形＝四辺。
    private void DrawBoxBorder(Color col, float w)
    {
        float l = -_hw, rgt = _hw, t = -_hh, b = _hh;
        if (_shape == Shape.BeamH)
        {
            DrawLine(new Vector2(l, t), new Vector2(rgt, t), col, w);
            DrawLine(new Vector2(l, b), new Vector2(rgt, b), col, w);
        }
        else if (_shape == Shape.BeamV)
        {
            DrawLine(new Vector2(l, t), new Vector2(l, b), col, w);
            DrawLine(new Vector2(rgt, t), new Vector2(rgt, b), col, w);
        }
        else // Rect
        {
            DrawLine(new Vector2(l, t), new Vector2(rgt, t), col, w);
            DrawLine(new Vector2(rgt, t), new Vector2(rgt, b), col, w);
            DrawLine(new Vector2(rgt, b), new Vector2(l, b), col, w);
            DrawLine(new Vector2(l, b), new Vector2(l, t), col, w);
        }
    }

    // 警告マーカー：危険色の丸バッジ＋白い「!」（角丸UIに合わせて丸く・小さく）。
    private void DrawWarn(Vector2 p, float k)
    {
        float s = 3.4f + 1.2f * k;
        DrawCircle(p, s, new Color(_tint.R, _tint.G, _tint.B, 0.5f));
        DrawCircle(p, s * 0.66f, new Color(_tint.R, _tint.G, _tint.B, 0.75f));
        DrawLine(p + new Vector2(0, -s * 0.45f), p + new Vector2(0, s * 0.12f), new Color(1f, 1f, 1f, 0.95f), 1.2f);
        DrawCircle(p + new Vector2(0, s * 0.45f), 0.8f, new Color(1f, 1f, 1f, 0.95f));
    }

    // ───── 角丸矩形（他UIと同じ Clean Glass 調。塗りと輪郭で共用）─────
    private static Vector2[] RoundRectPoints(Rect2 r, float rad)
    {
        rad = Mathf.Max(0.5f, Mathf.Min(rad, Mathf.Min(r.Size.X, r.Size.Y) * 0.5f));
        var pts = new System.Collections.Generic.List<Vector2>();
        Vector2[] cen =
        {
            new(r.Position.X + rad, r.Position.Y + rad),       // 左上
            new(r.End.X - rad, r.Position.Y + rad),            // 右上
            new(r.End.X - rad, r.End.Y - rad),                 // 右下
            new(r.Position.X + rad, r.End.Y - rad),            // 左下
        };
        float[] start = { Mathf.Pi, -Mathf.Pi / 2f, 0f, Mathf.Pi / 2f };
        const int seg = 4;
        for (int c = 0; c < 4; c++)
            for (int i = 0; i <= seg; i++)
            {
                float a = start[c] + (Mathf.Pi / 2f) * (i / (float)seg);
                pts.Add(cen[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad);
            }
        return pts.ToArray();
    }
    private void RoundFill(Rect2 r, float rad, Color col) => DrawColoredPolygon(RoundRectPoints(r, rad), col);
    private void RoundOutline(Rect2 r, float rad, Color col, float w)
    {
        var p = RoundRectPoints(r, rad);
        var closed = new Vector2[p.Length + 1];
        p.CopyTo(closed, 0); closed[p.Length] = p[0];
        DrawPolyline(closed, col, w);
    }

    // ───── 破線描画（マーチングする予兆輪郭。HTMLの dashed border 相当）─────
    private void DashedLine(Vector2 a, Vector2 b, Color col, float w, float dash, float gap, float phase)
    {
        Vector2 d = b - a; float len = d.Length();
        if (len < 0.001f) return;
        Vector2 dir = d / len; float step = dash + gap; float o = phase % step;
        for (float s = -o; s < len; s += step)
        {
            float s0 = Mathf.Max(0f, s), s1 = Mathf.Min(len, s + dash);
            if (s1 > s0) DrawLine(a + dir * s0, a + dir * s1, col, w);
        }
    }

    private void DashedRing(float r, int dashes, Color col, float w, float phase)
    {
        float seg = Mathf.Tau / dashes, fill = seg * 0.58f;
        for (int i = 0; i < dashes; i++)
        {
            float a0 = i * seg + phase;
            DrawArc(Vector2.Zero, r, a0, a0 + fill, 5, col, w);
        }
    }

    private void DashedBoxBorder(Color col, float w, float phase)
    {
        float l = -_hw, rgt = _hw, t = -_hh, b = _hh;
        const float dash = 8f, gap = 6f;
        if (_shape == Shape.BeamH)
        {
            DashedLine(new Vector2(l, t), new Vector2(rgt, t), col, w, dash, gap, phase);
            DashedLine(new Vector2(l, b), new Vector2(rgt, b), col, w, dash, gap, phase);
        }
        else if (_shape == Shape.BeamV)
        {
            DashedLine(new Vector2(l, t), new Vector2(l, b), col, w, dash, gap, phase);
            DashedLine(new Vector2(rgt, t), new Vector2(rgt, b), col, w, dash, gap, phase);
        }
        else
        {
            DashedLine(new Vector2(l, t), new Vector2(rgt, t), col, w, dash, gap, phase);
            DashedLine(new Vector2(rgt, t), new Vector2(rgt, b), col, w, dash, gap, phase);
            DashedLine(new Vector2(rgt, b), new Vector2(l, b), col, w, dash, gap, phase);
            DashedLine(new Vector2(l, b), new Vector2(l, t), col, w, dash, gap, phase);
        }
    }
}
