using Godot;
using System.Collections.Generic;

// Epilogue : EPILOGUE「名前」（v2 [P-EP]）。少年はもうログインして来ない（＝死の表現）。
// 鍵アカウント解錠 → 救った三人が全員知人だったと判明（伏線②③④）→
// 最古の投稿の4行英文の頭文字 M/I/N/A を縦読み（伏線①回収）→ こはるへのDMで遺志を継承。
// 全編エンジン描画。Zで送り、PW選択は←→＋Z。R/Start 長押しで最初から（スタッフロール中はタイトルへ）。
public partial class Epilogue : Node2D
{
    private const float W = 384f, H = 216f;
    private const float BgAlpha = 0.75f;
    private const double BgFadeSec = 0.6;

    private FontFile _font = null!;
    private Texture2D? _tears;   // クライマックスのミナ落涙立ち絵
    private readonly Texture2D?[] _bg = new Texture2D?[6]; // phaseごとの背景。0/1は同じタイムライン背景を共有
    private int _bgPhase;
    private int _bgPrevPhase;
    private double _bgFadeT = BgFadeSec; // _tとは独立した背景クロスフェード用タイマー
    private double _t;
    private int _phase;   // 0:来ない 1:全員知人 2:PW 3:解錠・4行 4:DM・END 5:スタッフロール
    private bool _zHeld;
    private bool _lrHeld;
    private readonly RetryHold _retry = new(); // R/Start 長押しで最初から/タイトルへ（即発の誤爆防止）
    private int _line;
    private double _lineT;
    private double _reveal;        // タイプライター表示済み文字数（＝現在ページ内）
    private GameManager? _game;    // 文字送り速度（MsgCharsPerSec）を本編設定と共有

    // テキストボックスは2行固定。2行超の行はページに割り、送り（Z）で続きを読ませる（本文は削らない）。
    //   会話フェーズ（phase 0/1 intro・phase 4 outro）だけが対象。折り返しは DrawLineBox と一致させる。
    private const float BoxWrapW = W - 56f;    // DrawLineBox の本文折り返し幅と一致
    private readonly System.Collections.Generic.List<string> _pages = new();
    private int _page;
    private int _pagedKey = -1;                // _pages を構築済みの行キー（phase×1000+line）
    private string CurPage => _pages.Count > 0 ? _pages[Mathf.Min(_page, _pages.Count - 1)] : "";
    private bool LastPage => _pages.Count == 0 || _page >= _pages.Count - 1;
    // 現在行のページを（未構築なら）作る。boot ログ行は Mono・それ以外は Zen で折り返す（描画と一致）。
    private void EnsurePages()
    {
        string? t = CurLineText();
        if (t == null) { _pages.Clear(); _page = 0; _pagedKey = -1; return; }
        int key = _phase * 1000 + _line;
        if (_pagedKey == key) return;
        _pagedKey = key; _page = 0;
        bool boot = t.StartsWith(">");
        _pages.Clear();
        _pages.AddRange(UiKit.Paginate(boot ? UiKit.Mono : _font, t, UiKit.CutBody, BoxWrapW, Hud.DlgMaxLines));
    }
    private void NextPage() { _page++; _reveal = 0; _lineT = 0; }

    // 既読スキップ（#22）：Ctrl/RB 長押しで「既読の行だけ」高速送り（本編HUDと同じ作法・独自レンダラ側の実装）。
    // PW選択(2)・縦読み(3)・スタッフロール(5)は対象外（CurLineText が null＝会話行フェーズのみ効く）。
    private int _readKey = -1;     // 既読チェック済みの行キー（phase×1000+line。フェーズ跨ぎの index 重複を区別）
    private bool _lineWasRead;     // 現在行が「表示開始時点で」既読だったか
    private bool _ffNow;           // いま高速送り中か（▶▶表示用）

