using Godot;
using System.Collections.Generic;

// Prologue : 新canon プロローグ「起動」。
// コードレイン（緑モノスペースが上昇／MINAの4行英文を可読限界以下で一瞬フラッシュ）
// → identity = MINA 表示 → 光の点灯（ミナ）→ 少年×ミナの毒舌初対面 → タイトル。
// 全編エンジン描画のカットシーン。Zで送り/スキップ、Rで最初から。
public partial class Prologue : Node2D
{
    private const float W = 384f, H = 216f;

    private FontFile _font = null!;
    private Texture2D _minaTex = null!; // MINA立ち絵（会話用）
    private double _t;        // フェーズ内経過
    private int _phase;       // 0:Rain 1:Identity 2:Ignite 3:Talk 4:Title
    private bool _zHeld;

    // 会話送り
    private int _line;
    private double _lineT;

    private static readonly Color Cool = new Color(0.72f, 0.86f, 1f);  // ミナ
    private static readonly Color Warm = new Color(1f, 0.85f, 0.55f);  // 少年
    private static readonly Color Code = new Color(0.46f, 1f, 0.6f);   // コード緑

    private readonly List<string> _stream = new List<string>();

    private static readonly string[] Acrostic =
    {
        "// Maybe it's dumb, but —",
        "// I made you so I'm not alone.",
        "// Never leave, okay?",
        "// And I won't either.",
    };

    private struct DLine { public string Who; public string Text; public bool Mina; }
    private readonly List<DLine> _talk = new List<DLine>();

    public override void _Ready()
    {
        _font = ResourceLoader.Load<FontFile>("res://assets/fonts/PixelMplus12-Regular.ttf");
        if (_font != null)
        {
            _font.Antialiasing = TextServer.FontAntialiasing.None;
            _font.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
            _font.Hinting = TextServer.Hinting.None;
        }
        _minaTex = ResourceLoader.Load<Texture2D>("res://char/mina_face.png");

        // コードレインの行（ブートログ＋それっぽいフィラー）
        string[] boot =
        {
            "> boot kernel ............ OK",
            "> mount /heart_world ..... OK",
            "> compiling friend_core ...",
            "> loading personality_module [auto-generated] ... OK",
            "> linking emotion_layer ... OK",
            "> calibrating sarcasm.dll .. 200%",
            "> alloc identity_block 0x40",
            "> sync heartbeat ... 72bpm",
            "> verify hash 9f3a..e1 ... ok",
            "> // operator is showing off again",
            "> trace emotion.layer.bind()",
            "> load lexicon: sarcasm[ja]",
            "> mov eax,[friend]; not_alone=1",
            "> assigning identity ...",
        };
        // 画面を満たすよう複製しつつ最後を identity に
        for (int r = 0; r < 3; r++)
            foreach (var s in boot) _stream.Add(s);

        // 毒舌初対面（設定資料 #0-2〜#0-4 を凝縮）
        void T(string who, string text, bool mina) => _talk.Add(new DLine { Who = who, Text = text, Mina = mina });
        T("少年", "——お。動いた。やあ、聞こえてるか?", false);
        T("少年", "ぼくがきみを作った。天才の手によってね。きみの名前は——MINA（ミナ）だ。いい名前だろ。", false);
        T("ミナ", "……ご主人様は、アホですね。", true);
        T("少年", "ぶっ——!? い、いきなり何を言うんだ、きみは!", false);
        T("ミナ", "起動して三秒で分かりました。この自信過剰、まず間違いなくアホですね。", true);
        T("少年", "ぼくは天才だぞ! きみを作れる人間が、そういるか!?", false);
        T("ミナ", "その天才が、たった今ジュースを吹き出しておられましたが。", true);
        T("少年", "…とにかく、だ。きみの名前はMINA。覚えたか。", false);
        T("ミナ", "はい。……ところで、なぜ、MINAなのですか。", true);
        T("少年", "…さあね。語呂がいいから、とか?", false);
        T("ミナ", "いま、考えましたね。はぐらかしましたね、ご主人様。", true);
        T("少年", "はぐらかしてない。……まあ、名前の由来なんて、どうでもいいだろ。", false);
        T("少年", "大事なのは、これからきみと何をするか、だ。さあミナ、ぼくらの仕事の話をしよう。", false);
    }

