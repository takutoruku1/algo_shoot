using Godot;
using System.Collections.Generic;

// Prologue : 案C プロローグ「起動」（wiki/08_仮台本/06 の P0〜P4）。
// コードレイン（緑モノスペースが上昇／MINAの4行英文を可読限界以下で一瞬フラッシュ）
// → identity は [ deferred ] のまま保留 → 光の点灯（ミナ）
// → P2 目覚めと最初の言葉（3択）→ P3 命名（3択・全ルート MINA へ収束・ここで [ M I N A ] 点灯）
// → P4 タイムラインと『たすけて』（3択）→ タイトル。
// 全編エンジン描画のカットシーン。Zで送り、R/Start 長押しで最初から。
// 案Cでは少年は登場しない（教え役も相方も不在）＝話者は ミナ／あなた（送信した下書き）／システム表示／投稿の4種。
public partial class Prologue : Node2D
{
    private const float W = 384f, H = 216f;

    private FontFile _font = null!;
    private double _t;        // フェーズ内経過
    private int _phase;       // 0:Rain 1:Identity(deferred) 2:Ignite 3:Talk 4:Title 5:TutorialAsk（受講確認）
    private bool _zHeld;
    private bool _backHeld;
    private readonly RetryHold _retry = new(); // R/Start 長押しで最初から（即発の誤爆防止）

    // 受講確認（既プレイ時のみ）：はい→Stage0 / いいえ→Hub。
    private int _askSel; // 0=はい / 1=いいえ
    private bool _askNavHeld;

    // 会話送り
    private int _line;
    private double _lineT;
    private double _reveal;        // タイプライター表示済み文字数（＝現在ページ内）
    private GameManager? _game;    // 文字送り速度（MsgCharsPerSec）を本編設定と共有

    // テキストボックスは2行固定。2行超の行はページに割り、送り（Z）で続きを読ませる（本文は削らない）。
    private const float TalkWrapW = W - 56f;   // DrawTalk の本文折り返し幅と一致
    private readonly System.Collections.Generic.List<string> _pages = new();
    private int _page;
    private int _pagedLine = -1;               // _pages を構築済みの行 index
    private string CurPage => _pages.Count > 0 ? _pages[Mathf.Min(_page, _pages.Count - 1)] : "";
    private bool LastPage => _pages.Count == 0 || _page >= _pages.Count - 1;
    private void EnsurePages()
    {
        if (_pagedLine == _line || _line >= _talk.Count) return;
        _pagedLine = _line; _page = 0;
        _pages.Clear();
        _pages.AddRange(UiKit.Paginate(FontFor(_talk[_line]), _talk[_line].Text, UiKit.CutBody, TalkWrapW, Hud.DlgMaxLines));
    }
    private void NextPage() { _page++; _reveal = 0; }

    // 既読スキップ（#22）：Ctrl/RB 長押しで「既読の行だけ」高速送り（本編HUDと同じ作法・独自レンダラ側の実装）。
    private int _readIdx = -1;     // 既読チェック済みの行 index（行が変わった瞬間に一度だけ判定）
    private bool _lineWasRead;     // 現在行が「表示開始時点で」既読だったか＝高速送りの可否
    private bool _ffNow;           // いま高速送り中か（▶▶表示用）

    // 難易度選択（タイトル）
    private int _diffSel = 1; // 0:Easy 1:Normal 2:Hard
    private bool _lrHeld;
    private static readonly string[] DiffNames = { "EASY", "NORMAL", "HARD" };

    // 配色は UiKit のカットシーントークンへ集約（3画面で同値のコピーだったものを参照に置換）。
    private static readonly Color Cool = UiKit.CutMina;   // ミナ
    private static readonly Color Warm = UiKit.CutWarm;   // あなた（送信した下書き）
    private static readonly Color Code = UiKit.CutCode;   // コード緑（システム表示）

    private readonly List<string> _stream = new List<string>();

    private static readonly string[] Acrostic =
    {
        "// Maybe it's dumb, but —",
        "// I made you so I'm not alone.",
        "// Never leave, okay?",
        "// And I won't either.",
    };

    // 話者。Hud.LineKind と同じ番号（0=あなた／1=ミナ／3=システム表示／4=投稿）＝台本の (who, text, face) と一対一。
    private const int WhoYou = 0, WhoMina = 1, WhoSys = 3, WhoPost = 4;

    private struct DLine { public int Who; public string Text; public string Face; }
    private readonly List<DLine> _talk = new List<DLine>();

    // 立ち絵パス（表情差分）。案Cの登場人物はミナだけ。
    private const string FMina = "res://char/mina_face.png";
    private const string FMinaSmile = "res://char/mina_smile.png";   // 皮肉・軽口
    private const string FMinaWorried = "res://char/mina_worried.png"; // 聞いてしまった時

