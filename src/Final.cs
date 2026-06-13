using Godot;
using System.Collections.Generic;

// Final : FINAL「汚染」（v2 [P-FINAL]）。戦闘で解決しない本作ルールの総決算。
// ミナの内側で汚染が頂点に達し暴走（自機が黒く溶け、世界中の悲鳴が流れ込む）→
// 闇の向こうから少年の光がまっすぐ歩いてくる→少年の対話で帰還（指先が触れ世界が白くなる）。
// 全編エンジン描画のカットシーン。Zで送り、Rで最初から。終了で EPILOGUE へ。
public partial class Final : Node2D
{
    private const float W = 384f, H = 216f;

    private FontFile _font = null!;
    private double _t;
    private int _phase;   // 0:暴走 1:対話 2:帰還(白)
    private bool _zHeld;
    private int _line;
    private double _lineT;

    private static readonly Color Cool = new Color(0.72f, 0.86f, 1f);  // ミナ
    private static readonly Color Warm = new Color(1f, 0.85f, 0.55f);  // 少年
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    // 流れ込む悲鳴（背景に薄く流れる断片）
    private static readonly string[] Screams =
    {
        "むだだよ", "どうせ", "ごめんなさい", "とどかない", "もういない",
        "わたしのせいだ", "ひとりになる", "なんで", "きえたい", "たすけて",
    };
    private readonly List<(string s, float x, float y, float sp)> _drift = new();

    private struct DLine { public string Who; public string Text; }
    private readonly List<DLine> _talk = new List<DLine>();

    public override void _Ready()
    {
        _rng.Randomize();
        _font = ResourceLoader.Load<FontFile>("res://assets/fonts/PixelMplus12-Regular.ttf");
        if (_font != null)
        {
            _font.Antialiasing = TextServer.FontAntialiasing.None;
            _font.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
            _font.Hinting = TextServer.Hinting.None;
        }
        // 汚染ゲージの終着点：黒く溶ける。
        GetNodeOrNull<GameManager>("/root/Game")?.SetContamination(1f);

        for (int i = 0; i < 22; i++)
            _drift.Add((Screams[i % Screams.Length],
                _rng.RandfRange(0, W), _rng.RandfRange(0, H), _rng.RandfRange(10f, 34f)));

        // Who: "地"=ミナの語り（ナレ・回想／話者名なし・中央寄せ） / "ミナ"=ミナのセリフ / "少年"=少年のセリフ
        void T(string who, string text) => _talk.Add(new DLine { Who = who, Text = text });
        T("地", "穢れを祓うたび、それはわたくしの中に溜まっていた。");
        T("地", "三人ぶんの、悲しみと、怒りと、届かなかった想い。");
        T("少年", "やれやれ。ぼくの最高傑作が、形無しだな。");
        T("少年", "きみは、ぼくの自慢なんだ。口は悪いし、生意気だし、ぼくをアホ呼ばわりするし——");
        T("少年", "最高なんだよ、きみは。");
        T("少年", "シェイクスピアは言った。\"Cowards die many times before their deaths.\"");
        T("少年", "臆病者は、死ぬ前に何度も死ぬ。——なあ、ミナ。ぼくは臆病者だから、もう何回も死んでるんだ。");
        T("地", "ただ、その声が、命綱でした。わたくしは、その光に向かって泳ぎました。");
        T("地", "その光が、いつもよりずっと薄かったこと。わたくしは、気づいていました。");
        T("ミナ", "……ご主人様は、アホですね。");
        T("地", "——それが、ご主人様と交わした、最後の軽口になりました。");
    }

    public override void _Process(double delta)
    {
        _t += delta;
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        _zHeld = z;

        if (Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start))
        {
            GetTree().ReloadCurrentScene();
            return;
        }

        // 悲鳴の漂い更新
        for (int i = 0; i < _drift.Count; i++)
        {
            var d = _drift[i];
            d.y -= d.sp * (float)delta;
            if (d.y < -10f) { d.y = H + 8f; d.x = _rng.RandfRange(0, W); }
            _drift[i] = d;
        }

