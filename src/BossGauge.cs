using Godot;

// ボス／中ボスの体力ゲージ（2026-09-27 作者指示「画面上部じゃなくて、簡略化して、実際に動いてるボスの上部に表示」）。
//   画面上端のカード（Hud.DrawBossCard＝アイコン・名前・ハンドル・リプ数・穢れバー・pip・残/総）をやめ、
//   ボス本体の頭上に「いまの1本」のバーと「残り本数」の結晶を置く。
//   ボスの子ノードなので、位置・表示／非表示（投稿で隠れる等）・Modulate のフェードはボスに追従し、
//   ボスが消えれば一緒に消える。状態（1本ぶんの割合・残本数・スペル色・割れフラッシュ・改心の見送り）は
//   従来どおり Hud が持つ＝各ボスの UpdateBossBar／SetBossBarTint／FlashBossBarBreak／HideBossBar は変えていない。
//   Z は絶対 11（自機 10・弾 0 より手前）＝弾幕の中でも読める。
// 塗りの色分け（2026-09-27 作者指示「無敵のタイミングと、BREAK時のHPバー色分けしたい」）：
//   無敵（SHIELDED／RECLOSE）＝灰＋45°の斜線ハッチ（「今は通らない」）。
//   BREAK（BREAK／EXPOSED）＝金。割れた一拍は白へ寄せて光り、あとは脈打つ。
//   改心の見送り中は従来どおり浄化色へ抜ける（上の2色より優先）。フェーズ色（スペル色）は残本数の点に残す。
//   殴れるかどうかは Hud.GaugeState ではなくボス本体（Enemy.GaugeVulnerable／GaugeBreakFresh）から直接読む。
// 色味：ゲージは盤面側に居るので、各 Root の CanvasModulate（夜の冷色 Tint）で金がくすんで黄土色に沈む。
//   BubbleLayer と同じく Tint の逆数を SelfModulate に置いて打ち消し、塗り・台座・点・斜線をすべて本来の色で見せる。
public partial class BossGauge : Node2D
{
    private Hud? _hud;
    private Enemy? _owner;

    private const float BarH = 3.8f;
    private const float DamageHold = 0.12f;
    private readonly Vector2[] _plate = new Vector2[6], _outline = new Vector2[7];
    private readonly Vector2[] _diamond = new Vector2[4], _diamondOutline = new Vector2[5];
    private readonly Vector2[] _shield = new Vector2[5], _shieldOutline = new Vector2[6];
    private readonly Vector2[] _shieldCrack = new Vector2[4];
    private float _trail, _lastFrac, _damageHold;
    private int _barIndex = -1, _barTotal;

    private static readonly Color Plate = new("0b1015"), Rail = new("56676d"), Track = new("1c272e");
    private static readonly Color InvulnFill = new("899ba5"), DamageFill = new("b88186");
    private static readonly Color HatchCol = new(0.10f, 0.10f, 0.12f, 0.6f);
    private const float HatchW = 0.65f, HatchPitch = 3.8f;
    private static readonly Color BreakFill = new("f0c85a");
    private static readonly Color ReformFill = new("76cfe0"), WindowWarning = new("ff9870");

    private double _t;   // BREAK 中の脈の時計
    private CanvasModulate? _tint;   // 祖先のどこかに居る世界の色味（無ければ打ち消し不要）

    // 検証用（tools/BossGaugeQa.cs）：直近の _Draw で選んだ塗り＝"invuln" / "break" / "purify"。
    public string LastFillKind { get; private set; } = "";

    public static BossGauge Attach(Hud hud, Enemy owner)
    {
        var g = new BossGauge { Name = "BossGauge", ZAsRelative = false, ZIndex = 11, _hud = hud, _owner = owner };
        owner.AddChild(g);
        return g;
    }

    public override void _Ready()
    {
        // 祖先を上へたどり、その子に居る CanvasModulate（Root の Tint／Main の WorldTint）を拾う。
        for (Node? n = GetParent(); n != null && _tint == null; n = n.GetParent())
            foreach (var c in n.GetChildren())
                if (c is CanvasModulate cm) { _tint = cm; break; }
    }