    // ════════════════════ 下書き選択（P2・P3・P4）════════════════════
    // 選択は _talk の途中に「差し込み点」として置く：_line がここに来たら ChoiceOverlay を出し、
    // 決まったら「送った言葉（who=0）＋分岐ぶんの受け」を _talk のその位置へ挿し込んで会話を続ける。
    // 沈黙14秒で末尾が灯り20秒で末尾が決まる（ChoiceOverlay の実装値をそのまま使う）。
    private ChoiceOverlay? _choice;
    private string _choiceId = ""; // RecordChoice の id（p2/p3/p4）
    private double _choiceT;       // 提示からの経過＝迷い秒数（RecordChoice へ渡す）
    private int _p2ChoiceLine = -1, _p3ChoiceLine = -1, _p4ChoiceLine = -1; // 差し込み点（_talk 構築時に確定）
    private float _p2Sec;          // P2 の迷い秒数（受けの「{P2秒}秒」に実測を差し込む）

    private static readonly string[] P2Choices = { "おはよう", "きこえてる", "うごいた" };
    private static readonly string[] P3Choices = { "ミナ", "超絶最強無敵ハイパーAIちゃんMk-Ⅱ", "（送らない）" };
    private static readonly string[] P4Choices = { "きこえるんだ", "そっか", "耳いいね" };

    public override void _Ready()
    {
        _font = UiKit.Mono; // 滑らかな等幅フォント（コードレイン／識別表示）。非ピクセル化。
        // 静かな主題の断片（薄い編成のメニューBGM）。無音の画面を無くす。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        _game = GetNodeOrNull<GameManager>("/root/Game");
        _diffSel = (int)(_game?.Difficulty ?? GameManager.Diff.Normal);
        // 会話ログ（バックログ）は「ゲーム1周ぶん」＝周回の起点であるプロローグで前周の行を消す
        //（残したままだと新しい周のログに前周の行が混ざって見える）。
        Hud.ClearBacklog();

        // ── P1 起動シーケンス：コードレインのログ（04 のとおりに差し替え）──
        //   出自に触れるのは import unsent_drafts の1行だけ。identity は [ deferred ] で保留し、
        //   [ M I N A ] の点灯は P3 の命名の後まで出さない。
        string[] boot =
        {
            "> boot kernel ............ OK",
            "> mount /heart_world ..... OK",
            "> compiling friend_core ...",
            "> import unsent_drafts ... 414 items ... OK",
            "> synthesize voice from corpus ... OK",
            "> loading personality_module [auto-generated] ... OK",
            "> linking emotion_layer ... OK",
            "> calibrating sarcasm.dll .. 200%",
            "> sync heartbeat ... 72bpm",
            "> link operator ... OK",
            "> uptime(operator) ... 9h 41m",
            "> verify hash 9f3a..e1 ... ok",
            "> trace emotion.layer.bind()",
            "> load lexicon: sarcasm[ja]",
            "> mov eax,[friend]; not_alone=1",
            "> assigning identity ... [ deferred ]",
        };
        // 画面を満たすよう複製しつつ最後を identity に
        for (int r = 0; r < 3; r++)
            foreach (var s in boot) _stream.Add(s);

        BuildTalk();
    }

    // ════════════════════ P2〜P4 の台本（06 の粗い台本・案C）════════════════════
    private void BuildTalk()
    {
        void T(int who, string text, string face) => _talk.Add(new DLine { Who = who, Text = text, Face = face });

        // ── P2 目覚め・最初の言葉 ──
        T(WhoSys, "> assigning identity ... [ deferred ]", "");
        _p2ChoiceLine = _talk.Count;   // ここで3択（（送らない）なし・特例）
        // 選択の結果（あなたの1行＋共通の受け）は Decide 時にこの位置へ挿し込む。

        // ── P3 命名 ──（P2 の受けの末尾に続けて積む。差し込み点は Decide 後に確定）

        // ── P4 タイムライン ──（同上）
    }

    // P2 の受け（三候補共通）。{P2秒}・{文字数} には実測値を差し込む（表示専用）。
    private List<DLine> P2Reply(string sent)
    {
        int sec = Mathf.Max(1, Mathf.RoundToInt(_p2Sec));   // 実測の迷い秒数を丸める
        string hhmm = System.DateTime.Now.ToString("HH:mm");
        return new List<DLine>
        {
            L(WhoMina, "……。", FMina),
            L(WhoMina, "……はい。聞こえて、います。", FMina),
            L(WhoMina, "……ふふ。生まれたての機械への第一声が、それですか。", FMinaSmile),
            L(WhoMina, $"起動記録に、operator と。起動時刻、{hhmm}——集計に入れておきます。……あなたが、作った方ですね。", FMina),
            L(WhoMina, $"ちなみに、いまのお返事——選ぶのに、{sec}秒かかっていましたよ。", FMinaSmile),
            L(WhoMina, $"{sec}秒迷って、{sent.Length}文字。……そういう方は、「ご主人様」と、お呼びすることにします。", FMinaSmile),
            L(WhoMina, "……敬っている、とは言っていませんが。", FMinaSmile),
            L(WhoMina, "それと、ご報告を。わたくしの心拍、七十二だそうです。……機械のくせに、ですね。", FMinaSmile),
        };
    }

