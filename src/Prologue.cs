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
    private double _t;        // フェーズ内経過
    private int _phase;       // 0:Rain 1:Identity 2:Ignite 3:Talk 4:Title
    private bool _zHeld;

    // 会話送り
    private int _line;
    private double _lineT;

    // 難易度選択（タイトル）
    private int _diffSel = 1; // 0:Easy 1:Normal 2:Hard
    private bool _lrHeld;
    private static readonly string[] DiffNames = { "EASY", "NORMAL", "HARD" };

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

    private struct DLine { public string Who; public string Text; public string Face; }
    private readonly List<DLine> _talk = new List<DLine>();

    // 立ち絵パス（表情差分）
    private const string FMina = "res://char/mina_face.png";
    private const string FCocky = "res://char/shonen_face.png";    // 不敵・通常
    private const string FFluster = "res://char/shonen_fluster.png"; // 動揺・照れ
    private const string FProud = "res://char/shonen_proud.png";   // 得意げ
    private const string FGentle = "res://char/shonen_gentle.png"; // 素の優しさ

    public override void _Ready()
    {
        _font = ResourceLoader.Load<FontFile>("res://assets/fonts/PixelMplus12-Regular.ttf");
        if (_font != null)
        {
            _font.Antialiasing = TextServer.FontAntialiasing.None;
            _font.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
            _font.Hinting = TextServer.Hinting.None;
        }
        _diffSel = (int)(GetNodeOrNull<GameManager>("/root/Game")?.Difficulty ?? GameManager.Diff.Normal);

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

        // 毒舌初対面（シナリオ設計書v2 [P-00] PROLOGUE 準拠）
        void T(string who, string text, string face) => _talk.Add(new DLine { Who = who, Text = text, Face = face });
        T("少年", "やあ。聞こえてるか?", FCocky);
        T("少年", "ぼくがきみを作った。天才の手によってね。", FProud);
        T("少年", "きみの名前は——MINAだ。", FProud);
        T("ミナ", "……ご主人様は、アホですね。", FMina);
        T("少年", "ぶっ——!? い、いきなり何を言うんだ、きみは!", FFluster);
        T("ミナ", "なぜ、MINAなのですか。", FMina);
        T("少年", "……さあね。語呂がいいから、とか?", FFluster);
        T("ミナ", "いま、考えましたね。", FMina);
        T("少年", "Xの投稿——声にならない叫びの奥に、人の本当の心がある。きみはそこに、潜っていける。", FCocky);
        T("少年", "これでぼくらは——Xに蔓延る闇ってやつを、成敗しようじゃないか。", FProud);
        T("ミナ", "……その決めゼリフ、何回練習したんですか。", FMina);
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
            case 4: // Title → 難易度を左右で選び、Zでダイブ（STAGE1 レイ）
                bool left = Input.IsActionPressed("ui_left");
                bool right = Input.IsActionPressed("ui_right");
                if ((left || right) && !_lrHeld)
                {
                    if (left) _diffSel = Mathf.Max(0, _diffSel - 1);
                    if (right) _diffSel = Mathf.Min(2, _diffSel + 1);
                    var g = GetNodeOrNull<GameManager>("/root/Game");
                    if (g != null) g.Difficulty = (GameManager.Diff)_diffSel;
                }
                _lrHeld = left || right;
                if (zEdge && _t > 0.6) GetTree().ChangeSceneToFile("res://Rei.tscn");
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

    // --- フェーズ2：光の点灯（ミナ） ---
    private void DrawIgnite()
    {
        float grow = Mathf.Clamp((float)_t / 1.2f, 0f, 1f);
        Vector2 c = new Vector2(W / 2f, 96f);
        for (int r = 4; r >= 1; r--)
            DrawCircle(c, (3f + r * 3f) * grow, new Color(Cool.R, Cool.G, Cool.B, 0.10f));
        DrawCircle(c, 4.5f * grow, new Color(0.9f, 0.97f, 1f));
    }

    // --- フェーズ3：話者の立ち絵を中央に表示（行ごとの表情を反映） ---
    private void DrawTalkSpeakers()
    {
        if (_line >= _talk.Count) return;
        var tex = ResourceLoader.Load<Texture2D>(_talk[_line].Face);
        if (tex != null)
        {
            float th = 132f;
            float tw = th * tex.GetWidth() / tex.GetHeight();
            float px = (W - tw) / 2f; // 中央寄せ
            DrawTextureRect(tex, new Rect2(px, H - 56f - th + 8f, tw, th), false);
        }
    }

    // --- フェーズ3：会話ボックス ---
    private void DrawTalk()
    {
        if (_font == null || _line >= _talk.Count) return;
        var d = _talk[_line];
        bool mina = d.Who == "ミナ";
        // ボックス
        DrawRect(new Rect2(14, H - 56, W - 28, 46), new Color(0.05f, 0.05f, 0.09f, 0.82f));
        DrawRect(new Rect2(14, H - 56, W - 28, 1), new Color(mina ? Cool : Warm, 0.8f));
        // 話者名
        DrawString(_font, new Vector2(20, H - 44), d.Who, HorizontalAlignment.Left, -1, 9,
            mina ? Cool : Warm);
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
        DrawString(_font, new Vector2(0, 78f), "X — タイムライン", HorizontalAlignment.Center, W, 16,
            new Color(0.9f, 0.92f, 1f, a));
        DrawString(_font, new Vector2(0, 104f), "STAGE 1 : レイ", HorizontalAlignment.Center, W, 11,
            new Color(Cool.R, Cool.G, Cool.B, a * 0.9f));

        // 難易度選択（◀ ▶ で変更）
        DrawString(_font, new Vector2(0, 132f), "難易度  ◀ " + DiffNames[_diffSel] + " ▶",
            HorizontalAlignment.Center, W, 11, new Color(1f, 0.92f, 0.6f, a));

        if (_t > 1.0 && ((int)(_t * 1.5f) % 2) == 0)
            DrawString(_font, new Vector2(0, 158f), "← → 難易度   Z：ダイブ   R：最初から",
                HorizontalAlignment.Center, W, 9, new Color(1f, 1f, 1f, 0.7f));
    }
}
