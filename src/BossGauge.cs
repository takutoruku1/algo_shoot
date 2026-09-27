using Godot;

// ボス／中ボスの体力ゲージ（2026-09-27 作者指示「画面上部じゃなくて、簡略化して、実際に動いてるボスの上部に表示」）。
//   画面上端のカード（Hud.DrawBossCard＝アイコン・名前・ハンドル・リプ数・穢れバー・pip・残/総）をやめ、
//   ボス本体の頭上に「いまの1本」のバーと「残り本数」の点だけを置く。
//   ボスの子ノードなので、位置・表示／非表示（投稿で隠れる等）・Modulate のフェードはボスに追従し、
//   ボスが消えれば一緒に消える。状態（1本ぶんの割合・残本数・スペル色・割れフラッシュ・改心の見送り）は
//   従来どおり Hud が持つ＝各ボスの UpdateBossBar／SetBossBarTint／FlashBossBarBreak／HideBossBar は変えていない。
//   Z は絶対 11（自機 10・弾 0 より手前）＝弾幕の中でも読める。
// 塗りの色分け（2026-09-27 作者指示「無敵のタイミングと、BREAK時のHPバー色分けしたい」）：
//   無敵（SHIELDED／RECLOSE）＝灰＋45°の斜線ハッチ（「今は通らない」）。
//   BREAK（BREAK／EXPOSED）＝金（本体まわりの無防備窓タイマーバーの e9d28b より少し暖かい f0c85a）。割れた一拍は白へ寄せて光り、あとは脈打つ。
//   改心の見送り中は従来どおり浄化色へ抜ける（上の2色より優先）。フェーズ色（スペル色）は残本数の点に残す。
//   殴れるかどうかは Hud.GaugeState ではなくボス本体（Enemy.GaugeVulnerable／GaugeBreakFresh）から直接読む。
// 色味：ゲージは盤面側に居るので、各 Root の CanvasModulate（夜の冷色 Tint）で金がくすんで黄土色に沈む。
//   BubbleLayer と同じく Tint の逆数を SelfModulate に置いて打ち消し、塗り・台座・点・斜線をすべて本来の色で見せる。
public partial class BossGauge : Node2D
{
    private Hud? _hud;
    private Enemy? _owner;

    private const float BarH = 2.6f;     // バーの高さ（world px。設計座標で約 9px）
    private const float PipPitch = 3.4f; // 残本数の点の間隔
    private const float PipR = 1.1f;

    private static readonly Color InvulnFill = new(0.60f, 0.63f, 0.68f);
    private const float InvulnAlpha = 0.55f;
    private static readonly Color HatchCol = new(0.10f, 0.10f, 0.12f, 0.6f);
    private const float HatchW = 0.8f, HatchPitch = 3f;
    // BREAK の金。本体まわりの無防備窓タイマーバー（e9d28b）より少し暖かく濃い＝Tint を打ち消した上で「金」と読める色。
    private static readonly Color BreakFill = new("f0c85a");

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
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_hud == null || _owner == null || !IsInstanceValid(_owner)) return;
        var s = _hud.GaugeState;
        if (!s.Visible || s.Fade <= 0f) return;

        float top = _owner.GaugeTop, w = _owner.GaugeWidth, a = s.Fade;
        // 改心の見送り：穢れ色（スペル色）→浄化色へ抜けてから薄れる（旧カードのアイコンと同じ段取り）。
        Color tint = s.Tint.Lerp(UiKit.PurifyHi, s.Purify);

        // 塗りの種類：見送り ＞ BREAK ＞ 無敵。
        string kind = s.Purify > 0f ? "purify" : _owner.GaugeVulnerable ? "break" : "invuln";
        LastFillKind = kind;
        Color fillCol = kind switch
        {
            "purify" => new Color(tint, a),
            "invuln" => new Color(InvulnFill, InvulnAlpha * a),
            // 割れた一拍（GaugeBreakFresh 1→0）は白へ寄せて光り、あとは 1.5Hz の脈で α 0.85〜1.0。
            _ => new Color(BreakFill.Lerp(Colors.White, 0.8f * _owner.GaugeBreakFresh),
                (0.85f + 0.15f * (0.5f + 0.5f * Mathf.Sin((float)_t * Mathf.Tau * 1.5f))) * a),
        };

        var bar = new Rect2(-w / 2f, top, w, BarH);
        DrawRect(bar.Grow(1f), new Color(0.13f, 0.12f, 0.15f, 0.85f * a));          // 台座
        DrawRect(bar, new Color(1f, 1f, 1f, 0.07f * a));                            // 空のトラック
        if (s.Frac > 0f)
        {
            var fill = new Rect2(bar.Position, new Vector2(w * s.Frac, BarH));
            DrawRect(fill, fillCol);
            if (kind == "invuln") DrawHatch(fill, a);
            DrawRect(new Rect2(fill.Position, new Vector2(fill.Size.X, 0.8f)), new Color(1f, 1f, 1f, 0.35f * a)); // 上端のハイライト
        }
        // バー1本割れの白フラッシュ（Hud.FlashBossBarBreak）
        if (s.Flash > 0f) DrawRect(bar, new Color(1f, 1f, 1f, 0.75f * s.Flash * a));

        // 残本数の点（左から「残っている本数」を満たす）。1本だけのボスは点を出さない（バーだけで足りる）。
        if (s.Total > 1)
        {
            int left = s.Index + 1;
            float x0 = -(s.Total - 1) * PipPitch / 2f;
            float py = top + BarH + 3.2f;
            for (int i = 0; i < s.Total; i++)
            {
                var c = new Vector2(x0 + i * PipPitch, py);
                DrawCircle(c, PipR + 0.6f, new Color(0.13f, 0.12f, 0.15f, 0.8f * a));   // 台座（空の点も数えられるように）
                DrawCircle(c, PipR, i < left ? new Color(tint, a) : new Color(tint, 0.4f * a));
            }
        }
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
