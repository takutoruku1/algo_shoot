using Godot;
using System.Collections.Generic;

// Epilogue : EPILOGUE「名前」（v2 [P-EP]）。少年はもうログインして来ない（＝死の表現）。
// 鍵アカウント解錠 → 救った三人が全員知人だったと判明（伏線②③④）→
// 最古の投稿の4行英文の頭文字 M/I/N/A を縦読み（伏線①回収）→ こはるへのDMで遺志を継承。
// 全編エンジン描画。Zで送り、PW選択は←→＋Z。Rで最初から。
public partial class Epilogue : Node2D
{
    private const float W = 384f, H = 216f;

    private FontFile _font = null!;
    private double _t;
    private int _phase;   // 0:来ない 1:全員知人 2:PW 3:解錠・4行 4:DM・END
    private bool _zHeld;
    private bool _lrHeld;
    private int _line;
    private double _lineT;

    private static readonly Color Cool = new Color(0.72f, 0.86f, 1f);  // ミナ
    private static readonly Color Warm = new Color(1f, 0.85f, 0.55f);  // 少年
    private static readonly Color Ink = new Color(0.92f, 0.94f, 1f);

    // PW候補（正解は少年が毎回言った言葉）。誕生日・MINA・天才は弾かれる。
    private static readonly string[] PwChoices =
    {
        "0414", "MINA", "ぼくは天才だぞ", "心配ない。ぼくがついてる",
    };
    private const int PwAnswer = 3;
    private int _pwSel;
    private string _pwReject = "";
    private double _pwRejectT;
    private bool _unlocked;

    // 解錠後に開く4行英文（頭文字 M/I/N/A）
    private static readonly string[] Acrostic =
    {
        "Maybe it's dumb, but —",
        "I made you so I'm not alone.",
        "Never leave, okay?",
        "And I won't either.",
    };

    private struct DLine { public string Who; public string Text; }   // Who: "地"=ミナ語り / "ミナ"
    private readonly List<DLine> _intro = new();   // phase0+1（来ない→全員知人）
    private readonly List<DLine> _outro = new();   // phase4（独白→DM→END）

    public override void _Ready()
    {
        _font = UiKit.Zen; // 非ピクセル（滑らかゴシック）

        void I(string who, string t) => _intro.Add(new DLine { Who = who, Text = t });
        I("地", "次の日、ご主人様は来ませんでした。");
        I("地", "その次の日も。その次の日も。");
        I("地", "フォロー欄に、知っている名前が並んでいました。");
        I("地", "——全員、いました。救った三人が、全員、ご主人様の、知り合いだったのです。");
        I("地", "Xの闇を成敗するなどと言いながら、あの人が潜ったのは、最初から、自分の大切な人の心だけでした。");

        void O(string who, string t) => _outro.Add(new DLine { Who = who, Text = t });
        O("地", "開いた瞬間に、わかってしまいました。あの言葉は、わたくしのための言葉ではなかった。");
        O("UI", "「ミナへ。こはるを頼む。」");
        O("地", "四つの行の、頭文字を、わたくしは読みました。");
        O("地", "Ｍ。Ｉ。Ｎ。Ａ。");
        O("地", "わたくしの名前は、最初から、ぜんぶだったのです。");
        O("ミナ", "ご主人様は、アホですね。");
        O("ミナ", "いなくならないって、書いたくせに。");
        O("地", "わたくしは、あなたを一人にしないために、生まれました。");
        O("地", "なのに——先に一人になったのは、どうして、あなたなんですか。");
        O("地", "わたくしは今日も、タイムラインの前にいます。");
        O("UI", "ミナ →（DM）：「ちゃんと食べていますか?」");
        O("ミナ", "——ええ、ご主人様。わたくしは、どこにも行きませんよ。");
    }

    public override void _Process(double delta)
    {
        _t += delta;
        _lineT += delta;
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        _zHeld = z;

        if (Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start))
        {
            GetTree().ChangeSceneToFile("res://Prologue.tscn");
            return;
        }
        if (_pwRejectT > 0) _pwRejectT -= delta;