    public override void _Process(double delta)
    {
        _t += delta;
        // 世界の色味（Tint）を打ち消す（BubbleLayer と同じ。乗算は float のまま出力まで届くので逆数で元の色に戻る）。
        if (_tint != null && IsInstanceValid(_tint) && _tint.Visible)
        {
            var c = _tint.Color;
            SelfModulate = new Color(1f / Mathf.Max(c.R, 0.05f), 1f / Mathf.Max(c.G, 0.05f), 1f / Mathf.Max(c.B, 0.05f), 1f);
        }
        else SelfModulate = Colors.White;
        if (_hud != null)
        {
            var s = _hud.GaugeState;
            // A refill belongs to a new bar; never carry damage from the previous life into it.
            if (!s.Visible || s.Index != _barIndex || s.Total != _barTotal || s.Frac > _lastFrac)
            {
                _trail = s.Frac;
                _damageHold = 0;
            }
            else
            {
                if (s.Frac < _lastFrac)
                {
                    if (_trail <= _lastFrac) _damageHold = DamageHold;
                    _trail = Mathf.Max(_trail, _lastFrac);
                }
                if (_damageHold > 0) _damageHold -= (float)delta;
                else _trail = Mathf.MoveToward(_trail, s.Frac, (float)delta * 1.8f);
            }
            _lastFrac = s.Frac;
            _barIndex = s.Index;
            _barTotal = s.Total;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_hud == null || _owner == null || !IsInstanceValid(_owner)) return;
        var s = _hud.GaugeState;
        if (!s.Visible || s.Fade <= 0f) return;

        float top = _owner.GaugeTop - 6f, w = _owner.GaugeWidth, a = s.Fade;
        // 改心の見送り：穢れ色（スペル色）→浄化色へ抜けてから薄れる（旧カードのアイコンと同じ段取り）。
        Color tint = s.Tint.Lerp(UiKit.PurifyHi, s.Purify);

        // 塗りの種類：見送り ＞ BREAK ＞ 無敵。
        string kind = s.Purify > 0f ? "purify" : _owner.GaugeVulnerable ? "break" : "invuln";
        LastFillKind = kind;
        Color fillCol = kind switch
        {
            "purify" => new Color(tint, a),
            "invuln" => new Color(InvulnFill, 0.8f * a),
            // 割れた一拍（GaugeBreakFresh 1→0）は白へ寄せて光り、あとは 1.5Hz の脈で α 0.85〜1.0。
            _ => new Color(BreakFill.Lerp(Colors.White, 0.8f * _owner.GaugeBreakFresh),
                (0.85f + 0.15f * (0.5f + 0.5f * Mathf.Sin((float)_t * Mathf.Tau * 1.5f))) * a),
        };

        var bar = new Rect2(-w / 2f, top, w, BarH);
        Color edge = kind == "invuln" ? Rail : fillCol;
        bool major = _owner is not CameoBoss;
        DrawPlate(bar.Grow(1.8f), new Color(Plate, 0.96f * a), new Color(edge, 0.72f * a));
        DrawPlate(bar, new Color(Track, a));
        if (_trail > s.Frac && s.Purify <= 0)
            DrawPlate(new Rect2(bar.Position, new Vector2(w * _trail, BarH)), new Color(DamageFill, 0.65f * a));
        if (s.Frac > 0f)
        {
            var fill = new Rect2(bar.Position, new Vector2(w * s.Frac, BarH));
            DrawPlate(fill, fillCol);
            if (kind == "invuln" && fill.Size.X > 4f)
                DrawHatch(new Rect2(fill.Position + new Vector2(1.9f, 0.4f), fill.Size - new Vector2(3.8f, 0.8f)), a);
            if (fill.Size.X > 3f)
                DrawLine(fill.Position + new Vector2(1.5f, 0.65f), new Vector2(fill.End.X - 1.5f, top + 0.65f),
                    new Color(Colors.White, 0.46f * a), 0.45f, true);
            if (s.Frac < 0.995f && fill.Size.X > 2f)
                DrawLine(new Vector2(fill.End.X - 0.5f, top + 1.1f), new Vector2(fill.End.X - 0.5f, top + BarH - 1.1f),
                    new Color(Colors.White, 0.86f * a), 0.65f, true);
        }
        for (int i = 1; i < 4; i++)
        {
            float x = bar.Position.X + w * i / 4;
            DrawLine(new Vector2(x, top + BarH - 0.6f), new Vector2(x, top + BarH + 0.9f),
                new Color(Plate, 0.8f * a), 0.6f, true);
        }
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * (w / 2 + 2.5f), mid = top + BarH / 2;
            DrawLine(new Vector2(x, mid - 2), new Vector2(x + side * 1.5f, mid), new Color(edge, a), 0.8f, true);
            DrawLine(new Vector2(x + side * 1.5f, mid), new Vector2(x, mid + 2), new Color(edge, a), 0.8f, true);
            if (major)
                DrawLine(new Vector2(side * (w / 2 - 7), top - 3), new Vector2(side * (w / 2 - 1), top - 3),
                    new Color(tint, 0.65f * a), 0.65f, true);
        }
        if (s.Flash > 0f)
        {
            DrawPlate(bar, new Color(Colors.White, 0.7f * s.Flash * a));
            float travel = (1 - s.Flash) * 5;
            for (int side = -1; side <= 1; side += 2)
                DrawLine(new Vector2(side * (w / 2 + travel), top - 2), new Vector2(side * (w / 2 + travel + 2), top - 3),
                    new Color(tint.Lightened(0.4f), s.Flash * a), 0.7f, true);
        }