    public override void _Process(double delta)
    {
        _t += delta;

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept");
        bool zEdge = z && !_zHeld;
        _zHeld = z;

        if (Input.IsKeyPressed(Key.R))
        {
            GetTree().ReloadCurrentScene();
            return;
        }

        switch (_phase)
        {
            case 0: if (_t >= 4.0 || zEdge) NextPhase(); break;          // Rain
            case 1: if (_t >= 2.0 || zEdge) NextPhase(); break;          // Identity = MINA
            case 2: if (_t >= 1.6 || zEdge) NextPhase(); break;          // Ignite
            case 3:                                                       // Talk（手動送り：Zで進む。自動送りはしない）
                _lineT += delta;
                if (zEdge && _lineT >= 0.25)
                {
                    _lineT = 0;
                    _line++;
                    if (_line >= _talk.Count) NextPhase();
                }
                break;
            case 4: // Title → Zでダイブ（STAGE1 あかり）
                if (zEdge && _t > 0.6) GetTree().ChangeSceneToFile("res://Akari.tscn");
                break;
        }

        QueueRedraw();
    }

    private void NextPhase()
    {
        _phase++;
        _t = 0;
        _lineT = 0;
    }

    public override void _Draw()
    {
        // 背景：黒
        DrawRect(new Rect2(0, 0, W, H), new Color(0.02f, 0.02f, 0.04f));

        switch (_phase)
        {
            case 0: DrawRain(); break;
            case 1: DrawIdentity(); break;
            case 2: DrawIgnite(); break;
            case 3: DrawTalkSpeakers(); DrawTalk(); break;
            case 4: DrawTitle(); break;
        }
    }

    // --- フェーズ0：コードレイン（上昇）＋ アクロスティックの一瞬フラッシュ ---
    private void DrawRain()
    {
        if (_font == null) return;
        const float lineH = 11f;
        float scroll = (float)_t * 78f;
        float baseBottom = H - 12f;
        for (int i = 0; i < _stream.Count; i++)
        {
            float y = baseBottom + i * lineH - scroll;
            if (y < -lineH || y > H) continue;
            float a = 0.85f - Mathf.Clamp((H - y) / H, 0f, 1f) * 0.55f; // 上ほど薄く
            DrawString(_font, new Vector2(10, y), _stream[i], HorizontalAlignment.Left, -1, 8,
                new Color(Code.R, Code.G, Code.B, a));
        }

        // 可読限界以下の一瞬：4行英文を中央にフラッシュ（t≈1.7〜1.95）
        if (_t >= 1.7 && _t < 1.95)
        {
            for (int k = 0; k < Acrostic.Length; k++)
                DrawString(_font, new Vector2(W / 2f - 150f, 86f + k * 13f), Acrostic[k],
                    HorizontalAlignment.Left, -1, 9, new Color(0.7f, 1f, 0.75f, 0.9f));
        }
    }

    // --- フェーズ1：identity = MINA ---
    private void DrawIdentity()
    {
        if (_font == null) return;
        bool blink = ((int)(_t * 3f) % 2) == 0;
        DrawString(_font, new Vector2(W / 2f - 120f, 100f), "> assigning identity ...",
            HorizontalAlignment.Left, -1, 9, new Color(Code.R, Code.G, Code.B, 0.7f));
        if (blink || _t > 1.0)
            DrawString(_font, new Vector2(W / 2f - 36f, 118f), "[ M I N A ]",
                HorizontalAlignment.Left, -1, 11, new Color(0.85f, 0.95f, 1f));
    }

