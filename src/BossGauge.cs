using Godot;

// ボス／中ボスの体力ゲージ（2026-09-27 作者指示「画面上部じゃなくて、簡略化して、実際に動いてるボスの上部に表示」）。
//   画面上端のカード（Hud.DrawBossCard＝アイコン・名前・ハンドル・リプ数・穢れバー・pip・残/総）をやめ、
//   ボス本体の頭上に「いまの1本」のバーと「残り本数」の点だけを置く。
//   ボスの子ノードなので、位置・表示／非表示（投稿で隠れる等）・Modulate のフェードはボスに追従し、
//   ボスが消えれば一緒に消える。状態（1本ぶんの割合・残本数・スペル色・割れフラッシュ・改心の見送り）は
//   従来どおり Hud が持つ＝各ボスの UpdateBossBar／SetBossBarTint／FlashBossBarBreak／HideBossBar は変えていない。
//   Z は絶対 11（自機 10・弾 0 より手前）＝弾幕の中でも読める。
public partial class BossGauge : Node2D
{
    private Hud? _hud;
    private Enemy? _owner;

    private const float BarH = 2.6f;     // バーの高さ（world px。設計座標で約 9px）
    private const float PipPitch = 3.4f; // 残本数の点の間隔
    private const float PipR = 1.1f;

    public static BossGauge Attach(Hud hud, Enemy owner)
    {
        var g = new BossGauge { Name = "BossGauge", ZAsRelative = false, ZIndex = 11, _hud = hud, _owner = owner };
        owner.AddChild(g);
        return g;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_hud == null || _owner == null || !IsInstanceValid(_owner)) return;
        var s = _hud.GaugeState;
        if (!s.Visible || s.Fade <= 0f) return;

        float top = _owner.GaugeTop, w = _owner.GaugeWidth, a = s.Fade;
        // 改心の見送り：穢れ色（スペル色）→浄化色へ抜けてから薄れる（旧カードのアイコンと同じ段取り）。
        Color tint = s.Tint.Lerp(UiKit.PurifyHi, s.Purify);

        var bar = new Rect2(-w / 2f, top, w, BarH);
        DrawRect(bar.Grow(1f), new Color(0.13f, 0.12f, 0.15f, 0.85f * a));          // 台座
        DrawRect(bar, new Color(1f, 1f, 1f, 0.07f * a));                            // 空のトラック
        if (s.Frac > 0f)
        {
            var fill = new Rect2(bar.Position, new Vector2(w * s.Frac, BarH));
            DrawRect(fill, new Color(tint, a));
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
}