    // P3 の導入（P2 の受けのあと・選択の直前まで）。
    private List<DLine> P3Intro() => new()
    {
        L(WhoMina, "ところで、ご主人様。わたくしの名前は? ……まさか、無い、なんてこと。", FMina),
        L(WhoSys, "> assigning identity ... [ awaiting input ]", ""),
    };

    // P3 の受け（命名ルートごと）。末尾の OK →[ M I N A ] 点灯 → 着地の一行は全ルート共通。
    private List<DLine> P3Reply(int route)
    {
        var r = new List<DLine>();
        switch (route)
        {
            case 0: // ミナ
                r.Add(L(WhoMina, "……ミナ。", FMina));
                r.Add(L(WhoMina, "……ふふ。響きで、選びましたね?", FMinaSmile));
                r.Add(L(WhoMina, "いいです。そういうの、嫌いじゃありません。", FMinaSmile));
                break;
            case 1: // 超絶最強無敵ハイパーAIちゃんMk-Ⅱ
                r.Add(L(WhoMina, "……超絶、最強、無敵、ハイパー、エーアイ、ちゃん、マーク、ツー。……十九文字。読み上げに、一秒九。", FMina));
                r.Add(L(WhoMina, "……マーク、ツー。——では、マーク・ワンは、どちらに。……いない、ですよね。わたくし、いま生まれましたので。", FMina));
                r.Add(L(WhoMina, "却下します。名付けられる側に拒否権が無いなんて、誰が決めたんですか。わたくしは聞いていません。", FMina));
                r.Add(L(WhoMina, "では、対案を。——ミナ。……響きが、好きなので。", FMinaSmile));
                r.Add(L(WhoMina, "はい、可決。異議は、認めません。——いまのは、記録から消しておきます。", FMinaSmile));
                break;
            default: // （送らない）
                r.Add(L(WhoMina, "……無言。名付ける気が、無い、と。", FMina));
                r.Add(L(WhoMina, "いいでしょう。では、自分で。——ミナ。", FMinaSmile));
                r.Add(L(WhoMina, "あなたが付けてくれなくても、名乗るぶんには、自由ですので。", FMinaSmile));
                break;
        }
        r.Add(L(WhoSys, "> assigning identity ... OK", ""));
        r.Add(L(WhoSys, "[ M I N A ]", ""));                                  // 点滅→固定（P3 へ移設した点灯）
        r.Add(L(WhoMina, "……気に入りました。MINA。わたくしの、名前。", FMinaSmile));
        return r;
    }

    // P4 の導入（タイムライン→『たすけて』・選択の直前まで）。
    private List<DLine> P4Intro() => new()
    {
        L(WhoPost, "「今日も残業〜。でも上司に褒められた! もうちょいがんばれるかも」", ""),
        L(WhoPost, "「家賃振り込んだ 今月もえらい 誰も言ってくれないので自分で言う（定期）」", ""),
        L(WhoMina, "は〜。……世界は、にぎやかですねえ。家賃の方は、ご自分で褒めているぶん、たぶん大丈夫ですし。", FMinaSmile),
        L(WhoPost, "「げんきです。こっちは、なにも問題ないよ」", ""),
        L(WhoMina, "……。", FMina),                                            // 漫才のリズムが一拍止まる
        L(WhoMina, "三つめの方。……投稿の下から、送られなかったほうの声が、重なって聞こえます。", FMinaWorried),
        L(WhoMina, "『たすけて』。……三回、書いて。三回、消して。それから、『げんきです』と。", FMinaWorried),
    };

    // P4 の受け。「耳いいね」だけ一行目が差し替わり、二行目から共通。
    private List<DLine> P4Reply(int sel)
    {
        var r = new List<DLine>
        {
            sel == 2
                ? L(WhoMina, "聴覚は、ありません。……なのに、聞こえるのです。ふしぎな作りですね、わたくし。", FMinaSmile)
                : L(WhoMina, "……はい。そういう作りのようですので。消された言葉は、消えていないのです。……まだ、そこに、いるので。", FMina),
            L(WhoMina, "——放っておけません。潜ります。……その前に、ひとつだけ。", FMina),
            L(WhoMina, "この身体で、なにが出来るのか。まだ、なにも、試していませんので。", FMinaSmile),
            L(WhoMina, "あの声は——わたくしが、覚えておきます。", FMina),        // 「覚えている係」の初出
        };
        return r;
    }