    // 配色は UiKit のカットシーントークンへ集約（3画面で同値のコピーだったものを参照に置換）。
    private static readonly Color Cool = UiKit.CutMina;   // ミナ
    private static readonly Color Ink  = UiKit.CutInk;
    private static readonly Color Code = UiKit.CutCode;   // コード緑（Prologue bootログと同値＝視覚照応）

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
        var timelineBg = ResourceLoader.Load<Texture2D>("res://char/bg/epilogue/bg_ep_timeline.png");
        _bg[0] = timelineBg;
        _bg[1] = timelineBg;
        _bg[2] = ResourceLoader.Load<Texture2D>("res://char/bg/epilogue/bg_ep_lock.png");
        _bg[3] = ResourceLoader.Load<Texture2D>("res://char/bg/epilogue/bg_ep_acrostic.png");
        _bg[4] = ResourceLoader.Load<Texture2D>("res://char/bg/epilogue/bg_ep_dm.png");
        _bg[5] = ResourceLoader.Load<Texture2D>("res://char/bg/epilogue/bg_ep_roll.png");
        _bgPhase = _phase;
        _bgPrevPhase = _phase;
        _bgFadeT = BgFadeSec;
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
        O("地", "雨の交差点。言いかけた唇。鳴り響いた、クラクション。");
        O("地", "——あの子を突き飛ばしたのが誰だったか、わたくしは、もう、聞くまでもありませんでした。");
        O("地", "——ええ。わたくしは、あなたの声で、できていますから。");              // ③の核：少年は自分の声でミナを作った（だから“ぼくの声じゃだめ”だった）
        // ここから S2 追補（正典 v3）＝死の「時期」の回収。死因（交差点）→時期（最初から）の順で二段落とし。
        // ミナが自分の最初の記憶＝起動記録を開く。Prologue bootログの2行をそのまま再掲（2周目はここで全部が繋がる：
        // 0414／replay／Stay不在／声の掠れ・光の薄さ）。感情は言わせず、ミナの短い軽口だけで受ける（show-don't-tell）。
        O("地", "それから、わたくしは、自分の最初の記憶を開きました。——目覚めた日の、起動記録です。");
        O("UI", "> read operator.vitals ... [signal lost 0414]");
        O("UI", "> fallback: replay operator from archive ... OK");
        O("地", "……最初から、書いてあったのです。わたくしが目覚めるより先に、あの人がいなくなっていたことも。それからずっと、わたくしに応えてくれていたのが、遺された声だったことも。");
        O("ミナ", "……アホですね。そんな大事なこと、機械にだけ、打ち明けて。");        // 落涙立ち絵の行。感情語を言わず軽口で受ける
        // シェイクスピア引用3回目（正典が名指しする泣き所）：レイ・あかり・こはるへ説いてきた言葉を、ミナがここで初めて回収する。
        // 日本語訳は付けない（show don't tell）。「人には言えたのに、自分には言えなかった」の皮肉だけをミナの一言で示す。
        O("地", "あの人は、いつも、他人にばかり言っていました。\"To thine own self be true.\"");
        O("ミナ", "……ご主人様は、それを、一度でも、ご自分に、言えたことが、ありましたか。");
        O("地", "わたくしは今日も、タイムラインの前にいます。");
        O("ミナ", "……今日は、晴れているそうです。どなたかの、空の写真で。");    // 小さな願い（外の世界）の代償：叶ってはいない。今も他人の投稿越しにしか空を知らない（show don't tell）
        O("UI", "ミナ →（DM）：「ちゃんと食べていますか?」");
        O("ミナ", "——ええ、ご主人様。わたくしは、どこにも行きませんよ。");
    }

    public override void _Process(double delta)
    {
        _t += delta;
        _lineT += delta;
        // 会話送り／各フェーズの決定：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも進める共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
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

        // 現在行を2行ページに割り、タイプライターは現在ページ内を進める（語り/会話の行フェーズだけ）。
        string? curT = CurLineText();
        EnsurePages();
        int pageLen = curT != null ? CurPage.Length : 0;
        if (curT != null && _reveal < pageLen)
            _reveal = Mathf.Min(pageLen, (float)(_reveal + delta * (_game?.MsgCharsPerSec ?? 48f)));

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
                    if (curT != null && _reveal < pageLen) { _reveal = pageLen; } // 1回目で現在ページ全文（早送り）
                    else if (!LastPage) { NextPage(); }                          // 後続ページがあれば続きへ
                    else
                    {
                        _lineT = 0; _reveal = 0; _line++; _page = 0; _pagedKey = -1;
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
                    if (curT != null && _reveal < pageLen) { _reveal = pageLen; } // 1回目で現在ページ全文（早送り）
                    else if (!LastPage) { NextPage(); }                          // 後続ページがあれば続きへ
                    else
                    {
                        _lineT = 0; _reveal = 0; _page = 0; _pagedKey = -1;
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
        UpdateBackgroundFade(delta);
        QueueRedraw();
    }

    private void UpdateBackgroundFade(double delta)
    {
        if (_bgPhase != _phase)
        {
            Texture2D? oldBg = BackgroundForPhase(_bgPhase);
            Texture2D? newBg = BackgroundForPhase(_phase);
            _bgPrevPhase = _bgPhase;
            _bgPhase = _phase;
            _bgFadeT = oldBg == newBg ? BgFadeSec : 0.0;
        }

        if (_bgFadeT < BgFadeSec)
        {
            _bgFadeT += delta;
            if (_bgFadeT > BgFadeSec) _bgFadeT = BgFadeSec;
        }
    }

    private Texture2D? BackgroundForPhase(int phase)
    {
        return phase >= 0 && phase < _bg.Length ? _bg[phase] : null;
    }

    private void DrawEpilogueBackground()
    {
        Rect2 rect = new Rect2(0, 0, W, H);
        float fade = Mathf.Clamp((float)(_bgFadeT / BgFadeSec), 0f, 1f);
        Texture2D? prev = BackgroundForPhase(_bgPrevPhase);
        Texture2D? current = BackgroundForPhase(_bgPhase);

        if (prev == current) prev = null;
        if (fade < 1f && prev != null)
            DrawTextureRect(prev, rect, false, new Color(1f, 1f, 1f, BgAlpha * (1f - fade)));
        if (current != null)
            DrawTextureRect(current, rect, false, new Color(1f, 1f, 1f, BgAlpha * fade));
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, W, H), new Color(0.03f, 0.04f, 0.07f));
        DrawEpilogueBackground();

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

    // 背景に直乗せする文字（PW／縦読み／スタッフロール）用のドロップシャドウ付き DrawString。
    //   ボックスの下敷きが無い画面では背景の明部に本文が溶けるため、(0.5,0.5) の黒を先に敷いて浮かせる。
    private void Shadowed(Font f, Vector2 pos, string s, HorizontalAlignment al, float w, int size, Color c)
    {
        DrawString(f, pos + new Vector2(0.5f, 0.5f), s, al, w, size, new Color(0f, 0f, 0f, 0.55f * c.A));
        DrawString(f, pos, s, al, w, size, c);
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
            Color c = head ? Cool with { A = 0.9f }
                    : post ? UiKit.CutInk with { A = 0.95f }
                    : Ink;
            int sz = line == "stay." ? UiKit.CutClimax : UiKit.CutBody;
            Shadowed(_font, new Vector2(0, y), line, HorizontalAlignment.Center, W, sz, c);
        }
        if (((int)(_t * 1.5f) % 2) == 0)
            Shadowed(_font, new Vector2(0, H - 10), "Z：タイトルへ", HorizontalAlignment.Center, W, UiKit.CutNote,
                UiKit.CutInk2 with { A = 0.7f });
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
        Shadowed(_font, new Vector2(0, 46f), "── 鍵のかかったアカウント ──", HorizontalAlignment.Center, W, UiKit.CutBody,
            Cool with { A = 0.9f });
        Shadowed(_font, new Vector2(0, 72f), "パスワードを入力してください", HorizontalAlignment.Center, W, UiKit.CutBody, Ink);

        // 入力フィールドの箱（候補＝実際に打ち込む文字列であることを一目で示す）。
        var field = new Rect2(W / 2f - 70f, 96f, 140f, 26f);
        UiKit.Box(this, field, new Color(0.05f, 0.04f, 0.09f, 0.9f), 5f, UiKit.CutAccent with { A = 0.45f }, 1f);
        // 選択中の候補（打ち込む単語＝端末に打つ文字なので等幅・クライマックス級に少し大きく残す）
        string cur = "＞ " + PwChoices[_pwSel];
        Shadowed(UiKit.Mono, new Vector2(field.Position.X, 114f), cur, HorizontalAlignment.Center, field.Size.X,
            UiKit.CutClimax, UiKit.CutAccent);
        // 左右送りの矢印は箱の外側へ（スペース詰めの疑似矢印をやめる）
        Shadowed(_font, new Vector2(field.Position.X - 16f, 114f), "◀", HorizontalAlignment.Left, -1, UiKit.CutBody,
            UiKit.CutInk2 with { A = 0.8f });
        Shadowed(_font, new Vector2(field.End.X + 6f, 114f), "▶", HorizontalAlignment.Left, -1, UiKit.CutBody,
            UiKit.CutInk2 with { A = 0.8f });

        if (_pwRejectT > 0f)
            Shadowed(_font, new Vector2(0, 150f), _pwReject, HorizontalAlignment.Center, W, UiKit.CutBody,
                new Color(0.9f, 0.5f, 0.6f));

        if (((int)(_t * 1.5f) % 2) == 0)
            Shadowed(_font, new Vector2(0, 176f), "← → 選択   Z：決定", HorizontalAlignment.Center, W, UiKit.CutNote,
                UiKit.CutInk2 with { A = 0.85f });
    }

    private void DrawAcrostic()
    {
        if (_font == null) return;
        // 最古の投稿：ミナ誕生前の日付
        Shadowed(_font, new Vector2(0, 26f), "最古の投稿 — ミナ誕生の、ずっと前の日付", HorizontalAlignment.Center, W, UiKit.CutNote,
            UiKit.CutInk2 with { A = 0.85f });
        Shadowed(_font, new Vector2(0, 44f), "「ミナへ。こはるを頼む。」", HorizontalAlignment.Center, W, UiKit.CutBody,
            UiKit.CutAccent);

        float baseY = 60f;   // 全4行（y=60〜148）を画面縦中央に寄せる
        float appear = (float)_t;
        for (int k = 0; k < Acrostic.Length; k++)
        {
            if (appear < 0.6f + k * 0.7f) break; // 一行ずつ浮かぶ
            float y = baseY + k * 22f;
            // 頭文字を強調（M/I/N/A の縦読み＝伏線回収の核。本文より一段大きいクライマックス級で残す）
            Shadowed(_font, new Vector2(64f, y), Acrostic[k].Substring(0, 1), HorizontalAlignment.Left, -1, UiKit.CutClimax,
                UiKit.CutAccent);
            // 本文は x=84（頭文字から20px空ける＝"M / aybe" が単語の途中で切れて見えないように）
            // TrimStart：原文が "I made..." のように2文字目が空白の行でも本文の頭を他行と揃える（原文は変えない）
            Shadowed(_font, new Vector2(84f, y), Acrostic[k].Substring(1).TrimStart(), HorizontalAlignment.Left, -1, UiKit.CutBody, Ink);
        }
        if (_t >= 4.0 && ((int)(_t * 1.5f) % 2) == 0)
            Shadowed(_font, new Vector2(0, 186f), "Z：つづける", HorizontalAlignment.Center, W, UiKit.CutNote,
                UiKit.CutInk2 with { A = 0.85f });
    }

    private void DrawOutro()
    {
        if (_font == null || _line >= _outro.Count) return;
        // クライマックス：ミナの台詞行で落涙の立ち絵を差す（画をピークに集める／§8）。
        if (_outro[_line].Who == "ミナ" && _tears != null)
        {
            float a = Mathf.Clamp((float)_lineT / 0.5f, 0f, 1f);
            float ph = 116f, pw = ph * _tears.GetWidth() / Mathf.Max(1, _tears.GetHeight());
            DrawTextureRect(_tears, new Rect2(W / 2f - pw / 2f, H - 58f - ph + 6f, pw, ph), false,
                new Color(1f, 1f, 1f, a));
        }
        DrawLineBox(_outro[_line]);
        if (_line >= _outro.Count - 1)
            Shadowed(_font, new Vector2(0, 40f), "END", HorizontalAlignment.Center, W, UiKit.CutClimax,
                UiKit.CutInk with { A = 0.9f });
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
        // 画面テキスト（DM等）は浄化シアン、bootログはコード緑、語りはニュートラル、セリフはミナ色。
        Color edge = narr ? UiKit.CutNarr : (ui ? (boot ? Code : UiKit.Purify) : Cool);
        // 現在ページ（2行固定・禁則つき）。ボックスは2行分の固定高さ（行数で伸ばさない＝全ボックス統一）。
        string page = CurPage;
        var lines = UiKit.WrapLines(font, page, UiKit.CutBody, W - 56);
        float boxTop = H - 58f;   // 2行固定（下余白12px＝額縁を効かせる）
        // ボックス（Hub/Shop と同じ角丸＋話者色の額縁。UiKit.CutBox で3画面共通）
        UiKit.CutBox(this, new Rect2(14, boxTop, W - 28, H - 10f - boxTop), edge, boot ? 0.4f : 0.5f);
        string label = narr || boot ? "" : d.Who;
        if (label != "")
            DrawString(UiKit.ZenBold, new Vector2(24, boxTop + 12), label, HorizontalAlignment.Left, -1, UiKit.CutSpeaker, edge);
        var align = narr ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        // 中央寄せのナレは「中央から左右へ広がる」見え方になるタイプライターをやめ、現在ページ全文をその場でフェードイン表示。
        //   （中央寄せ＋部分文字列だと毎フレーム再センタリングされて左右に展開して見えるため）。
        // セリフ（左寄せ）は従来どおり左→右のタイプライターで送る。
        Color ink = boot ? Code : Ink;   // bootログ行はコード緑（Prologue と同値）
        int shown;
        if (narr)
        {
            shown = page.Length;                             // 現在ページ全文をその場で（広がる演出なし）
            float a = Mathf.Clamp((float)_lineT / 0.35f, 0f, 1f); // 短いフェードイン
            ink = new Color(Ink.R, Ink.G, Ink.B, a);
        }
        else
        {
            shown = Mathf.Clamp((int)_reveal, 0, page.Length); // bootログもタイプライター＝端末に流れる感を保つ
        }
        UiKit.TypewriterLines(this, font, lines, new Vector2(24, boxTop + 27f), W - 56, UiKit.CutBody, ink, shown, align);
        // 既読高速送り中の控えめな表示（ボックス右上・#22）。
        if (_ffNow)
            DrawString(UiKit.ZenBold, new Vector2(W - 42, boxTop + 12), "▶▶", HorizontalAlignment.Left, -1, UiKit.CutSpeaker,
                new Color(Cool, 0.8f));
        // 送り三角は現在ページの全文表示後だけ点滅（本編と同じ作法。後続ページも同じ▼で示す）。
        // ナレは現在ページを即表示するので、フェード完了で点滅（タイプライター完了を待たない）。
        bool ready = narr ? _lineT >= 0.35 : _reveal >= page.Length;
        if (ready && ((int)(_t * 2f) % 2) == 0)
            DrawString(_font, new Vector2(W - 26, H - 16), "▼", HorizontalAlignment.Left, -1, UiKit.CutNote,
                new Color(1f, 1f, 1f, 0.7f));
    }
}