        switch (_phase)
        {
            case 0: if (_t >= 3.2 || zEdge) NextPhase(); break;          // 暴走の見せ
            case 1:                                                       // 対話（手動送り）
                _lineT += delta;
                if (zEdge && _lineT >= 0.25)
                {
                    _lineT = 0; _line++;
                    if (_line >= _talk.Count) NextPhase();
                }
                break;
            case 2: // 帰還（白）→ EPILOGUE
                if (_t >= 3.0) GetTree().ChangeSceneToFile("res://Epilogue.tscn");
                break;
        }
        QueueRedraw();
    }

    private void NextPhase() { _phase++; _t = 0; _lineT = 0; }

    public override void _Draw()
    {
        // 背景：暴走中は黒、帰還で白へ。
        if (_phase < 2)
            DrawRect(new Rect2(0, 0, W, H), new Color(0.01f, 0.01f, 0.02f));
        else
        {
            float a = Mathf.Clamp((float)_t / 1.5f, 0f, 1f);
            DrawRect(new Rect2(0, 0, W, H), new Color(0.01f, 0.01f, 0.02f).Lerp(new Color(1f, 1f, 1f), a));
        }

        if (_phase < 2)
        {
            DrawScreams();
            DrawCorruptedCore();
            if (_phase >= 1) DrawApproachingLight();
            DrawTalk();
        }
        else
        {
            // 帰還後：薄い光（少年）と取り戻したミナの光が並ぶ。
            float a = Mathf.Clamp((float)_t / 1.5f, 0f, 1f);
            DrawCircle(new Vector2(W / 2f - 14f, H / 2f), 5f, new Color(Cool.R, Cool.G, Cool.B, 1f - a * 0.3f));
            DrawCircle(new Vector2(W / 2f + 14f, H / 2f), 4f, new Color(Warm.R, Warm.G, Warm.B, (1f - a) * 0.5f)); // 薄い
        }
    }

    private void DrawScreams()
    {
        if (_font == null) return;
        foreach (var d in _drift)
            DrawString(_font, new Vector2(d.x, d.y), d.s, HorizontalAlignment.Left, -1, 9,
                new Color(0.5f, 0.18f, 0.3f, 0.35f));
    }

    private void DrawCorruptedCore()
    {
        Vector2 c = new Vector2(W / 2f, H / 2f - 6f);
        float pulse = 1f + 0.12f * Mathf.Sin((float)_t * 4f);
        for (int r = 5; r >= 1; r--)
            DrawCircle(c, (6f + r * 5f) * pulse, new Color(0.08f, 0.02f, 0.10f, 0.22f));
        DrawCircle(c, 10f * pulse, new Color(0.04f, 0.02f, 0.06f));
        // にじむ濁った縁
        DrawArc(c, 12f * pulse, 0, Mathf.Tau, 28, new Color(0.32f, 0.12f, 0.28f, 0.5f), 1.5f);
    }

    private void DrawApproachingLight()
    {
        // 右の闇から中央へまっすぐ歩いてくる少年の光。
        float x = Mathf.Lerp(W - 20f, W / 2f + 22f, Mathf.Min(1f, _line / 6f));
        Vector2 c = new Vector2(x, H / 2f - 6f);
        for (int r = 3; r >= 1; r--)
            DrawCircle(c, 3f + r * 2.5f, new Color(Warm.R, Warm.G, Warm.B, 0.10f));
        DrawCircle(c, 3.2f, new Color(1f, 0.93f, 0.78f, 0.85f)); // 薄め（光が薄い伏線）
    }

    private void DrawTalk()
    {
        if (_font == null || _phase != 1 || _line >= _talk.Count) return;
        var d = _talk[_line];
        bool narr = d.Who == "地";       // ミナの語り＝話者名なし・中央寄せでセリフと区別
        bool mina = d.Who == "ミナ";
        Color edge = narr ? new Color(0.62f, 0.64f, 0.72f) : (mina ? Cool : Warm);
        DrawRect(new Rect2(14, H - 56, W - 28, 46), new Color(0.05f, 0.05f, 0.09f, 0.85f));
        DrawRect(new Rect2(14, H - 56, W - 28, 1), new Color(edge, 0.8f));
        if (!narr)
            DrawString(_font, new Vector2(20, H - 44), d.Who, HorizontalAlignment.Left, -1, 9, edge);
        var align = narr ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        DrawMultilineString(_font, new Vector2(20, H - 30), d.Text, align,
            W - 52, 11, -1, new Color(0.95f, 0.95f, 0.98f),
            TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.GraphemeBound);
        if (((int)(_t * 2f) % 2) == 0)
            DrawString(_font, new Vector2(W - 26, H - 16), "▼", HorizontalAlignment.Left, -1, 9,
                new Color(1f, 1f, 1f, 0.7f));
    }
}
