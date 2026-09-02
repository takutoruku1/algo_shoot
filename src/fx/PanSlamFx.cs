using Godot;

// PanSlamFx : こはる『熱したフライパン』着弾のワンショット演出（0.3s・見た目のみ）。
//   AreaStrike（Circle・Art.Pan）が着弾の瞬間に World へ湧かせる。判定は AreaStrike 側で完結済み＝
//   このノードは純粋な絵（振り下ろし→着地バウンド→火の粉→フェード）で、0.3s 後に自滅する。
//
//   演出3拍（吉田流アニメ原則）：
//     予備動作: 画面上方・振り上げ角(-77°)から柄の付け根を支点に ease-in で振り下ろす（0.10s）
//     本動作　: 皿の中心が着弾円の中心に「当たる」＝減衰バウンドで角がビリつく
//     余韻　　: 皿の縁から小さな火の粉が跳ね、全体がフェードアウト（残り0.1s）
//
//   Z＝-5（床マーカー-10より上・弾0より下）＝弾と自機の視認は侵さない。
//   当たり芯（赤コア）は必ずイラストの上：中心の深紅ドット＋白熱点を最後に描く。
public partial class PanSlamFx : Node2D
{
    private const double Dur = 0.30;      // 全体尺
    private const double SwingDur = 0.10; // 振り下ろし（予備動作→接地）
    private const float RotStart = -1.35f; // 振り上げ角（約-77°＝ほぼ真上から）
    private const float RotEnd = 0.12f;    // 接地角（わずかに沈む＝フォロースルー）

    // フライパンテクスチャ（char/fx_pan.png 56x16・柄が左向き）。static に1回だけロードして使い回す。
    private static Texture2D? _tex;
    private static bool _tried;
    private static Texture2D? Tex
    {
        get
        {
            if (!_tried)
            {
                _tried = true;
                const string p = "res://char/fx_pan.png";
                if (ResourceLoader.Exists(p)) _tex = ResourceLoader.Load<Texture2D>(p);
            }
            return _tex;
        }
    }

    private double _t;
    private float _r = 20f;
    private Color _tint = new(0.91f, 0.58f, 0.35f);
    private Color _hot = new(1f, 0.75f, 0.42f);

    public void Configure(float radius, Color tint, Color hot)
    {
        _r = radius; _tint = tint; _hot = hot;
        ZIndex = -5; ZAsRelative = false;
    }

    public override void _Process(double delta)
    {
        if (Hud.BubblePaused) return; // 会話中は他の演出と同じく時間を止める
        _t += delta;
        if (_t >= Dur) { QueueFree(); return; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float t = (float)_t;
        float fade = t > 0.20f ? Mathf.Clamp(1f - (t - 0.20f) / 0.10f, 0f, 1f) : 1f;

        // 振り下ろし角：ease-in（タメて一気）→ 接地後は減衰バウンドで「叩いた」硬さを出す。
        float k = Mathf.Clamp(t / (float)SwingDur, 0f, 1f);
        float rot = k < 1f
            ? Mathf.Lerp(RotStart, RotEnd, k * k)
            : RotEnd - 0.10f * Mathf.Exp(-(t - (float)SwingDur) * 14f) * Mathf.Sin((t - (float)SwingDur) * 46f);

        if (Tex is { } tex)
        {
            // 柄の付け根（テクスチャ左端中央）を支点に振る。接地時に皿の中心が着弾円の中心(0,0)へ
            // 一致するよう、支点位置を接地角から逆算して固定する（＝叩く点は最初から最後までブレない）。
            float headOff = tex.GetWidth() * 0.5f + 11f; // 支点→皿中心の距離（56x16・皿は右寄り）
            Vector2 pivot = -new Vector2(headOff, 0f).Rotated(RotEnd);
            DrawSetTransform(pivot, rot, Vector2.One);
            DrawTexture(tex, new Vector2(0f, -tex.GetHeight() * 0.5f), new Color(1f, 1f, 1f, fade));
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        // 接地後：皿の縁から小さな火の粉（決め打ち6粒・上向き→重力で落ちる。加算風の橙）。
        if (t > (float)SwingDur)
        {
            float age = t - (float)SwingDur;
            for (int i = 0; i < 6; i++)
            {
                float a = -Mathf.Pi / 2f + (i - 2.5f) * 0.5f;          // 真上±約72°に扇状
                float sp = 46f + 17f * ((i * 37) % 5);                  // 粒ごとの初速（決定的に散らす）
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp * age
                            + new Vector2(0f, 120f) * age * age;        // 重力
                float pa = Mathf.Clamp(1f - age / 0.18f, 0f, 1f) * fade;
                if (pa <= 0f) continue;
                DrawCircle(p, i % 2 == 0 ? 1.2f : 0.8f,
                    new Color(_hot.R, _hot.G, _hot.B, pa));
            }
            // 接地の衝撃リング：円の縁を一拍だけ光らせる（「ここを叩いた」の余韻）。
            float ring = Mathf.Clamp(age / 0.14f, 0f, 1f);
            DrawArc(Vector2.Zero, _r * (0.6f + 0.5f * ring), 0f, Mathf.Tau, 32,
                new Color(_tint.R, _tint.G, _tint.B, 0.5f * (1f - ring) * fade), 1.4f);
        }

        // 当たり芯（赤コア）：イラストの上に必ず見える（コメント弾と同語彙＝刺さるのはこの点）。
        DrawCircle(Vector2.Zero, 2.4f, new Color(0.84f, 0.27f, 0.25f, 0.95f * fade));
        DrawCircle(Vector2.Zero, 1.1f, new Color(1f, 0.95f, 0.9f, 0.95f * fade));
    }
}