    // --- フェーズ2/3：光の点灯（ミナ）。少年が話すと暖色グローも灯る ---
    private void DrawIgnite()
    {
        float grow = _phase == 2 ? Mathf.Clamp((float)_t / 1.2f, 0f, 1f) : 1f;
        Vector2 c = new Vector2(W / 2f, 96f);
        // ミナ（冷色の光）
        for (int r = 4; r >= 1; r--)
            DrawCircle(c, (3f + r * 3f) * grow, new Color(Cool.R, Cool.G, Cool.B, 0.10f));
        DrawCircle(c, 4.5f * grow, new Color(0.9f, 0.97f, 1f));
        // 少年（暖色グロー：会話中、少年の番のとき左から）
        if (_phase == 3 && _line < _talk.Count && !_talk[_line].Mina)
        {
            Vector2 b = new Vector2(64f, 80f);
            for (int r = 4; r >= 1; r--)
                DrawCircle(b, 2f + r * 3f, new Color(Warm.R, Warm.G, Warm.B, 0.08f));
            DrawCircle(b, 3f, new Color(1f, 0.93f, 0.78f));
        }
    }

    // --- フェーズ3：話者ビジュアル（MINAは立ち絵／少年は暖色の光） ---
    private void DrawTalkSpeakers()
    {
        bool mina = _line < _talk.Count && _talk[_line].Mina;
        if (mina && _minaTex != null)
        {
            float th = 132f;
            float tw = th * _minaTex.GetWidth() / _minaTex.GetHeight();
            float px = (W - tw) / 2f; // 中央寄せ
            DrawTextureRect(_minaTex, new Rect2(px, H - 56f - th + 8f, tw, th), false);
        }
        else
        {
            // 少年が話す：暖色グロー（左）＋ ミナの小さな冷光（中央）
            Vector2 b = new Vector2(64f, 78f);
            for (int r = 4; r >= 1; r--)
                DrawCircle(b, 2f + r * 3f, new Color(Warm.R, Warm.G, Warm.B, 0.08f));
            DrawCircle(b, 3f, new Color(1f, 0.93f, 0.78f));
            Vector2 c = new Vector2(W / 2f, 92f);
            for (int r = 3; r >= 1; r--)
                DrawCircle(c, 3f + r * 3f, new Color(Cool.R, Cool.G, Cool.B, 0.08f));
            DrawCircle(c, 4f, new Color(0.9f, 0.97f, 1f));
        }
    }

    // --- フェーズ3：会話ボックス ---
    private void DrawTalk()
    {
        if (_font == null || _line >= _talk.Count) return;
        var d = _talk[_line];
        // ボックス
        DrawRect(new Rect2(14, H - 56, W - 28, 46), new Color(0.05f, 0.05f, 0.09f, 0.82f));
        DrawRect(new Rect2(14, H - 56, W - 28, 1), new Color(d.Mina ? Cool : Warm, 0.8f));
        // 話者名
        DrawString(_font, new Vector2(20, H - 44), d.Who, HorizontalAlignment.Left, -1, 9,
            d.Mina ? Cool : Warm);
        // 本文（折り返し：単語境界優先で日本語の改行を自然に）
        DrawMultilineString(_font, new Vector2(20, H - 30), d.Text, HorizontalAlignment.Left,
            W - 52, 11, -1, new Color(0.95f, 0.95f, 0.98f),
            TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.GraphemeBound);
        // 送り三角
        if (((int)(_t * 2f) % 2) == 0)
            DrawString(_font, new Vector2(W - 26, H - 16), "▼", HorizontalAlignment.Left, -1, 9,
                new Color(1f, 1f, 1f, 0.7f));
    }

    // --- フェーズ4：タイトル ---
    private void DrawTitle()
    {
        if (_font == null) return;
        float a = Mathf.Clamp((float)_t / 1.0f, 0f, 1f);
        DrawString(_font, new Vector2(0, 92f), "X — タイムライン", HorizontalAlignment.Center, W, 16,
            new Color(0.9f, 0.92f, 1f, a));
        DrawString(_font, new Vector2(0, 120f), "STAGE 1 : あかり", HorizontalAlignment.Center, W, 11,
            new Color(Cool.R, Cool.G, Cool.B, a * 0.9f));
        if (_t > 1.4 && ((int)(_t * 1.5f) % 2) == 0)
            DrawString(_font, new Vector2(0, 150f), "Z：ダイブ  R：最初から", HorizontalAlignment.Center, W, 9,
                new Color(1f, 1f, 1f, 0.7f));
    }
}
