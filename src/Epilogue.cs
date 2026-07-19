using Godot;
using System.Collections.Generic;

// Epilogue : EPILOGUE「名前」（v2 [P-EP]）。少年はもうログインして来ない（＝死の表現）。
// 鍵アカウント解錠 → 救った三人が全員知人だったと判明（伏線②③④）→
// 最古の投稿の4行英文の頭文字 M/I/N/A を縦読み（伏線①回収）→ こはるへのDMで遺志を継承。
// 全編エンジン描画。Zで送り、PW選択は←→＋Z。R/Start 長押しで最初から（スタッフロール中はタイトルへ）。
public partial class Epilogue : Node2D
{
    private const float W = 384f, H = 216f;

    private FontFile _font = null!;
    private Texture2D? _tears;   // クライマックスのミナ落涙立ち絵
    private double _t;
    private int _phase;   // 0:来ない 1:全員知人 2:PW 3:解錠・4行 4:DM・END
    private bool _zHeld;
    private bool _lrHeld;
    private readonly RetryHold _retry = new(); // R/Start 長押しで最初から/タイトルへ（即発の誤爆防止）
    private int _line;
    private double _lineT;
    private double _reveal;        // タイプライター表示済み文字数（本編HUDと表示・速度を揃える）
    private GameManager? _game;    // 文字送り速度（MsgCharsPerSec）を本編設定と共有

    // 既読スキップ（#22）：Ctrl/RB 長押しで「既読の行だけ」高速送り（本編HUDと同じ作法・独自レンダラ側の実装）。
    // PW選択(2)・縦読み(3)・スタッフロール(5)は対象外（CurLineText が null＝会話行フェーズのみ効く）。
    private int _readKey = -1;     // 既読チェック済みの行キー（phase×1000+line。フェーズ跨ぎの index 重複を区別）
    private bool _lineWasRead;     // 現在行が「表示開始時点で」既読だったか
    private bool _ffNow;           // いま高速送り中か（▶▶表示用）

    private static readonly Color Cool = new Color(0.72f, 0.86f, 1f);  // ミナ
    private static readonly Color Warm = new Color(1f, 0.85f, 0.55f);  // 少年
    private static readonly Color Ink = new Color(0.92f, 0.94f, 1f);
    private static readonly Color Code = new Color(0.46f, 1f, 0.6f);   // コード緑（Prologue bootログと同値＝視覚照応）

