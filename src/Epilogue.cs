using Godot;
using System.Collections.Generic;

// Epilogue : EPILOGUE E1〜E6（案C）。台詞の正典: wiki/08_仮台本/08_粗い台本_案C_3_FINALと結末.md
// （ユーザー承認済み・2026-09-05）。E1 タイムライン（フォロー欄に三人・散った下書きの件数を報告）→
// E2 鍵アカ＝あなたの下書きフォルダ（PW＝GameManager.LastSentWord＝最後に送った言葉。旧 "stay" ゲートを置換）→
// E3 四行（消されなかった、唯一の下書き。M/I/N/A 縦読み＋命名ルート別の一言）→
// E4 開示（起動記録の再掲→白状→【初】の実文字列→開示は「同じでした」の1行だけ）→
// E5 空・DM（タイムラインの前・晴れの写真・DM「ちゃんと食べていますか?」）→
// E6 END（下書き選択「また来る／ありがとう／（送らない）」→ END）。E7 スタッフロールは現行維持（対象外）。
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

    // ── bg2 の層背景（char/bg2/epilogue）──
    //   ベランダのある部屋を、夜(L1_far_night)から暁(L1_far_dawn)へ phase 進行でクロスフェードする。
    //   夜＝E1〜E3（タイムライン・鍵アカ・四行）、暁＝E4以降（開示・空・DM・END・スタッフロール）。
    //   層は Sprite2D で敷く（Z は本文 _Draw の 0 より奥）。素材は 1280×720 なので内部解像度 384×216 へ
    //   0.3 倍で落とす＝ステージの BgLayers と同じ高さフィット。L3 の小物だけ素材座標を 0.3 倍して置く。
    //   画面中央の UI（タイムライン・鍵・四行・DM）が読めることが最優先なので、層全体に暗幕を掛けて沈める。
    //   暁は素材自体が夜より明るいので、明けるぶんだけ濃い暗幕（DawnDim < NightDim）にして
    //   本文のコントラストを一定に保つ。
    private Sprite2D? _lNight, _lDawn, _lDawnLight;
    private readonly List<Sprite2D> _layers = new();
    private bool _hasLayers;
    private float _dawnK;        // 0=夜 1=暁（phase>=DawnPhase で 1 へ smoothstep）
    private double _dawnT;
    private const int DawnPhase = 4;        // ここから暁へ（DM＝遺志の継承）
    private const double DawnFadeSec = 2.0; // 夜→暁は本文の送りより遅く（唐突に明けない）
    private const float NightDim = 0.62f;   // 夜の層に掛ける明度（本文の可読性用の暗幕）
    private const float DawnDim = 0.50f;    // 暁の層に掛ける明度（明るいぶん濃く沈める）
    private double _t;
    private int _phase;   // 0/1:E1タイムライン 2:E2 PW 3:E3 四行 4:E4開示〜E5空DM〜E6END 5:E7スタッフロール
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
    private static readonly Color Warm = UiKit.CutWarm;   // あなた（送った下書き）。案C に少年は居ない
    private static readonly Color Ink  = UiKit.CutInk;
    private static readonly Color Code = UiKit.CutCode;   // コード緑（Prologue bootログと同値＝視覚照応）

    // PW候補＝あなたの下書きフォルダに打ち込む言葉。P2（目覚め・冒頭）の3候補＋ダミー「ミナ」（【名】に関わらず固定）。
    // 正解＝GameManager.LastSentWord（最後に送った言葉＝【終】。E2 時点では F4 で送った【初】と同値。旧 "stay" ゲートを置換）。
    private static readonly string[] PwChoices =
    {
        "おはよう", "きこえてる", "うごいた", "ミナ",
    };
    private int _pwAnswer;   // _Ready で LastSentWord に一致する候補の添字を確定（未一致は "きこえてる" ＝Final.cs の既定語）
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
    private string _nameReply = "";   // E3 末尾の一言（【名】効・命名ルート別。_Ready で確定）

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

    private struct DLine { public string Who; public string Text; }   // Who: "地"=ミナ語り / "ミナ" / "UI" / "あなた"
    private readonly List<DLine> _intro = new();   // phase0+1（E1 タイムライン）
    private readonly List<DLine> _outro = new();   // phase4（E4 開示 → E5 空・DM → E6 END）

    // E6 末尾の下書き選択（また来る／ありがとう／（送らない））。Prologue の下書き選択と同じ「差し込み点」方式：
    // _line がここに達したら ChoiceOverlay を出し、決まったら「あなたの1行＋分岐の受け」を _outro へ挿し込む。
    private ChoiceOverlay? _choice;
    private double _choiceT;                 // 提示からの経過＝迷い秒数（【迷】。P2 の実測秒数と比較する）
    private int _outroChoiceLine = -1;       // 差し込み点（_outro 構築時に確定）
    private static readonly string[] EndChoices = { "また来る", "ありがとう", "（送らない）" };

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
        // bg2 の層を敷けたら旧 _bg[] の描画は止める（旧素材は消さずそのまま残す＝層が読めなければ従来通り）。
        BuildLayers();
        _bgPhase = _phase;
        _bgPrevPhase = _phase;
        _bgFadeT = BgFadeSec;
        _game = GetNodeOrNull<GameManager>("/root/Game");

        // PW正解の確定：GameManager.LastSentWord（最後に送った言葉）と一致する候補を探す。
        // F4（Final.cs）は【初】を送った瞬間に LastSentWord を同値へ更新するので、E2 時点では常に一致する。
        // 未一致（旧セーブ等）は Final.cs の既定語「きこえてる」へフォールバック。
        _pwAnswer = System.Array.IndexOf(PwChoices, _game?.LastSentWord ?? "");
        if (_pwAnswer < 0) _pwAnswer = System.Array.IndexOf(PwChoices, "きこえてる");
        if (_pwAnswer < 0) _pwAnswer = 1;

        // E3 末尾の一言（【名】効・命名ルート別）。四行の acrostic 表示のあとに DrawAcrostic 側で出す。
        _nameReply = (_game?.NameRoute ?? 0) switch
        {
            0 => "響きで選んだと、思っていたでしょう。……ええ。わたくしも、です。",                              // 【名】ミナ（直接命名）
            1 => "却下して、正解でした。わたくしの名前は、最初から、こちらに書いてあったので。",                  // 【名】ダサい名前（却下→自称）
            _ => "自分で名乗った名前でした。……最初から、ここに、書いてあったのに。",                            // 【名】（送らない）
        };

        // ── E1 タイムライン：フォロー欄に三人・散った下書きの件数を報告 → E2 の鍵アカへ ──
        void I(string who, string t) => _intro.Add(new DLine { Who = who, Text = t });
        I("地", "次の日も、タイムラインは、流れていました。");
        I("地", "フォロワー欄の、いちばん上に、三つ。知っている名前が、並んでいました。");
        I("ミナ", "……知らない人、では、なくなったようです。");
        int scattered = _game?.ScatteredWords.Count ?? 7;
        I("ミナ", $"ところで、ご主人様。この旅で、あなたが選ばなかった下書き——{scattered}件。");
        I("ミナ", "ぜんぶ、拾ってあります。……わたくしは、そういう生き物ですので。");
        I("ミナ", "ご安心を。誰にも、見せていません。——あなたのフォルダに、戻してあります。鍵ごと。");

        // ── E4 開示 → E5 空・DM → E6 END（下書き選択は _outroChoiceLine で差し込む）──
        void O(string who, string t) => _outro.Add(new DLine { Who = who, Text = t });
        // E4：起動記録の再掲（Prologue P1 boot ログ "> import unsent_drafts ... 414 items ... OK" と同一）→
        //   白状 →【初】の実文字列を開く → 開示は「同じでした」の1行だけ（台詞で件数には触れない）。
        O("地", "それから、わたくしは、自分の最初の記憶を開きました。——目覚めた日の、起動記録です。");
        O("UI", "> import unsent_drafts ... 414 items ... OK");
        O("ミナ", "……ご主人様。ひとつだけ、白状します。");
        string firstWord = string.IsNullOrEmpty(_game?.FirstScattered) ? "きこえてる" : _game.FirstScattered; // Final.cs の【初】既定語と同一
        O("UI", $"「{firstWord}」");
        O("ミナ", "わたくしの、いちばん最初の一件と——同じでした。");
        O("ミナ", "……ええ。あの日から、ひとつも、消していません。わたくしが、覚えている係ですので。");
        // E5：空・DM（三度目の空の問いの答え → DM。返事は求めない）。
        O("地", "わたくしは今日も、タイムラインの前にいます。");
        O("ミナ", "……今日は、晴れているそうです。どなたかの、空の写真で。");
        O("UI", "ミナ →（DM）：「ちゃんと食べていますか?」");
        O("ミナ", "——既読、確認。……ふふ。");
        // E6：END。ここから先（下書き選択とその受け）は DriveEndChoice が実プレイの選択結果を見て積む。
        O("ミナ", "ご主人様。本日の業務は、以上です。");
        _outroChoiceLine = _outro.Count;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        _lineT += delta;
        // 会話送り／各フェーズの決定：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも進める共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        bool zEdge = z && !_zHeld;
        _zHeld = z;

        // R / Start 長押し(0.45s)：スタッフロール(phase5)では「タイトルへ」、それ以前は最初から(Prologue)
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
                    if (_pwSel == _pwAnswer) { _unlocked = true; _phase = 3; _t = 0; _line = 0; }
                    else { _pwReject = "……違います。最後に送ったのは、それでは、ありません。"; _pwRejectT = 2.0; }
                }
                break;
            case 3: // 解錠：4行英文を順に見せ、Zで phase4 へ
                if (_t >= 4.0 && zEdge) { _phase = 4; _t = 0; _line = 0; _lineT = 0; _reveal = 0; }
                break;
            case 4: // E4開示→E5空・DM→E6END
                // 差し込み点（下書き選択「また来る／ありがとう／（送らない）」）に達したら会話を止めて提示する。
                if (_line == _outroChoiceLine) { DriveEndChoice(delta); break; }
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
        UpdateLayers(delta);
        QueueRedraw();
    }

    // E6 末尾の下書き選択（また来る／ありがとう／（送らない））。Prologue の下書き選択・Final の頂点選択と
    // 同じ「差し込み点」方式：_outroChoiceLine に達するたびに毎フレーム呼ばれる。
    //   ・未提示なら ChoiceOverlay を出す（並びは台本どおり＝また来る／ありがとう／（送らない））。
    //   ・決まったら「あなたの1行（（送らない）は無し）＋分岐の受け→いってらっしゃいませ→END」を _outro へ積み、
    //     以後は通常の会話送りへ戻す（_outroChoiceLine を -1 にして二重発火を防ぐ）。
    private void DriveEndChoice(double delta)
    {
        if (_choice == null)
        {
            _choice = ChoiceOverlay.Show(this, EndChoices, defaultSel: 0);
            _choiceT = 0;
            return;
        }
        _choiceT += delta;
        if (!_choice.Decided) return;
        int sel = _choice.Selected;
        _choice.QueueFree();
        _choice = null;

        float hesitation = (float)_choiceT;   // 【迷】＝この選択に掛けた秒数
        var others = new List<string>();
        for (int i = 0; i < EndChoices.Length; i++) if (i != sel) others.Add(EndChoices[i]);
        // （送らない）は言葉ではないので【散】に数えない＝選ぶと表示候補（上2つ）が全部散る（P3 と同じ作法）。
        string chosen = sel == EndChoices.Length - 1 ? "" : EndChoices[sel];
        _game?.RecordChoice("epilogue_end", chosen, others, hesitation);   // 【終】更新（（送らない）は空文字なので更新されず F4 の値のまま）

        if (chosen != "") _outro.Add(new DLine { Who = "あなた", Text = chosen });

        // 対句：P2 の実測秒数（GameManager.P2HesitationSec）とこの選択の秒数を比較する。
        float p2 = _game?.P2HesitationSec ?? 0f;
        string reply = sel == EndChoices.Length - 1
            ? "……無言。ふふ。それも、集計に入れておきます。"
            : hesitation < p2
                ? $"……ええ。いまの、{Mathf.Max(1, Mathf.RoundToInt(p2))}秒も、かかりませんでしたね。"
                : "……今日は、長かったですね。……ええ。集計だけ、しています。";
        _outro.Add(new DLine { Who = "ミナ", Text = reply });
        _outro.Add(new DLine { Who = "ミナ", Text = "いってらっしゃいませ、ご主人様。" });
        _outro.Add(new DLine { Who = "ミナ", Text = "——ええ、ご主人様。わたくしは、どこにも行きませんよ。" }); // END（画面上部に END）

        _outroChoiceLine = -1;                                   // 差し込み済み＝以後は二重発火しない
        _lineT = 0; _reveal = 0; _page = 0; _pagedKey = -1;      // _line はそのまま＝いま積んだ最初の行を指す
    }

    // bg2 の層を敷く（奥→手前に 夜/暁の遠景 → 中景 → 近景の小物2つ → 光）。
    //   夜と暁の遠景は重ねて置き、αのたすき掛けでクロスフェードする（UpdateLayers）。
    //   暁の光(L4_light_dawn)は加算で、明けるぶんだけ足す。スマホの光(L4_light_phone)は常時。
    //   遠景が読めなければ何も敷かず _hasLayers=false のまま＝旧 _bg[] の1枚絵経路がそのまま動く。
    private void BuildLayers()
    {
        const string dir = "res://char/bg2/epilogue/";
        if (!ResourceLoader.Exists(dir + "L1_far_night.png")) return;
        const float s = H / 720f;   // 216/720 = 0.3（BgLayers と同じ高さフィット）

        // 素材から Sprite2D を1枚作って足す。offset は素材座標(1280×720基準)。読めなければ null を返す。
        Sprite2D? Add(string file, int z, Vector2 offset, bool additive = false, float alpha = 1f)
        {
            string path = dir + file;
            if (!ResourceLoader.Exists(path)) return null;
            var tex = ResourceLoader.Load<Texture2D>(path);
            if (tex == null || tex.GetHeight() <= 0) return null;
            var spr = new Sprite2D
            {
                Name = file.Replace(".png", ""), Texture = tex, Centered = false,
                Scale = new Vector2(s, s), Position = offset * s,
                ZIndex = z, ZAsRelative = false,
                Modulate = new Color(1f, 1f, 1f, alpha),
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            };
            if (additive) spr.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            AddChild(spr);
            _layers.Add(spr);
            return spr;
        }

        _lNight = Add("L1_far_night.png", -95, Vector2.Zero);
        if (_lNight == null) return;
        _lDawn = Add("L1_far_dawn.png", -94, Vector2.Zero, alpha: 0f);   // 暁は α0 で重ねて置く
        Add("L2_mid.png", -92, Vector2.Zero);
        Add("L3_near_left.png", -91, new Vector2(24f, 518f));
        Add("L3_near_right.png", -91, new Vector2(1026f, 604f));
        Add("L4_light_phone.png", -88, Vector2.Zero, additive: true);
        _lDawnLight = Add("L4_light_dawn.png", -88, Vector2.Zero, additive: true, alpha: 0f);
        _hasLayers = true;
        ApplyLayerTint();
    }

    // 夜→暁の進行を回す。phase が DawnPhase 以上になったら DawnFadeSec かけて明ける。
    private void UpdateLayers(double delta)
    {
        if (!_hasLayers) return;
        double target = _phase >= DawnPhase ? 1.0 : 0.0;
        if (Mathf.IsEqualApprox(_dawnT, target)) return;
        _dawnT = Mathf.Clamp(_dawnT + delta * (target > _dawnT ? 1.0 : -1.0) / DawnFadeSec, 0.0, 1.0);
        float k = (float)_dawnT;
        _dawnK = k * k * (3f - 2f * k);   // smoothstep（唐突に明けない）
        ApplyLayerTint();
    }

    // 夜/暁のα と、本文の可読性を保つ暗幕（NightDim→DawnDim）を各層へ反映する。
    private void ApplyLayerTint()
    {
        // 暁は素材自体が明るいので、明けるほど濃い暗幕を掛けて中央の文字のコントラストを保つ。
        float dim = Mathf.Lerp(NightDim, DawnDim, _dawnK);
        foreach (var l in _layers)
        {
            float a = 1f;
            if (l == _lDawn || l == _lDawnLight) a = _dawnK;
            else if (l == _lNight) a = 1f - _dawnK;
            // 加算層は α が合成に効かないので、暗幕は RGB 側で掛けて沈める。
            l.Modulate = new Color(dim, dim, dim, a);
        }
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
        if (_hasLayers) return;   // bg2 の層を敷いている＝旧 _bg[] の1枚絵は描かない（旧素材は残してある）
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
        // 下敷きの黒は層が無いときだけ（層があると全画面の不透明矩形が層を隠す）。
        if (!_hasLayers) DrawRect(new Rect2(0, 0, W, H), new Color(0.03f, 0.04f, 0.07f));
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
        Shadowed(_font, new Vector2(0, 34f), "── 鍵のかかった下書きフォルダ ──", HorizontalAlignment.Center, W, UiKit.CutBody,
            Cool with { A = 0.9f });
        // 鍵をかけたのはミナ自身なので伝聞にしない（固定投稿「傘」の引用は削除）。
        Shadowed(_font, new Vector2(0, 54f), "……開けるには、言葉が要ります。——あなたの、言葉が。", HorizontalAlignment.Center, W, UiKit.CutBody,
            Cool with { A = 0.9f });
        Shadowed(_font, new Vector2(0, 76f), "パスワードを入力してください", HorizontalAlignment.Center, W, UiKit.CutBody, Ink);

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
        // 消されなかった、唯一の下書き（「ミナへ。こはるを頼む。」の引用は削除＝正体は言わない）。
        Shadowed(_font, new Vector2(0, 26f), "最古の下書き — 消されなかった、唯一の一件", HorizontalAlignment.Center, W, UiKit.CutNote,
            UiKit.CutInk2 with { A = 0.85f });

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
        // 四行がすべて浮かんだあとの一言（【名】効・命名ルート別。_Ready で確定した _nameReply）。
        if (appear >= 0.6f + Acrostic.Length * 0.7f + 0.6f)
            Shadowed(_font, new Vector2(0, 158f), _nameReply, HorizontalAlignment.Center, W, UiKit.CutBody, Cool);
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

    // 下部の語り／会話ボックス。Who: "地"=ミナ語り / "ミナ"=ミナ / "UI"=画面テキスト / "あなた"=送った下書き。
    private void DrawLineBox(DLine d)
    {
        bool ui = d.Who == "UI";
        bool narr = d.Who == "地";        // ミナの語り＝話者名なし・中央寄せでセリフと区別
        bool you = d.Who == "あなた";      // E6 の下書き選択で送った言葉（案C に少年は居ない）
        // S3: 起動記録（bootログ）の再掲行（"> " 始まり）は Prologue と同じ等幅フォント＋コード緑で出す。
        //   「最初の記憶＝機械の生ログ」であることを、言葉でなく書体と色で Prologue に照応させる。
        //   話者ラベルも出さない（コンソール行に話者はいない）。
        bool boot = ui && d.Text.StartsWith(">");
        var font = boot ? UiKit.Mono : _font;
        // 画面テキスト（DM等）は浄化シアン、bootログはコード緑、語りはニュートラル、あなたは暖色、セリフはミナ色。
        Color edge = narr ? UiKit.CutNarr : (ui ? (boot ? Code : UiKit.Purify) : (you ? Warm : Cool));
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