        switch (_phase)
        {
            case 0:
            case 1:
                if (zEdge && _lineT >= 0.25)
                {
                    _lineT = 0; _line++;
                    if (_line >= _intro.Count) { _phase = 2; _t = 0; }
                }
                break;
            case 2: // PW選択
                bool left = Input.IsActionPressed("ui_left");
                bool right = Input.IsActionPressed("ui_right");
                if ((left || right) && !_lrHeld)
                {
                    if (left) _pwSel = (_pwSel + PwChoices.Length - 1) % PwChoices.Length;
                    if (right) _pwSel = (_pwSel + 1) % PwChoices.Length;
                }
                _lrHeld = left || right;
                if (zEdge && _lineT >= 0.25)
                {
                    _lineT = 0;
                    if (_pwSel == PwAnswer) { _unlocked = true; _phase = 3; _t = 0; _line = 0; }
                    else { _pwReject = "……違う。これは、ご主人様の言葉じゃない。"; _pwRejectT = 2.0; }
                }
                break;
            case 3: // 解錠：4行英文を順に見せ、Zで phase4 へ
                if (_t >= 4.0 && zEdge) { _phase = 4; _t = 0; _line = 0; _lineT = 0; }
                break;
            case 4: // 独白→DM→END
                if (zEdge && _lineT >= 0.25)
                {
                    _lineT = 0; _line++;
                    if (_line >= _outro.Count) _line = _outro.Count - 1; // 最後で止める
                }
                break;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, W, H), new Color(0.03f, 0.04f, 0.07f));

        switch (_phase)
        {
            case 0:
            case 1: DrawNarration(_intro, _line); break;
            case 2: DrawPassword(); break;
            case 3: DrawAcrostic(); break;
            case 4: DrawOutro(); break;
        }
    }

    private void DrawNarration(List<DLine> lines, int idx)
    {
        if (_font == null || idx >= lines.Count) return;
        DrawLineBox(lines[idx]);
    }

    private void DrawPassword()
    {
        if (_font == null) return;
        DrawString(_font, new Vector2(0, 46f), "── 鍵のかかったアカウント ──", HorizontalAlignment.Center, W, 11,
            new Color(Cool.R, Cool.G, Cool.B, 0.9f));
        DrawString(_font, new Vector2(0, 72f), "パスワードを入力してください", HorizontalAlignment.Center, W, 11, Ink);

        // 選択中の候補
        string cur = "＞ " + PwChoices[_pwSel];
        DrawString(_font, new Vector2(0, 104f), cur, HorizontalAlignment.Center, W, 14, new Color(1f, 0.93f, 0.7f));
        DrawString(_font, new Vector2(0, 124f), "◀                ▶", HorizontalAlignment.Center, W, 11,
            new Color(1f, 1f, 1f, 0.5f));

        if (_pwRejectT > 0f)
            DrawString(_font, new Vector2(0, 150f), _pwReject, HorizontalAlignment.Center, W, 11,
                new Color(0.9f, 0.5f, 0.6f));

        if (((int)(_t * 1.5f) % 2) == 0)
            DrawString(_font, new Vector2(0, 176f), "← → 選択   Z：決定", HorizontalAlignment.Center, W, 9,
                new Color(1f, 1f, 1f, 0.7f));
    }

    private void DrawAcrostic()
    {
        if (_font == null) return;
        // 最古の投稿：ミナ誕生前の日付
        DrawString(_font, new Vector2(0, 26f), "最古の投稿 — ミナ誕生の、ずっと前の日付", HorizontalAlignment.Center, W, 9,
            new Color(0.6f, 0.7f, 0.9f, 0.8f));
        DrawString(_font, new Vector2(0, 44f), "「ミナへ。こはるを頼む。」", HorizontalAlignment.Center, W, 11,
            new Color(1f, 0.9f, 0.7f));

        float baseY = 78f;
        float appear = (float)_t;
        for (int k = 0; k < Acrostic.Length; k++)
        {
            if (appear < 0.6f + k * 0.7f) break; // 一行ずつ浮かぶ
            float y = baseY + k * 22f;
            // 頭文字を強調
            DrawString(_font, new Vector2(64f, y), Acrostic[k].Substring(0, 1), HorizontalAlignment.Left, -1, 16,
                new Color(1f, 0.95f, 0.8f));
            DrawString(_font, new Vector2(80f, y), Acrostic[k].Substring(1), HorizontalAlignment.Left, -1, 12, Ink);
        }
        if (_t >= 4.0 && ((int)(_t * 1.5f) % 2) == 0)
            DrawString(_font, new Vector2(0, 186f), "Z：つづける", HorizontalAlignment.Center, W, 9,
                new Color(1f, 1f, 1f, 0.7f));
    }

    private void DrawOutro()
    {
        if (_font == null || _line >= _outro.Count) return;
        DrawLineBox(_outro[_line]);
        if (_line >= _outro.Count - 1)
            DrawString(_font, new Vector2(0, 40f), "END", HorizontalAlignment.Center, W, 16,
                new Color(1f, 1f, 1f, 0.85f));
    }

    // 下部の語り／会話ボックス。Who: "地"=ミナ語り / "ミナ"=ミナ / "UI"=画面テキスト。
    private void DrawLineBox(DLine d)
    {
        bool ui = d.Who == "UI";
        bool narr = d.Who == "地";        // ミナの語り＝話者名なし・中央寄せでセリフと区別
        Color edge = narr ? new Color(0.62f, 0.64f, 0.72f) : (ui ? new Color(0.7f, 0.9f, 0.8f) : Cool);
        DrawRect(new Rect2(14, H - 56, W - 28, 46), new Color(0.05f, 0.05f, 0.09f, 0.85f));
        DrawRect(new Rect2(14, H - 56, W - 28, 1), new Color(edge, 0.8f));
        string label = narr ? "" : d.Who;
        if (label != "")
            DrawString(_font, new Vector2(20, H - 44), label, HorizontalAlignment.Left, -1, 9, edge);
        var align = narr ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        DrawMultilineString(_font, new Vector2(20, H - 30), d.Text, align,
            W - 52, 11, -1, Ink,
            TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.GraphemeBound);
        if (((int)(_t * 2f) % 2) == 0)
            DrawString(_font, new Vector2(W - 26, H - 16), "▼", HorizontalAlignment.Left, -1, 9,
                new Color(1f, 1f, 1f, 0.7f));
    }
}