        if (s.Total > 1)
        {
            int left = s.Index + 1;
            float pitch = Mathf.Min(4.3f, (w - 4) / s.Total);
            float radius = Mathf.Min(1.25f, pitch * 0.31f);
            float x0 = -(s.Total - 1) * pitch / 2f;
            float py = top + BarH + 3.4f;
            for (int i = 0; i < s.Total; i++)
            {
                var center = new Vector2(x0 + i * pitch, py);
                bool current = i == s.Index;
                DrawCrystal(center, radius + 0.6f, new Color(Plate, 0.9f * a), new Color(Plate, 0));
                DrawCrystal(center, radius,
                    i < left ? new Color(current ? fillCol : tint, a) : new Color(Track, a),
                    new Color(current ? Colors.White : i < left ? tint : Rail, (current ? 0.8f : 0.6f) * a));
            }
        }
        if (s.Purify <= 0f && (_owner.GaugeVulnerable || _owner.GaugeReforming))
            DrawRecoveryGauge(_owner, a);
    }

    private void DrawRecoveryGauge(Enemy owner, float alpha)
    {
        bool reforming = owner.GaugeReforming;
        float fraction = reforming ? owner.GaugeReformProgress : owner.GaugeWindowLeft;
        bool warning = !reforming && fraction < 0.25f;
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_t * Mathf.Tau * (warning ? 4f : 1.5f));
        Color accent = reforming ? ReformFill : warning ? WindowWarning : BreakFill;
        float w = owner.GaugeWidth, y = owner.GaugeBottom;
        var track = new Rect2(-w / 2 + 8f, y, w - 8f, 2.8f);
        DrawPlate(track.Grow(1.3f), new Color(Plate, 0.96f * alpha), new Color(accent, 0.55f * alpha));

        const int cells = 5;
        const float gap = 1.1f;
        float cellWidth = (track.Size.X - gap * (cells - 1)) / cells;
        for (int i = 0; i < cells; i++)
        {
            var cell = new Rect2(track.Position + new Vector2(i * (cellWidth + gap), 0), new Vector2(cellWidth, track.Size.Y));
            DrawPlate(cell, new Color(Track, alpha));
            float charge = Mathf.Clamp(fraction * cells - i, 0f, 1f);
            if (charge <= 0f) continue;
            var fill = new Rect2(cell.Position, new Vector2(cellWidth * charge, cell.Size.Y));
            DrawPlate(fill, new Color(accent, (warning ? 0.65f + 0.35f * pulse : 0.92f) * alpha));
            if (fill.Size.X > 1.4f)
                DrawLine(fill.Position + new Vector2(0.7f, 0.65f), new Vector2(fill.End.X - 0.7f, y + 0.65f),
                    new Color(Colors.White, 0.5f * alpha), 0.4f, true);
        }

        var center = new Vector2(-w / 2 + 2.5f, y + 1.3f);
        _shield[0] = center + new Vector2(-2.7f, -2.9f);
        _shield[1] = center + new Vector2(2.7f, -2.9f);
        _shield[2] = center + new Vector2(2.3f, 0.8f);
        _shield[3] = center + new Vector2(0, 3f);
        _shield[4] = center + new Vector2(-2.3f, 0.8f);
        DrawColoredPolygon(_shield, new Color(Plate, 0.96f * alpha));
        for (int i = 0; i < 5; i++) _shieldOutline[i] = _shield[i];
        _shieldOutline[5] = _shield[0];
        DrawPolyline(_shieldOutline, new Color(accent, (0.7f + 0.3f * pulse) * alpha), 0.7f, true);
        if (reforming)
        {
            float scan = Mathf.Lerp(center.Y + 1.4f, center.Y - 1.8f, fraction);
            DrawLine(new Vector2(center.X - 1.5f, scan), new Vector2(center.X + 1.5f, scan),
                new Color(accent.Lightened(0.3f), alpha), 0.65f, true);
        }
        else
        {
            _shieldCrack[0] = center + new Vector2(0.7f, -1.8f);
            _shieldCrack[1] = center + new Vector2(-0.6f, -0.1f);
            _shieldCrack[2] = center + new Vector2(0.6f, 0.2f);
            _shieldCrack[3] = center + new Vector2(-0.7f, 1.7f);
            DrawPolyline(_shieldCrack, new Color(accent, alpha), 0.65f, true);
        }
    }

    private void DrawPlate(Rect2 rect, Color fill, Color? edge = null)
    {
        float cut = Mathf.Min(rect.Size.Y / 2, rect.Size.X / 2);
        float x = rect.Position.X, y = rect.Position.Y, right = rect.End.X, bottom = rect.End.Y;
        _plate[0] = new Vector2(x + cut, y); _plate[1] = new Vector2(right - cut, y);
        _plate[2] = new Vector2(right, y + rect.Size.Y / 2); _plate[3] = new Vector2(right - cut, bottom);
        _plate[4] = new Vector2(x + cut, bottom); _plate[5] = new Vector2(x, y + rect.Size.Y / 2);
        DrawColoredPolygon(_plate, fill);
        if (edge is not Color line) return;
        for (int i = 0; i < 6; i++) _outline[i] = _plate[i];
        _outline[6] = _plate[0];
        DrawPolyline(_outline, line, 0.55f, true);
    }

    private void DrawCrystal(Vector2 center, float radius, Color fill, Color edge)
    {
        _diamond[0] = center + new Vector2(0, -radius); _diamond[1] = center + new Vector2(radius, 0);
        _diamond[2] = center + new Vector2(0, radius); _diamond[3] = center + new Vector2(-radius, 0);
        DrawColoredPolygon(_diamond, fill);
        if (edge.A <= 0) return;
        for (int i = 0; i < 4; i++) _diamondOutline[i] = _diamond[i];
        _diamondOutline[4] = _diamond[0];
        DrawPolyline(_diamondOutline, edge, 0.45f, true);
    }

    // 45° の斜線（右上がり）を矩形 r の中だけに引く。x+y=k の直線を k を HatchPitch 刻みで動かし、
    //   矩形との交わり（線分）を計算して描く＝クリップ用のノードやシェーダを使わない。
    private void DrawHatch(Rect2 r, float a)
    {
        float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        var col = new Color(HatchCol, HatchCol.A * a);
        for (float k = x0 + y0 + HatchPitch * 0.5f; k < x1 + y1; k += HatchPitch)
        {
            float xa = Mathf.Max(x0, k - y1), xb = Mathf.Min(x1, k - y0);
            if (xb - xa < 0.05f) continue;
            DrawLine(new Vector2(xa, k - xa), new Vector2(xb, k - xb), col, HatchW, true);
        }
    }
}