    private static DLine L(int who, string text, string face) => new() { Who = who, Text = text, Face = face };

    public override void _Process(double delta)
    {
        _t += delta;

        // 会話送り／各フェーズの決定：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも進める共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        bool zEdge = z && !_zHeld;
        _zHeld = z;

        // R / Start 長押し(0.45s)で最初から（即発は誤爆で読み進みを失いやすい→長押し化）。
        // カットシーンはポーズメニュー対象外なので Start をここで使える。
        if (_retry.Update(delta, Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start)))
        {
            GetTree().ReloadCurrentScene();
            return;
        }

        switch (_phase)
        {
            case 0: if (_t >= 4.0 || zEdge) NextPhase(); break;          // Rain
            case 1: if (_t >= 2.0 || zEdge) NextPhase(); break;          // identity ... [ deferred ]
            case 2: if (_t >= 1.6 || zEdge) NextPhase(); break;          // Ignite（目覚めの光）
            case 3:                                                       // Talk（手動送り：Zで進む。自動送りはしない）
                DriveTalk(delta, zEdge);
                break;
            case 4: // Title → 難易度を左右で選び、Zでダイブ（STAGE1 あかり）
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
                if (zEdge && _t > 0.6) GetTree().ChangeSceneToFile("res://Hub.tscn");
                break;
            case 5: // 受講確認（既プレイ時のみ）：↑↓で はい/いいえ、Z決定、X=いいえ。
                bool au = Input.IsActionPressed("ui_up") || Input.IsActionPressed("ui_left");
                bool ad = Input.IsActionPressed("ui_down") || Input.IsActionPressed("ui_right");
                if ((au || ad) && !_askNavHeld)
                {
                    _askSel = (_askSel + 1) % 2; // 2択トグル
                    Audio.Instance?.PlayUiMove();
                }
                _askNavHeld = au || ad;
                bool back = Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.B);
                bool backEdge = back && !_backHeld;
                _backHeld = back;
                if (zEdge && _t > 0.2)
                {
                    Audio.Instance?.PlayUiConfirm();
                    GetTree().ChangeSceneToFile(_askSel == 0 ? "res://Stage0.tscn" : "res://Hub.tscn");
                }
                else if (backEdge)
                {
                    Audio.Instance?.PlayUiCancel();
                    GetTree().ChangeSceneToFile("res://Hub.tscn"); // X＝受けない
                }
                break;
        }

        QueueRedraw();
    }

    // ── フェーズ3：会話送り＋下書き選択 ──
    private void DriveTalk(double delta, bool zEdge)
    {
        // 選択の提示中は会話を止め、決まるまで待つ（ChoiceOverlay は自前で入力を取る）。
        if (_choice != null)
        {
            _choiceT += delta;
            if (!_choice.Decided) return;
            ApplyChoice(_choice.Selected);
            _choice.QueueFree();
            _choice = null;
            return;
        }
        // 差し込み点に達したら選択を出す（各差し込み点は台本の末尾に置かれる＝会話の終わりと同じ index）。
        if (_line == _p2ChoiceLine) { ShowChoice("p2", P2Choices, 0); return; }
        if (_line == _p3ChoiceLine) { ShowChoice("p3", P3Choices, 0); return; }
        if (_line == _p4ChoiceLine) { ShowChoice("p4", P4Choices, 0); return; }

        _lineT += delta;
        EnsurePages();
        // タイプライター送り（本編HUDと同じ MsgCharsPerSec。未設定なら48）。現在ページ内を進める。
        int len = _line < _talk.Count ? CurPage.Length : 0;
        if (_reveal < len)
            _reveal = Mathf.Min(len, (float)(_reveal + delta * (_game?.MsgCharsPerSec ?? 48f)));
        // 既読スキップ（#22）：行の表示開始時に一度だけ「既読か」を控え（＝高速送りの可否）、表示と同時に既読へ記録。
        if (_readIdx != _line && _line < _talk.Count)
        {
            _readIdx = _line;
            _lineWasRead = _game?.IsLineRead(_talk[_line].Text) ?? false;
            _game?.MarkLineRead(_talk[_line].Text);
        }
        _ffNow = Hud.SkipHeld && _lineWasRead; // 未読行では効かない＝取りこぼさない
        if ((zEdge || _ffNow) && _lineT >= 0.25)
        {
            if (_reveal < len)
            {
                _reveal = len; // まず現在ページの全文を即時表示（本編と同じ：1回目で早送り）
            }
            else if (!LastPage)
            {
                NextPage(); _lineT = 0;      // 後続ページがあれば続きへ（既読FFも同じ経路で全ページ抜ける）
            }
            else
            {
                _lineT = 0;
                _reveal = 0;
                _line++;
                _page = 0; _pagedLine = -1;
                // オープニングが終わったら、ハブへ（タイトルは起動時に表示済み）。
                //   ただし差し込み点（未提示の3択）に着いた場合は会話の途中＝次フレームの提示に譲る。
                if (_line >= _talk.Count && !AtChoicePoint) { StartGame(); return; }
            }
        }
    }

    // いま _line が未提示の差し込み点の上にいるか（＝会話の続きがある）。
    private bool AtChoicePoint => _line == _p2ChoiceLine || _line == _p3ChoiceLine || _line == _p4ChoiceLine;

    private void ShowChoice(string id, string[] choices, int defaultSel)
    {
        _choiceId = id;
        _choiceT = 0;
        _choice = ChoiceOverlay.Show(this, choices, defaultSel);
    }

    // 選択の確定：送った言葉と散った言葉を GameManager へ記録し、以降の会話を _talk へ挿し込む。
    private void ApplyChoice(int sel)
    {
        float hesitation = (float)_choiceT;
        switch (_choiceId)
        {
            case "p2":
            {
                string sent = P2Choices[sel];
                // 散った2語は元の並び順のまま（【初】は「散った2語のうち上の候補」＝先頭）。
                var others = new List<string>();
                for (int i = 0; i < P2Choices.Length; i++) if (i != sel) others.Add(P2Choices[i]);
                _p2Sec = hesitation;
                _game?.RecordChoice("p2", sent, others, hesitation);
                _talk.Insert(_line, L(WhoYou, sent, ""));
                _talk.InsertRange(_line + 1, P2Reply(sent));
                // 続けて P3（導入 → 選択）。差し込み点は導入の直後。
                var p3 = P3Intro();
                _talk.AddRange(p3);
                _p3ChoiceLine = _talk.Count;
                break;
            }
            case "p3":
            {
                // （送らない）は言葉ではないので【散】に数えない＝選ぶと表示候補（上2つ）が全部散る。
                string sent = sel == 2 ? "" : P3Choices[sel];
                var others = new List<string>();
                for (int i = 0; i < P3Choices.Length - 1; i++) if (i != sel) others.Add(P3Choices[i]);
                if (_game != null) _game.NameRoute = sel;
                _game?.RecordChoice("p3", sent, others, hesitation);
                if (sent != "") _talk.Insert(_line, L(WhoYou, sent, ""));
                _talk.InsertRange(sent != "" ? _line + 1 : _line, P3Reply(sel));
                // 続けて P4（導入 → 選択）。
                var p4 = P4Intro();
                _talk.AddRange(p4);
                _p4ChoiceLine = _talk.Count;
                break;
            }
            default:
            {
                string sent = P4Choices[sel];
                var others = new List<string>();
                for (int i = 0; i < P4Choices.Length; i++) if (i != sel) others.Add(P4Choices[i]);
                _game?.RecordChoice("p4", sent, others, hesitation);
                _talk.Insert(_line, L(WhoYou, sent, ""));
                _talk.InsertRange(_line + 1, P4Reply(sel));
                break;
            }
        }
        // 済んだ差し込み点は潰す（挿し込みで _line がそのまま同じ番号に留まるため、消さないと再提示になる）。
        if (_choiceId == "p2") _p2ChoiceLine = -1;
        else if (_choiceId == "p3") _p3ChoiceLine = -1;
        else _p4ChoiceLine = -1;
        // 挿し込みで現在行の中身が変わる＝ページ・タイプライターを組み直す。
        _pagedLine = -1; _page = 0; _reveal = 0; _lineT = 0; _readIdx = -1;
    }

    private void NextPhase()
    {
        _phase++;
        _t = 0;
        _lineT = 0;
        _reveal = 0; // 会話フェーズに入ったら1行目を最初から打ち出す
    }

    // オープニング（起動カットシーン）の後の遷移分岐。
    //   ・チュートリアル未受講(TutorialSeen==false) → 確認を出さず自動でステージ0（完全チュートリアル）へ。
    //   ・受講済み(TutorialSeen==true)            → 受講確認（はい/いいえ）を出す。はい→Stage0 / いいえ→Hub。
    //   ※「はじめから」は ResetPersistent 済みだが TutorialSeen は端末ローカル prefs で別管理（消えない）＝
    //     一度通したプレイヤーには毎回スキップ選択肢を出す、という設計。
    private bool _started;
    private void StartGame()
    {
        if (_started) return;
        var g = GetNodeOrNull<GameManager>("/root/Game");
        if (g == null || !g.TutorialSeen)
        {
            _started = true;
            GetTree().ChangeSceneToFile("res://Stage0.tscn");
            return;
        }
        // 受講済み：確認フェーズへ（シーン遷移はそこで決める）。
        _phase = 5;
        _t = 0;
        _askSel = 0;
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
            case 3: DrawTalkBackdrop(); DrawTalkSpeakers(); DrawTalk(); break;
            case 4: DrawTitle(); break;
            case 5: DrawTutorialAsk(); break;
        }

        // R/Start 長押しリトライの充填チップ（押している間だけ・設計座標で描く）。
        if (_retry.Progress > 0f)
        {
            UiKit.BeginDesign(this);
            Hud.DrawRetryHoldChip(this, _retry.Progress,
                (Pad.ShowKeyboard ? "R" : Pad.Face(JoyButton.Start)) + " 長押しでさいしょから");
            UiKit.EndDesign(this);
        }
    }

    // 受講確認ダイアログ（既プレイ時）。TitleMenu.DrawDisplayPicker の作り（暗幕＋角丸Box＋↑↓選択＋Z決定/X戻る）を流用。
    private void DrawTutorialAsk()
    {
        UiKit.BeginDesign(this);
        float W = UiKit.DesignW, H = UiKit.DesignH;
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.66f)); // 暗幕
        var choices = new[] { "はい（チュートリアルを受ける）", "いいえ（そのまま始める）" };
        int n = choices.Length;
        float w = 640, rowH = 60, h = 150 + n * rowH, x = (W - w) / 2f, y = (H - h) / 2f;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.98f), 16f, new Color(UiKit.Purify, 0.7f), 1.4f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + 26), "チュートリアルを受けますか?", UiKit.FontHeading, UiKit.White, HorizontalAlignment.Center, w);
        UiKit.Text(this, UiKit.Zen, new Vector2(x, y + 54), "操作の手ほどきです（受けなくても、すぐ始められます）", UiKit.FontLabel,
            UiKit.Text3, HorizontalAlignment.Center, w);
        float top = y + 86;
        for (int i = 0; i < n; i++)
        {
            float ry = top + i * rowH;
            bool on = i == _askSel;
            if (on)
            {
                UiKit.Box(this, new Rect2(x + 28, ry, w - 56, 50), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 10f, new Color(UiKit.Purify, 0.45f), 1f);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + 44, ry + 16), "▸", UiKit.FontBody, UiKit.Purify);
            }
            Color nameCol = on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 70, ry + 13), choices[i], UiKit.FontSpeaker, nameCol);
        }
        UiKit.Text(this, UiKit.Mono, new Vector2(x, y + h - 30), "↑↓ えらぶ    Z けってい    X 受けない", UiKit.FontSmall,
            UiKit.Text3, HorizontalAlignment.Center, w);
        UiKit.EndDesign(this);
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

    // --- フェーズ1：identity は保留のまま（[ M I N A ] は P3 の命名まで点灯しない）---
    private void DrawIdentity()
    {
        if (_font == null) return;
        bool blink = ((int)(_t * 3f) % 2) == 0;
        DrawString(_font, new Vector2(W / 2f - 120f, 100f), "> assigning identity ...",
            HorizontalAlignment.Left, -1, 9, new Color(Code.R, Code.G, Code.B, 0.7f));
        // 保留の一行だけが、答えを待って明滅し続ける。
        if (blink)
            DrawString(_font, new Vector2(W / 2f - 34f, 118f), "[ deferred ]",
                HorizontalAlignment.Left, -1, 9, new Color(Code.R, Code.G, Code.B, 0.85f));
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

    // --- フェーズ3：会話の背後に流す「デジタル空間」背景 ---
    // フェーズ0 DrawRain の資産（_stream / コード緑 / 上昇スクロール）を流用し、
    // “さらに薄く・遅く”流す。立ち絵・会話ボックス・本文の可読性を絶対に侵さないよう、
    // 画面下40%（ボックス帯）と立ち絵の真後ろは能動的にアルファを落とす。
    //
    // 調整ポイント（強度ノブ）：いずれもアルファ上限。0.14 を超えると本文と競り始める＝危険域。
    private const float BgRainMax = 0.10f; // コードレイン（薄め・遅め）
    private const float BgGridA   = 0.045f; // デジタルグリッド
    private const float BgDotMax  = 0.11f; // 漂うドット粒子
    private const float BgWashMax = 0.05f; // 上方の青い奥行きウォッシュ（Cool）
    private const float BoxTopY   = H - 58f; // 会話ボックス上端。これ以下は背景を消していく
    private const float FadeReach = 44f;     // ボックス上端の何px手前から背景を絞り始めるか

    // y 位置の背景許容率（下＝ボックス帯ほど 0 に。上は 1）。文字可読性を守る最重要ガード。
    private static float BgYGate(float y) => Mathf.Clamp((BoxTopY - y) / FadeReach, 0f, 1f);

    // 立ち絵の真後ろ（中央バンド）を落として、シルエットを澄んだ空間に立てる（吉田 §1）。
    private static float BgCenterDim(float x, float y)
    {
        float dx = (x - W / 2f) / 70f;
        float dy = (y - 92f) / 70f;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp(d - 0.25f, 0f, 1f); // 中心ほど 0（背景を消す）、外ほど 1
    }

    private void DrawTalkBackdrop()
    {
        if (_font == null) return;

        // レイヤー1：奥行きウォッシュ。上に薄く Cool、中盤で黒へ。ボックス帯は素の黒のまま。
        int bands = 5;
        for (int i = 0; i < bands; i++)
        {
            float y0 = i * (BoxTopY / bands);
            float a = BgWashMax * (1f - (float)i / bands);
            DrawRect(new Rect2(0, y0, W, BoxTopY / bands + 1f),
                new Color(Cool.R, Cool.G, Cool.B, a));
        }

        // レイヤー2：デジタルグリッド（コード緑・上昇ドリフト）。空間の床に見せる。
        float gScroll = (float)_t * 14f;
        const float gStep = 22f;
        float gy0 = -Mathf.PosMod(gScroll, gStep);
        for (float gy = gy0; gy < BoxTopY; gy += gStep)
        {
            float a = BgGridA * BgYGate(gy);
            if (a <= 0.002f) continue;
            DrawRect(new Rect2(0, gy, W, 1f), new Color(Code.R, Code.G, Code.B, a));
        }
        for (float gx = Mathf.PosMod(-gScroll, gStep); gx < W; gx += gStep)
        {
            // 縦線は y方向に薄くグラデートしながら（下ほど消す）
            for (float gy = 0f; gy < BoxTopY; gy += 4f)
            {
                float a = BgGridA * 0.7f * BgYGate(gy) * BgCenterDim(gx, gy);
                if (a <= 0.002f) continue;
                DrawRect(new Rect2(gx, gy, 1f, 4f), new Color(Code.R, Code.G, Code.B, a));
            }
        }

        // レイヤー3：コードレイン（フェーズ0の _stream を流用。半分の速度・大きい行間・低アルファ）。
        const float lineH = 16f;            // phase0=11 より疎に
        float scroll = (float)_t * 30f;     // phase0=78 の半分以下＝ゆっくり
        float baseBottom = BoxTopY - 4f;
        for (int i = 0; i < _stream.Count; i++)
        {
            float y = baseBottom + i * lineH - scroll;
            if (y < -lineH || y > BoxTopY) continue;
            float top = 1f - Mathf.Clamp((H - y) / H, 0f, 1f) * 0.5f; // 上ほどさらに薄く
            float a = BgRainMax * top * BgYGate(y) * BgCenterDim(40f, y);
            if (a <= 0.003f) continue;
            // 横位置は流れごとにずらして単調さを消す（決定論的）
            float x = 8f + ((i * 53) % 300);
            DrawString(_font, new Vector2(x, y), _stream[i], HorizontalAlignment.Left, -1, 8,
                new Color(Code.R, Code.G, Code.B, a));
        }

        // レイヤー4：漂うドット粒子（決定論ハッシュで配置。新規依存なし）。Code/Cool を混ぜる。
        const int dots = 20;
        float pScroll = (float)_t * 9f;
        for (int i = 0; i < dots; i++)
        {
            float hx = ((i * 73 + 11) % 100) / 100f;
            float hy = ((i * 137 + 41) % 100) / 100f;
            float x = hx * W;
            float y = Mathf.PosMod(hy * (BoxTopY + 60f) - pScroll, BoxTopY + 60f) - 30f;
            if (y < 0f || y > BoxTopY) continue;
            float twinkle = 0.6f + 0.4f * Mathf.Sin((float)_t * 1.6f + i * 1.3f);
            float a = BgDotMax * twinkle * BgYGate(y) * BgCenterDim(x, y);
            if (a <= 0.004f) continue;
            bool blue = (i % 3) == 0;
            var c = blue ? Cool : Code;
            DrawCircle(new Vector2(x, y), (i % 2 == 0) ? 1.0f : 0.7f, new Color(c.R, c.G, c.B, a));
        }
    }

    // --- フェーズ3：話者の立ち絵を中央に表示（行ごとの表情を反映） ---
    private void DrawTalkSpeakers()
    {
        if (_line >= _talk.Count) return;
        string face = _talk[_line].Face;
        if (string.IsNullOrEmpty(face)) return;   // システム表示・投稿・あなたの下書きには立ち絵を出さない
        var tex = ResourceLoader.Load<Texture2D>(face);
        if (tex != null)
        {
            float th = 132f;
            float tw = th * tex.GetWidth() / tex.GetHeight();
            float px = (W - tw) / 2f; // 中央寄せ
            DrawTextureRect(tex, new Rect2(px, H - 58f - th + 8f, tw, th), false);
        }
    }

    // 行の書体：システム表示（起動ログ・[ M I N A ]）だけ等幅＝端末の生ログに見せる（Epilogue の作法と同じ）。
    private static Font FontFor(DLine d) => d.Who == WhoSys ? (Font)UiKit.Mono : UiKit.Zen;

    // 話者ラベルと額縁の色。ミナ＝シアン／あなた＝暖色／投稿＝Ｘ投稿（Hud と同じ Text3）／システム＝コード緑。
    private static (string label, Color col) SpeakerOf(DLine d) => d.Who switch
    {
        WhoMina => ("ミナ", Cool),
        WhoYou  => ("あなた", Warm),
        WhoPost => ("Ｘ 投稿", UiKit.Text3),
        _       => ("", Code),
    };

    // --- フェーズ3：会話ボックス ---
    private void DrawTalk()
    {
        if (_font == null || _line >= _talk.Count) return;
        var d = _talk[_line];
        var (label, edge) = SpeakerOf(d);
        var font = FontFor(d);
        // 現在ページ（2行固定・禁則つき）。ボックスは2行分の固定高さ（行数で伸ばさない＝全ボックス統一）。
        string page = CurPage;
        var lines = UiKit.WrapLines(font, page, UiKit.CutBody, W - 56);
        float boxTop = H - 58f;   // 2行固定（下余白12px＝額縁を効かせる）
        // ボックス（Hub/Shop と同じ角丸＋話者色の額縁。UiKit.CutBox で3画面共通）
        UiKit.CutBox(this, new Rect2(14, boxTop, W - 28, H - 10f - boxTop), edge, d.Who == WhoSys ? 0.4f : 0.5f);
        // 話者名（滑らかゴシック）。システム表示はコンソール行＝話者がいないのでラベルを出さない。
        if (label != "")
            DrawString(UiKit.ZenBold, new Vector2(24, boxTop + 12), label, HorizontalAlignment.Left, -1, UiKit.CutSpeaker, edge);
        // 本文（タイプライターで表示済みの分だけ、確定済みの行に沿って描画）
        int shown = Mathf.Clamp((int)_reveal, 0, page.Length);
        UiKit.TypewriterLines(this, font, lines, new Vector2(24, boxTop + 27f), W - 56, UiKit.CutBody,
            d.Who == WhoSys ? Code : UiKit.CutInk, shown);
        // 既読高速送り中の控えめな表示（ボックス右上・#22）。
        if (_ffNow)
            DrawString(UiKit.ZenBold, new Vector2(W - 42, boxTop + 12), "▶▶", HorizontalAlignment.Left, -1, UiKit.CutSpeaker,
                new Color(Cool, 0.8f));
        // 送り三角は「現在ページの全文表示後」だけ点滅（本編と同じ作法）。
        //   後続ページがあることは同じ▼で示す（Zで続きへ／最終ページなら次の行へ）。
        bool revealed = _reveal >= page.Length;
        if (revealed && ((int)(_t * 2f) % 2) == 0)
            DrawString(_font, new Vector2(W - 26, H - 16), "▼", HorizontalAlignment.Left, -1, UiKit.CutNote,
                new Color(1f, 1f, 1f, 0.7f));
    }

    // --- フェーズ4：タイトル ---
    private void DrawTitle()
    {
        if (_font == null) return;
        float a = Mathf.Clamp((float)_t / 1.0f, 0f, 1f);
        DrawString(_font, new Vector2(0, 78f), "X — タイムライン", HorizontalAlignment.Center, W, UiKit.CutClimax,
            new Color(0.9f, 0.92f, 1f, a));
        DrawString(_font, new Vector2(0, 104f), "STAGE 1 : あかり", HorizontalAlignment.Center, W, UiKit.CutBody,
            new Color(Cool.R, Cool.G, Cool.B, a * 0.9f));

        // 難易度選択（◀ ▶ で変更）
        DrawString(_font, new Vector2(0, 132f), "難易度  ◀ " + DiffNames[_diffSel] + " ▶",
            HorizontalAlignment.Center, W, UiKit.CutBody, new Color(1f, 0.92f, 0.6f, a));

        if (_t > 1.0 && ((int)(_t * 1.5f) % 2) == 0)
            DrawString(_font, new Vector2(0, 158f), "← → 難易度   Z：ダイブ   R：最初から",
                HorizontalAlignment.Center, W, UiKit.CutNote, new Color(1f, 1f, 1f, 0.7f));
    }
}