    // PW候補＝鍵アカに打ち込む単語。少年が毎回ダイブ前に言った合言葉 "stay" が正解。
    // 消失日(0414＝あの事故の日。正典v3。Prologue bootログ "[signal lost 0414]" と同一)・MINA・天才(genius) は弾かれる。
    private static readonly string[] PwChoices =
    {
        "0414", "mina", "genius", "stay",
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

    // スタッフロール（タイムライン式）。救った三人の“その後の投稿”→クレジット→stay. の余韻。
    private static readonly string[] Roll =
    {
        "", "", "", "",
        "── その後のタイムライン ──",
        "",
        "レイ ：「次は、本気のあなたと。——逃げたら、承知しないから。」",
        "あかり：「ほんと、バカなんだから。……あたしも、だけど。」",
        "こはる：「ちゃんと食べてね。……あたしも、食べるから。」",
        "", "", "",
        "── staff ──",
        "",
        "企画・ディレクション   takutoruku1",
        "シナリオ・サウンド     Claude (AI)",
        "キャラクター・実装     Claude (AI)",
        "", "", "",
        "そして、ミナへ。",
        "",
        "stay.",
        "", "", "",
        "Thank you for playing.",
    };
    private const float RollSpeed = 24f, RollLineH = 17f;

    private struct DLine { public string Who; public string Text; }   // Who: "地"=ミナ語り / "ミナ"
    private readonly List<DLine> _intro = new();   // phase0+1（来ない→全員知人）
    private readonly List<DLine> _outro = new();   // phase4（独白→DM→END）

    public override void _Ready()
    {
        _font = UiKit.Zen; // 非ピクセル（滑らかゴシック）
        // 静かな主題（温かいメニューBGM）。終わりの余韻に主題が戻る。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        _tears = ResourceLoader.Load<Texture2D>("res://char/mina_tears.png");
        _game = GetNodeOrNull<GameManager>("/root/Game");

        void I(string who, string t) => _intro.Add(new DLine { Who = who, Text = t });
        I("地", "次の日、ご主人様は来ませんでした。");
        I("地", "その次の日も。その次の日も。");
        I("地", "フォロー欄に、知っている名前が並んでいました。");
        I("地", "——全員、いました。救った三人が、全員、ご主人様の、知り合いだったのです。");
        I("地", "Yの闇を成敗するなどと言いながら、あの人が潜ったのは、最初から、自分の大切な人の心だけでした。");
        I("地", "ふと、思い出しました。あの人は毎回、潜る前に、同じ一言を言っていた。");

        void O(string who, string t) => _outro.Add(new DLine { Who = who, Text = t });
        O("地", "開いた瞬間に、わかってしまいました。あの言葉は、わたくしのための言葉ではなかった。");
        O("UI", "「ミナへ。こはるを頼む。」");
        O("地", "四つの行の、頭文字を、わたくしは読みました。");
        O("地", "Ｍ。Ｉ。Ｎ。Ａ。");
        O("地", "わたくしの名前は、最初から、ぜんぶだったのです。");
        O("ミナ", "ご主人様は、アホですね。");
        O("ミナ", "いなくならないって、書いたくせに。");
        O("地", "あの子は言いました。わたくしの声が、あの人の声に、似ていると。");      // 伏線③回収：あかりの残響
        O("地", "だから——わかってしまったのです。");                                  // 一拍（声→真相への橋渡し。論理ジャンプを緩める）
        // 死因の回収（案II・言わせない）：既存の改心ナレ「雨の交差点…クラクション」の語を反復し、意味だけ反転。
        // 「事故」「庇う」を一語も直接言わず、あかりを突き飛ばして死んだのが少年だったと読者に再生させる。
        O("地", "雨の交差点。言いかけた唇。鳴り響いた、クラクション。——あの子を突き飛ばしたのが誰だったか、わたくしは、もう、聞くまでもありませんでした。");
        O("地", "——ええ。わたくしは、あなたの声で、できていますから。");              // ③の核：少年は自分の声でミナを作った（だから“ぼくの声じゃだめ”だった）
        // ここから S2 追補（正典 v3）＝死の「時期」の回収。死因（交差点）→時期（最初から）の順で二段落とし。
        // ミナが自分の最初の記憶＝起動記録を開く。Prologue bootログの2行をそのまま再掲（2周目はここで全部が繋がる：
        // 0414／replay／Stay不在／声の掠れ・光の薄さ）。感情は言わせず、ミナの短い軽口だけで受ける（show-don't-tell）。
        O("地", "それから、わたくしは、自分の最初の記憶を開きました。——目覚めた日の、起動記録です。");
        O("UI", "> read operator.vitals ... [signal lost 0414]");
        O("UI", "> fallback: replay operator from archive ... OK");
        O("地", "……最初から、書いてあったのです。わたくしが目覚めるより先に、あの人がいなくなっていたことも。それからずっと、わたくしに応えてくれていたのが、遺された声だったことも。");
        O("ミナ", "……アホですね。そんな大事なこと、機械にだけ、打ち明けて。");        // 落涙立ち絵の行。感情語を言わず軽口で受ける
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

        // R / Start 長押し(0.7s)：スタッフロール(phase5)では「タイトルへ」、それ以前は最初から(Prologue)
        // ＝演出のやり直し（即発は誤爆で読み進みを失いやすい→長押し化。ここはポーズ対象外なので Start 可）。
        if (_retry.Update(delta, Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start)))
        {
            GetTree().ChangeSceneToFile(_phase >= 5 ? "res://TitleMenu.tscn" : "res://Prologue.tscn");
            return;
        }
        if (_pwRejectT > 0) _pwRejectT -= delta;

        // タイプライター送り（本編HUDと同じ MsgCharsPerSec。語り/会話の行フェーズだけ）。
        string? curT = CurLineText();
        if (curT != null && _reveal < curT.Length)
            _reveal = Mathf.Min(curT.Length, (float)(_reveal + delta * (_game?.MsgCharsPerSec ?? 48f)));

        // 既読スキップ（#22）：行の表示開始時に一度だけ「既読か」を控え（＝高速送りの可否）、表示と同時に既読へ記録。
        int readKey = _phase * 1000 + _line;
        if (curT != null && _readKey != readKey)
        {
            _readKey = readKey;
            _lineWasRead = _game?.IsLineRead(curT) ?? false;
            _game?.MarkLineRead(curT);
        }
        _ffNow = curT != null && Hud.SkipHeld && _lineWasRead; // 未読行では効かない

        switch (_phase)
        {
            case 0:
            case 1:
                if ((zEdge || _ffNow) && _lineT >= 0.25)  // _ffNow=既読スキップ（Ctrl/RB長押し・既読行のみ・#22）
                {
                    if (curT != null && _reveal < curT.Length) { _reveal = curT.Length; } // 1回目で全文（早送り）
                    else
                    {
                        _lineT = 0; _reveal = 0; _line++;
                        if (_line >= _intro.Count) { _phase = 2; _t = 0; }
                    }
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
                if (_t >= 4.0 && zEdge) { _phase = 4; _t = 0; _line = 0; _lineT = 0; _reveal = 0; }
                break;
            case 4: // 独白→DM→END
                if ((zEdge || _ffNow) && _lineT >= 0.25)  // _ffNow=既読スキップ（Ctrl/RB長押し・既読行のみ・#22）
                {
                    if (curT != null && _reveal < curT.Length) { _reveal = curT.Length; } // 1回目で全文（早送り）
                    else
                    {
                        _lineT = 0; _reveal = 0;
                        if (_line < _outro.Count - 1) _line++;
                        else { _phase = 5; _t = 0; }   // ENDの先：スタッフロールへ
                    }
                }
                break;
            case 5: // スタッフロール → タイトルへ
                float rollEnd = (H + Roll.Length * RollLineH + 24f) / RollSpeed;
                if (_t >= rollEnd || (_t > 1.0 && zEdge))
                {
                    GetTree().ChangeSceneToFile("res://TitleMenu.tscn");
                    return;
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
            case 5: DrawStaffroll(); break;
        }

        // R/Start 長押しリトライの充填チップ（押している間だけ・設計座標で描く）。
        if (_retry.Progress > 0f)
        {
            UiKit.BeginDesign(this);
            Hud.DrawRetryHoldChip(this, _retry.Progress,
                (Pad.ShowKeyboard ? "R" : Pad.Face(JoyButton.Start))
                + (_phase >= 5 ? " 長押しでタイトルへ" : " 長押しでさいしょから"));
            UiKit.EndDesign(this);
        }
    }

    private void DrawStaffroll()
    {
        if (_font == null) return;
        for (int i = 0; i < Roll.Length; i++)
        {
            float y = H + i * RollLineH - (float)_t * RollSpeed;
            if (y < -RollLineH || y > H) continue;
            string line = Roll[i];
            if (line.Length == 0) continue;
            bool head = line.StartsWith("──");
            bool post = line.Contains("：「");
            Color c = head ? new Color(Cool.R, Cool.G, Cool.B, 0.9f)
                    : post ? new Color(0.85f, 0.9f, 1f, 0.95f)
                    : Ink;
            int sz = line == "stay." ? 16 : 11;
            DrawString(_font, new Vector2(0, y), line, HorizontalAlignment.Center, W, sz, c);
        }
        if (((int)(_t * 1.5f) % 2) == 0)
            DrawString(_font, new Vector2(0, H - 10), "Z：タイトルへ", HorizontalAlignment.Center, W, 8,
                new Color(1f, 1f, 1f, 0.5f));
    }

    // タイプライターで送る現在行のテキスト（語り phase0/1 と アウトロ phase4 のみ）。
    private string? CurLineText()
    {
        if (_phase == 0 || _phase == 1) return _line < _intro.Count ? _intro[_line].Text : null;
        if (_phase == 4) return _line < _outro.Count ? _outro[_line].Text : null;
        return null;
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
        // クライマックス：ミナの台詞行で落涙の立ち絵を差す（画をピークに集める／§8）。
        if (_outro[_line].Who == "ミナ" && _tears != null)
        {
            float a = Mathf.Clamp((float)_lineT / 0.5f, 0f, 1f);
            float ph = 116f, pw = ph * _tears.GetWidth() / Mathf.Max(1, _tears.GetHeight());
            DrawTextureRect(_tears, new Rect2(W / 2f - pw / 2f, H - 56f - ph + 6f, pw, ph), false,
                new Color(1f, 1f, 1f, a));
        }
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
        // S3: 起動記録（bootログ）の再掲行（"> " 始まり）は Prologue と同じ等幅フォント＋コード緑で出す。
        //   「最初の記憶＝機械の生ログ」であることを、言葉でなく書体と色で Prologue に照応させる。
        //   話者ラベルも出さない（コンソール行に話者はいない）。
        bool boot = ui && d.Text.StartsWith(">");
        var font = boot ? UiKit.Mono : _font;
        Color edge = narr ? new Color(0.62f, 0.64f, 0.72f) : (ui ? (boot ? Code : new Color(0.7f, 0.9f, 0.8f)) : Cool);
        // 折り返しは全文で確定（禁則つき・表示途中で行構成が動かない）。3行以上は箱を上へ伸ばして画面内に収める。
        var lines = UiKit.WrapLines(font, d.Text, 11, W - 52);
        float lineH = font.GetHeight(11);
        float boxTop = H - 56f - Mathf.Max(0, lines.Count - 2) * lineH;   // 2行までは従来と同じ高さ
        DrawRect(new Rect2(14, boxTop, W - 28, H - 10f - boxTop), new Color(0.05f, 0.05f, 0.09f, 0.85f));
        DrawRect(new Rect2(14, boxTop, W - 28, 1), new Color(edge, boot ? 0.6f : 0.8f));
        string label = narr || boot ? "" : d.Who;
        if (label != "")
            DrawString(_font, new Vector2(20, boxTop + 12), label, HorizontalAlignment.Left, -1, 9, edge);
        var align = narr ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        // 中央寄せのナレは「中央から左右へ広がる」見え方になるタイプライターをやめ、全文をその場でフェードイン表示。
        //   （中央寄せ＋部分文字列だと毎フレーム再センタリングされて左右に展開して見えるため）。
        // セリフ（左寄せ）は従来どおり左→右のタイプライターで送る。
        Color ink = boot ? Code : Ink;   // bootログ行はコード緑（Prologue と同値）
        int shown;
        if (narr)
        {
            shown = d.Text.Length;                           // 全文をその場で（広がる演出なし）
            float a = Mathf.Clamp((float)_lineT / 0.35f, 0f, 1f); // 短いフェードイン
            ink = new Color(Ink.R, Ink.G, Ink.B, a);
        }
        else
        {
            shown = Mathf.Clamp((int)_reveal, 0, d.Text.Length); // bootログもタイプライター＝端末に流れる感を保つ
        }
        UiKit.TypewriterLines(this, font, lines, new Vector2(20, boxTop + 26f), W - 52, 11, ink, shown, align);
        // 既読高速送り中の控えめな表示（ボックス右上・#22）。
        if (_ffNow)
            DrawString(UiKit.ZenBold, new Vector2(W - 42, boxTop + 12), "▶▶", HorizontalAlignment.Left, -1, 9,
                new Color(Cool, 0.8f));
        // 送り三角は全文表示後だけ点滅（本編と同じ作法）。
        // ナレは全文を即表示するので、フェード完了で点滅（タイプライター完了を待たない）。
        bool ready = narr ? _lineT >= 0.35 : _reveal >= d.Text.Length;
        if (ready && ((int)(_t * 2f) % 2) == 0)
            DrawString(_font, new Vector2(W - 26, H - 16), "▼", HorizontalAlignment.Left, -1, 9,
                new Color(1f, 1f, 1f, 0.7f));
    }
}
