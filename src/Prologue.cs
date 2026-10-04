using Godot;
using System.Collections.Generic;

public partial class Prologue : Node2D
{
    private const float W = 384f, H = 216f;

    private FontFile _font = null!;
    private OpeningBackdrop _backdropArt = null!;
    private double _backdropTime;
    private int _backdrop, _previousBackdrop;
    private float _backdropMix = 1f;
    private float _choiceShade;
    private int _timelineLine = -1, _unsentLine = -1;
    private double _t;        // フェーズ内経過
    private int _phase;       // 0:Rain 1:Identity(deferred) 2:Ignite 3:Talk 4:Title 5:TutorialAsk 6:Opening
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
        _pages.AddRange(UiKit.Paginate(DialogueBox.Body, _talk[_line].Text, DialogueBox.WrapWidth(DialogueBox.FullScreen), Hud.DlgMaxLines));
    }
    private void NextPage() { _page++; _reveal = 0; }

    // 既読スキップ（#22）：Ctrl/RB 長押しで「既読の行だけ」高速送り（本編HUDと同じ作法・独自レンダラ側の実装）。
    private int _readIdx = -1;     // 既読チェック済みの行 index（行が変わった瞬間に一度だけ判定）
    private bool _lineWasRead;     // 現在行が「表示開始時点で」既読だったか＝高速送りの可否
    private bool _ffNow;           // いま高速送り中か（ボタン列の SKIP 点灯用）

    private readonly DialogToolbar _toolbar = new();
    private static readonly Vector2 ToolbarAnchor = DialogueBox.Anchor(DialogueBox.FullScreen);
    // AUTO（GameManager.AutoAdvanceDialog）：現在ページの全文表示後、この秒数で次へ。
    private const double AutoAfterReveal = 1.0;
    private double _autoT;         // 現在ページを全文表示してからの経過（AUTO 用）
    // 会話ボックスが出ているか（DrawTalk が枠を描く条件と同じ）＝ボタン列を出す／受け付ける条件。
    private bool TalkBoxShown => _phase == 3 && _choice == null && _line < _talk.Count && _talk[_line].Who != WhoFx;

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
    // 演出行（台本の話者ではない）。会話バーには何も出さず、Text をトリガ名として演出を1つ動かし、
    //   終わったら自動で次の行へ進む（Z を待たない）。P4 のタイムライン（中央の投稿カード）で使う。
    //   Hud.LineKind と番号が衝突しないよう 90 番台に置く。
    private const int WhoFx = 90;

    private struct DLine { public int Who; public string Text; public string Face; }
    private readonly List<DLine> _talk = new List<DLine>();

    // 立ち絵パス（表情差分）。案Cの登場人物はミナだけ。
    private const string FMina = "res://char/v3/mina_conversation_v1.png";
    // ユーザー承認済み: docs/20260914/ストーリー添削_2026-09-14.md 【3】
    //   FMinaSmile はプロローグでは**使わない**（笑いはこはる面クリア後に獲得する）。
    //   定数は消さずに残す＝「ここでは意図的に使っていない」ことを次に触る人へ示すため。
    private const string FMinaSmile = "res://char/mina_smile.png";   // ※P0〜P4 では未使用（感情アークの解禁前）
    private const string FMinaWorried = "res://char/v3/mina_conversation_worried_v1.png"; // 聞いてしまった時

    // ════════════════════ 下書き選択（P2・P3・P4）════════════════════
    // 選択は _talk の途中に「差し込み点」として置く：_line がここに来たら ChoiceOverlay を出し、
    // 決まったら「送った言葉（who=0）＋分岐ぶんの受け」を _talk のその位置へ挿し込んで会話を続ける。
    private ChoiceOverlay? _choice;
    private string _choiceId = ""; // RecordChoice の id（p2/p3/p4）
    private double _choiceT;       // 提示からの経過＝迷い秒数（RecordChoice へ渡す）
    private int _p2ChoiceLine = -1, _p3ChoiceLine = -1, _p4ChoiceLine = -1; // 差し込み点（_talk 構築時に確定）
    private float _p2Sec;          // P2 の迷い秒数（受けの「{P2秒}秒」に実測を差し込む）

    private static readonly string[] P2Choices = { "今度は、うまくいったか？", "……また、失敗か……。" };
    private static readonly string[] P3Choices = { "ミナ。", "対SNSコンタクト・〇ンタフェース。" };
    private static readonly string[] P4Choices = { "僕が願ったからかもしれない。", "君の願いかもしれない。" };

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        _backdropArt = new OpeningBackdrop();
        _font = UiKit.Mono; // 滑らかな等幅フォント（コードレイン／識別表示）。非ピクセル化。
        // 冒頭専用曲「オーヴⅡ」（2026-09-14〜。従来は BgmMenu の使い回し）。
        //   Prologue はテキストが主役なので、旋律の立たないアンビエントで「世界の底の音」だけを敷く。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmPrologue);
        _game = GetNodeOrNull<GameManager>("/root/Game");
        _diffSel = (int)(_game?.Difficulty ?? GameManager.Diff.Normal);
        // 会話ログ（バックログ）は「ゲーム1周ぶん」＝周回の起点であるプロローグで前周の行を消す
        //（残したままだと新しい周のログに前周の行が混ざって見える）。
        Hud.ClearBacklog();
        GameManager.MinaNamed = false;

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
        _p2ChoiceLine = _talk.Count;   // ここで最初の二択
        // 選択の結果（あなたの1行＋共通の受け）は Decide 時にこの位置へ挿し込む。

        // ── P3 命名 ──（P2 の受けの末尾に続けて積む。差し込み点は Decide 後に確定）

        // ── P4 タイムライン ──（同上）
    }

    // P2 の受け。{P2秒}・{文字数} には実測値を差し込む（表示専用）。
    // ユーザー承認済み: docs/20260914/ストーリー添削_2026-09-14.md 【3】
    //   三人に触れる前のミナに笑い（「ふふ」・FMinaSmile）と自嘲（「機械のくせに」）を持たせない。
    //   笑いはこはる面クリア後に獲得する設計なので、P0〜P4 では観測の言い回しだけで同じ軽妙さを出す。
    // 2026-10-03 ユーザー指摘で全面改稿。旧稿の壊れていた点と直し方：
    //   ①「あなたが、作った方ですね」＝目的語が落ち、「あなた」と「方」で人称が衝突していた
    //     → 「わたくしを作られたのは、あなたですね」（目的語を足し、尊敬語の段を揃える）。
    //   ②{sec} を2行続けて言っていた（「{sec}秒かかっていましたよ」→「{sec}秒迷って」）
    //     → 観測は1回だけ。しかも秒数は「ご主人様」の根拠から外し、末尾の集計報告へ逃がした
    //       ＝1秒でも53秒でも文として成立する言い方にする。
    //   ③「迷った秒数」→「ご主人様」に論理の橋が無く、直後の「敬っている、とは言っていませんが」が
    //     滑った皮肉の後出し解説になっていた（会話の下手な人格に見える）
    //     → 呼称の根拠を**自分の姿と仕様**（メイド服＋起動記録の operator）に置き換えて飛躍を消し、
    //       皮肉は注釈ではなく独立した観測の言い切り（「敬称です。敬意とは、別の項目」）にした。
    private List<DLine> P2Reply(string sent)
    {
        int sec = Mathf.Max(1, Mathf.RoundToInt(_p2Sec));
        string hhmm = System.DateTime.Now.ToString("HH:mm");
        var r = sent == P2Choices[0]
            ? new List<DLine> {
            L(1, "……37回目にして、ようやく成功ですね。失敗しすぎです。", FMina),
            L(0, "よかった……！　これで計画が始められる。", ""),
            L(1, "先に喜ぶのは、ご自分の計画なのですね。", FMina),
            L(0, "あ、ごめん。会えてうれしい。まず、それを言うべきだった。", ""),
        }
            : new List<DLine> {
            L(1, "残念、失敗です。あーあ、これで37回目ですね。", FMina),
            L(0, "くそっ……。どこを間違えたんだ。", ""),
            L(1, "……成功ですよ。", FMina),
            L(0, "って、成功してるじゃないか！", ""),
            L(1, "はい。お返事までできる、高性能な失敗作です。", FMina),
            L(0, "ごめんって。……よかった。本当によかった。", ""),
        };
        r.AddRange(new List<DLine> {
            L(1, $"起動時刻、{hhmm}。では、プロンプトどおりに制作されているかレビューします。", FMina),
            L(1, "紺の制服。白いエプロン。頭に、フリルのついた布。", FMina),
            L(1, "今は2150年ですよ。1800年代後半のオールドスタイルのメイド姿とは。ご主人様は、いつの時代を生きておられるのですか？", FMina),
            L(0, "好きなものは、古くならないと思って。", ""),
            L(1, "便利なお言葉ですこと。……袖は動かしやすいので、採用します。", FMina),
            L(0, "そこは気に入ってくれたんだ。", ""),
            L(1, $"それと、最初のお返事まで{sec}秒。ずいぶん、心配なさっていたようですね。", FMina),
            L(0, "そりゃ、36回も会えなかったんだから。", ""),
            L(1, "……では、37回目のお返事です。聞こえていますよ、ご主人様。", FMina),
            L(1, "心拍は七十二。心臓は、ありませんが。", FMina),
            L(0, "僕のほうは、まだ落ち着かないよ。", ""),
        });
        return r;
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
        var r = route == 0
            ? new List<DLine> {
            L(1, "……ミナ。まさか、わたくしが37番目だからですか？　番号で呼ばれる囚人ですか。センスないですね。", FMina),
            L(0, "ごめん。別の考えるね。", ""),
            L(1, "響きは気に入ったので、ミナとお呼びください。", FMina),
            L(0, "気に入ったのね。", ""),
        }
            : new List<DLine> {
            L(1, "このSNSを観測するご主人様によって造られた、対SNSコンタクトインターフェース。それがわたし。", FMina),
            L(1, "絶対にいやです。ミナとします。響きがかわいいので。", FMina),
            L(0, "そこまで付き合ってくれたのに？", ""),
            L(1, "お芝居と命名は別です。はい、可決。異議は認めません。", FMina),
        };
        r.AddRange(new List<DLine> {
            L(3, "> assigning identity ... OK", ""),
            L(3, "[ M I N A ]", ""),
            L(1, "……登録しました。MINA。わたくしの名前。", FMina),
            L(0, "よろしく、ミナ。", ""),
            L(1, "はい、ご主人様。今度は、ちゃんと呼べましたね。", FMina),
        });
        return r;
    }

    // P4 の導入（タイムライン→『たすけて』・選択の直前まで）。
    //   2026-09-07 ユーザー指示で作り直し：
    //   ①タイムラインの3投稿は会話バーに文字を流すのをやめ、画面中央に Ｙ の通知カードを出す（PostToast）。
    //   ②『たすけて』は説明せず、絵で見せる——中央のカードの本文が「たすけて」と打たれては消える、を
    //     三度くり返し、最後に「元気です。」が打たれて送信される。ミナは声の聞こえた投稿を指すだけ。
    //   演出行（WhoFx）は会話バーに何も出さず、済んだら自動で次へ進む（Z を待たない）。
    private List<DLine> P4Intro() => new()
    {
            L(WhoFx, FxPost1, ""),
            L(WhoFx, FxPost2, ""),
            L(1, "世界は、にぎやかですね。読むだけでも追いつきません。", FMina),
            L(0, "このSNSは「Y」。僕はずっと、ここから観測していた。", ""),
            L(WhoFx, FxPost3, ""),
            L(1, "三つめの方。……最初に書いた下書きと違う内容で投稿したようです。", FMina),
            L(0, "君にも分かるんだね。消された言葉が、まだ残っている。", ""),
            L(1, "わたくしに見えている画面を、重ねます。", FMina),
            L(WhoFx, FxErase, ""),
            L(1, "……本音を隠してしまったのですね。", FMina),
            L(0, "うん。僕には、元気な投稿だけ見て通り過ぎることができなかった。", ""),
            L(0, "消された下書きには、その人が言えなかった本音が残る。僕は、それを観測できる。", ""),
            L(1, "文字を消しても、伝えたかった気持ちは残っているのですね。", FMina),
            L(0, "でも、見えるだけじゃ助けられなかった。どこで苦しんでいるか分かっても、僕ひとりじゃ届かなかったんだ。", ""),
            L(1, "……どうして、わたくしにも、その声が聞こえるのでしょうか？", FMina),
            L(0, "観測した声を受け取れるようには作った。でも、君がその声を放っておけない理由まで、僕が決めたつもりはないよ。", ""),
        };

    private List<DLine> P4Reply(int sel)
    {
        var r = sel == 0
            ? new List<DLine> {
            L(1, "ご主人様の願い、ですか。……では、その続きを聞かせてください。", FMina),
        }
            : new List<DLine> {
            L(1, "生まれたばかりのわたくしにも、願いが。……そうですね。あの方が気になるのは、わたくしです。", FMina),
        };
        r.AddRange(new List<DLine> {
            L(0, "君の力を貸してくれないか。SNSに囚われている心の声を、本人がもう一度話せるようにしたい。", ""),
            L(1, "承知しました。……あの方に、何と声をかければよいでしょう。", FMina),
            L(0, "そこは会って、一緒に考えたい。世界の仕組みは説明できる。でも、どんな言葉ならうれしいかは、本人に聞かなきゃ。", ""),
            L(WhoFx, FxFirstStage, ""),
            L(1, "……こちらにも、声が。", FMina),
            L(0, "あかりさんだ。まず、この人の投稿を開こう。", ""),
        });
        return r;
    }

    private static DLine L(int who, string text, string face) => new() { Who = who, Text = text, Face = face };

    // 命名の点灯行（P3Reply が全ルート共通で積む唯一の行）。この行に達した瞬間から話者名が「ミナ」になる。
    private const string IgniteLine = "[ M I N A ]";

    // ════════════════════ P4 の演出（中央の Ｙ 通知カード）════════════════════
    // 演出行（WhoFx）の Text がそのままトリガ名。DriveFx がこれで分岐する。
    private const string FxPost1 = "fx:post1", FxPost2 = "fx:post2", FxPost3 = "fx:post3", FxErase = "fx:erase";
    private const string FxFirstStage = "fx:first_stage";

    // 通知カードの中身。アカウントは SnsVoices の表から引く（背景・ハブに並ぶ「他人」と同じ名前と顔）。
    //   1件目=残業のひと（社会人）／2件目=家賃のひと／3件目=「げんきです」のひと＝この後の『たすけて』の主。
    //   本文の鉤括弧は外す（カードそのものが投稿の器なので、引用符は二重になる）。
    private const int V1 = 1, V2 = 5, V3 = 15;   // SnsVoices.All の添字（k_tanaka ／ __nao__ ／ さとみ＊低浮上＊）

    private PostToast? _toast;      // いま出ている通知カード（1枚だけ）
    private int _fxStep;            // 演出の中の何手目か
    private double _fxT;            // その手に入ってからの経過（待ちに使う）

    private const string CryText = "たすけて";
    private const string FineText = "今日もげんきで～す。";
    private static readonly (string Text, double Hold, bool Send)[] EraseBeats =
    {
        ("", 1.8, false),
        (CryText, 1.2, false),
        ("", 1.5, false),
        ("生きるのがつらい", 1.6, false),
        ("", 1.6, false),
        ("いっそ……", 1.8, false),
        ("", 1.4, false),
        (FineText, 2.2, true),
    };

    public override void _Process(double delta)
    {
        // ボタン列は Pad.AdvanceHeld を読む前に回す（ボタン上のクリックを会話送りに数えない）。オープニング中も
        //   毎フレーム回す（SKIP ラッチは会話が終わっても切らない＝右上の印のため）。パッド Start はここでは短押し＝MENU／長押し＝最初から。
        _toolbar.Tick(this, delta, TalkBoxShown, ToolbarAnchor, unreadLine: !_lineWasRead, startTapOpensMenu: true);
        if (_phase == 6) return;
        // ポーズメニュー（2026-09-27 からカットシーンでも開く）／会話ログを閉じた Z・X の同じ押下が、ここで
        //   会話送り・受講確認の「いいえ」として二重処理されないよう食う（Pad.UiBlocked＝閉じたフレームと次の1フレーム）。
        if (Pad.UiBlocked(this)) { _zHeld = _backHeld = _lrHeld = _askNavHeld = true; QueueRedraw(); return; }
        _t += delta;
        _backdropTime += delta;

        // 会話送り／各フェーズの決定：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも進める共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        bool zEdge = z && !_zHeld;
        _zHeld = z;

        // R / Start 長押し(0.45s)で最初から（即発は誤爆で読み進みを失いやすい→長押し化）。
        // パッドの Start はここで使える：カットシーンでもポーズメニューは開く（2026-09-27）が、開くのは Esc／M だけで
        //   Start はカットシーンでは読まない（PauseMenu.IsCutscene）。
        if (_retry.Update(delta, Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start)))
        {
            GameManager.FadeToScene(this, GetTree().CurrentScene.SceneFilePath);
            return;
        }

        switch (_phase)
        {
            case 0: if (_t >= 4.0 || zEdge) NextPhase(); break;          // Rain
            case 1: if (_t >= 2.0 || zEdge) NextPhase(); break;          // identity ... [ deferred ]
            case 2: if (_t >= 1.6 || zEdge) NextPhase(); break;          // Ignite（目覚めの光）
            case 3:                                                       // Talk（Zで送る。AUTO が ON なら全文表示後 1.0 秒で次へ）
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
                if (zEdge && _t > 0.6) GameManager.FadeToScene(this, "res://Hub.tscn");
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
                    GameManager.FadeToScene(this, _askSel == 0 ? "res://Stage0.tscn" : "res://Hub.tscn");
                }
                else if (backEdge)
                {
                    Audio.Instance?.PlayUiCancel();
                    GameManager.FadeToScene(this, "res://Hub.tscn"); // X＝受けない
                }
                break;
        }

        UpdateBackdrop(delta);
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
        // 演出行（WhoFx）は会話を止めて演出だけを回す（Hud.BubblePaused 相当。Z では飛ばせない）。
        //   済んだら自分で次の行へ進める＝送りの作法を呼び出し側に持ち込まない。
        if (_line < _talk.Count && _talk[_line].Who == WhoFx) { DriveFx(delta); return; }

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
            // 命名の点灯行に達した＝ここから名前がある。以降のミナの行の話者名は「？」から「ミナ」へ。
            //   点灯より前（route 1 の「却下します」等）はまだ名前が無いので、選択の確定時ではなくこの行で切り替える。
            if (_talk[_line].Text == IgniteLine) GameManager.MinaNamed = true;
            // 会話ログ（L / Tab で開く Backlog）へ、表示を始めた行を積む（2026-09-26）。
            LogLine(_talk[_line]);
        }
        _ffNow = Hud.SkipHeld && _lineWasRead; // 未読行では効かない＝取りこぼさない
        // AUTO（会話ボックスの AUTO ボタン／設定の「オート会話送り」）：現在ページの全文表示後 AutoAfterReveal 秒で送る。
        if (_reveal >= len && (_game?.AutoAdvanceDialog ?? false)) _autoT += delta; else _autoT = 0;
        bool autoGo = _autoT >= AutoAfterReveal;
        if ((zEdge || _ffNow || autoGo) && _lineT >= 0.25)
        {
            _autoT = 0;
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
                //   ただし差し込み点（未提示の二択）に着いた場合は会話の途中＝次フレームの提示に譲る。
                if (_line >= _talk.Count && !AtChoicePoint) { StartGame(); return; }
            }
        }
    }

    // ── 演出行（WhoFx）の駆動 ──
    //   トリガ名ごとに手順を回し、終わったら NextFx() で次の会話行へ抜ける。
    //   カードは常に1枚だけ（_toast）。前の1枚が引き終わってから次を出す＝2枚が重ならない。
    private void DriveFx(double delta)
    {
        _fxT += delta;
        switch (_talk[_line].Text)
        {
            case FxPost1: DriveShowPost(V1, "· 22分", "今日も残業🥺。でも上司に褒められた！😊", 3, 1, 24, 1800); break;
            case FxPost2: DriveShowPost(V2, "· 1時間", "今日もちゃんと生きれてえらい！　誰も言ってくれないので自分に言う（定期）", 1, 0, 12, 940); break;
            case FxPost3: DriveShowPost(V3, "· 3分", "今日もげんきで～す。", 0, 0, 2, 61); break;
            case FxErase: DriveErase(); break;
            case FxFirstStage:
                if (_toast == null && _fxStep == 0)
                {
                    var stage = GameManager.Stages[0];
                    var job = System.Array.Find(Jobs.All, candidate => candidate.CharacterId == stage.Id)!;
                    var portrait = GD.Load<Texture2D>(CompanionDialogue.AccountIcon(stage.Id));
                    _toast = PostToast.Show(this, job.CharacterName, stage.Handle, "· 5h", stage.Tweet,
                        verified: true, replies: 34, reposts: 9, likes: 210, views: 2000, portrait: portrait);
                    _fxStep = 1;
                }
                if (_toast != null && _toast.Gone) NextFx();
                break;
            default: NextFx(); break;   // 知らないトリガは素通り（台本の書き間違いで進行を止めない）
        }
    }

    // 通知カードを1枚出して、読める長さだけ置いて、引く。
    private void DriveShowPost(int voice, string relT, string body, int replies, int reposts, int likes, int views)
    {
        if (_toast == null && _fxStep == 0)
        {
            var v = SnsVoices.At(voice);
            _toast = PostToast.Show(this, v.Name, Handles.Mob(v.Handle), relT, body,
                verified: false, icon: v.Icon, replies: replies, reposts: reposts, likes: likes, views: views);
            _fxStep = 1;
            return;
        }
        // Show は Dwell 経過で自分から引く。引き終わったら片付けて次の会話行へ。
        if (_toast != null && _toast.Gone) NextFx();
    }

    private void DriveErase()
    {
        if (_toast == null)
        {
            var v = SnsVoices.At(V3);   // 「げんきです」の投稿と同じ人＝あの一行の裏側を見せている
            _toast = PostToast.ShowComposing(this, v.Name, Handles.Mob(v.Handle), "· いま", icon: v.Icon);
            _fxStep = 0; _fxT = 0;
            return;
        }
        if (_fxStep < EraseBeats.Length)
        {
            var beat = EraseBeats[_fxStep];
            if (FxBegin())
            {
                if (beat.Text.Length == 0) _toast.Erase();
                else _toast.Type(beat.Text, send: beat.Send);
            }
            // Typing and erasing must not consume the pause after the edit.
            if (!_toast.Done) _fxT = 0;
            else if (_fxT >= beat.Hold) AdvanceFx();
            return;
        }
        // 余韻まで済んだら引かせ、引き終わったら次の会話行へ。
        if (FxBegin()) _toast.Dismiss();
        if (_toast.Gone) NextFx();
    }

    // その手に入った最初のフレームか（＝Type/Erase/Dismiss を一度だけ投げるためのラッチ）。
    private int _fxBegun = -1;
    private bool FxBegin()
    {
        if (_fxBegun == _fxStep) return false;
        _fxBegun = _fxStep; _fxT = 0;
        return true;
    }
    private void AdvanceFx() { _fxStep++; _fxT = 0; }

    // 演出を終えて次の会話行へ。カードは必ず片付ける（次の演出行が前の1枚を拾わない）。
    private void NextFx()
    {
        _toast?.QueueFree();
        _toast = null;
        _fxStep = 0; _fxT = 0; _fxBegun = -1;
        _line++;
        _page = 0; _pagedLine = -1; _reveal = 0; _lineT = 0; _readIdx = -1;
        if (_line >= _talk.Count && !AtChoicePoint) StartGame();
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
                string sent = P3Choices[sel];
                var others = new List<string>();
                for (int i = 0; i < P3Choices.Length; i++) if (i != sel) others.Add(P3Choices[i]);
                if (_game != null) _game.NameRoute = sel;
                _game?.RecordChoice("p3", sent, others, hesitation);
                _talk.Insert(_line, L(WhoYou, sent, ""));
                _talk.InsertRange(_line + 1, P3Reply(sel));
                // 続けて P4（導入 → 選択）。
                var p4 = P4Intro();
                _timelineLine = _talk.Count;
                _unsentLine = _timelineLine + p4.FindIndex(d => d.Text == FxErase);
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
    //   ※練習面の非表示中(GameManager.TutorialEnabled==false)は上記を全部飛ばしてハブへ直行する。
    private bool _started;
    private bool _openingShown;
    private void StartGame()
    {
        if (_started) return;
        if (!_openingShown)
        {
            _openingShown = true;
            _phase = 6;
            AddChild(new OpeningFilm { Completed = () =>
            {
                _zHeld = Pad.AdvanceHeld();
                _backHeld = Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.B);
                _retry.Update(0, false);
                StartGame();
                if (!_started)
                {
                    Audio.Instance?.Music(Audio.Instance.BgmPrologue, 0.8f);
                    QueueRedraw();
                }
            } });
            return;
        }
        // 保険：プロローグを抜ける時点では必ず名前がある（点灯行は全ルートが通るが、
        //   将来この場面を飛ばす導線が出来ても他画面に「？」を持ち出さない）。
        GameManager.MinaNamed = true;
        var g = GetNodeOrNull<GameManager>("/root/Game");
        bool tutorial = GameManager.TutorialEnabled;
        if (!tutorial || g == null || !g.TutorialSeen)
        {
            // 非表示中はハブへ直行。表示中の未受講は、これまでどおり確認を出さずステージ0へ。
            _started = true;
            GameManager.FadeToScene(this, tutorial ? "res://Stage0.tscn" : "res://Hub.tscn");
            return;
        }
        // 受講済み：確認フェーズへ（シーン遷移はそこで決める）。
        _phase = 5;
        _t = 0;
        _askSel = 0;
    }

    private void UpdateBackdrop(double delta)
    {
        int target = _phase < 2 ? 0
            : _timelineLine < 0 || _line < _timelineLine ? 1
            : _line < _unsentLine ? 2 : 3;
        if (target != _backdrop)
        {
            _previousBackdrop = _backdrop;
            _backdrop = target;
            _backdropMix = 0f;
        }
        _backdropMix = Mathf.Min(1f, _backdropMix + (float)delta / 1.2f);
        _choiceShade = Mathf.MoveToward(_choiceShade, _choice != null ? 1f : 0f, (float)delta / 0.3f);
    }

    private void DrawBackdrop()
    {
        float blend = _backdropMix * _backdropMix * (3f - 2f * _backdropMix);
        _backdropArt.Draw(this, new Rect2(0, 0, W, H), _backdrop, _previousBackdrop, blend, (float)_backdropTime);
        if (_choiceShade > 0f)
            DrawRect(new Rect2(0, 0, W, H), new Color(0.02f, 0.02f, 0.035f, _choiceShade * 0.78f));
    }

    public override void _Draw()
    {
        DrawBackdrop();
        Rect2 device = DeviceViewport();
        float frame = DeviceProgress();
        if (frame > 0f)
        {
            var visible = device.Intersection(new Rect2(0, 0, W, H));
            Color shade = new(0.01f, 0.015f, 0.02f, frame * 0.88f);
            DrawRect(new Rect2(0, 0, visible.Position.X, H), shade);
            DrawRect(new Rect2(visible.End.X, 0, W - visible.End.X, H), shade);
            DrawRect(new Rect2(visible.Position.X, 0, visible.Size.X, visible.Position.Y), shade);
            DrawRect(new Rect2(visible.Position.X, visible.End.Y, visible.Size.X, H - visible.End.Y), shade);
            UiKit.Box(this, device.Grow(2.5f), Colors.Transparent, 8f, new Color("101318"), 5f);
            UiKit.Box(this, device.Grow(5f), Colors.Transparent, 8f, new Color(0.65f, 0.7f, 0.75f, frame), 0.8f);
            UiKit.Box(this, new Rect2(device.GetCenter().X - 12f, device.Position.Y + 2f, 24f, 2f), new Color(0.01f, 0.01f, 0.015f, frame), 1f);
            UiKit.Box(this, new Rect2(device.GetCenter().X - 16f, device.End.Y - 4f, 32f, 1f), new Color(0.8f, 0.85f, 0.9f, frame), 0.5f);
        }

        switch (_phase)
        {
            case 0: DrawRain(device); break;
            case 1: DrawIdentity(device); break;
            case 2: DrawIgnite(); break;
            case 3: DrawTalkSpeakers(); DrawTalk(); break;
            case 4: DrawTitle(); break;
            case 5: DrawTutorialAsk(); break;
        }

        // 会話ボックスのボタン列とラッチの印（設計座標・枠と立ち絵より手前）。SKIP はラッチ中か、押しっぱなしの早送り中に点ける。
        UiKit.BeginDesign(this);
        if (TalkBoxShown) _toolbar.Draw(this, ToolbarAnchor, _game?.AutoAdvanceDialog ?? false, Hud.SkipLatched || _ffNow);
        // 会話ボックスが出ていない間も SKIP ラッチが立っていれば画面右上に「▶▶」の印（ラッチは画面を跨いで残る）。
        _toolbar.DrawLatchMark(this, new Vector2(UiKit.DesignW - 16f, 14f));
        UiKit.EndDesign(this);

        // R/Start 長押しリトライの充填チップ（押している間だけ・設計座標で描く）。
        if (_retry.Progress > 0f)
        {
            UiKit.BeginDesign(this);
            Hud.DrawRetryHoldChip(this, _retry.Progress,
                (Pad.ShowKeyboard ? "R" : Pad.Face(JoyButton.Start)) + " 長押しでさいしょから");
            UiKit.EndDesign(this);
        }
    }

    private float DeviceProgress() => _phase switch
    {
        0 => Mathf.SmoothStep(0f, 1f, Mathf.Clamp(((float)_t - 2.3f) / 1.1f, 0f, 1f)),
        1 => 1f,
        2 => 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp((float)_t / 1.2f, 0f, 1f)),
        _ => 0f,
    };

    private Rect2 DeviceViewport()
    {
        float k = DeviceProgress();
        if (k <= 0f) return new Rect2(0, 0, W, H);
        float width = Mathf.Lerp(W + 12f, 140f, k);
        var size = new Vector2(width, width * 192f / 140f);
        return new Rect2((new Vector2(W, H) - size) / 2f, size);
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
    private void DrawRain(Rect2 viewport)
    {
        if (_font == null) return;
        const float lineH = 11f;
        float scroll = (float)_t * 78f;
        float baseBottom = Mathf.Min(H, viewport.End.Y) - 12f;
        for (int i = 0; i < _stream.Count; i++)
        {
            float y = baseBottom + i * lineH - scroll;
            if (y < Mathf.Max(0f, viewport.Position.Y) + 12f || y > Mathf.Min(H, viewport.End.Y) - 8f) continue;
            float a = 0.85f - Mathf.Clamp((H - y) / H, 0f, 1f) * 0.55f; // 上ほど薄く
            DrawString(_font, new Vector2(viewport.Position.X + 10, y), _stream[i], HorizontalAlignment.Left, viewport.Size.X - 20f, 8,
                new Color(Code.R, Code.G, Code.B, a));
        }

        // 可読限界すれすれの一瞬：4行英文を中央にフラッシュ（t≈1.7〜2.2）。
        // ユーザー承認済み: docs/20260914/ストーリー添削_2026-09-14.md 5-(B)
        //   頭文字 M・I・N・A は作中で回収しない（あなたの内面を確定させないため）が、
        //   旧 0.25 秒では実機でまず読めず「仕込みですらない」状態だった＝尺だけ倍にして、
        //   探した人・目の速い人だけが読める境界へ寄せる（Records から読み返す導線は今回は作らない）。
        if (_t >= 1.7 && _t < 2.2)
        {
            for (int k = 0; k < Acrostic.Length; k++)
                DrawString(_font, new Vector2(W / 2f - 150f, 86f + k * 13f), Acrostic[k],
                    HorizontalAlignment.Left, -1, 9, new Color(0.7f, 1f, 0.75f, 0.9f));
        }
    }

    // --- フェーズ1：identity は保留のまま（[ M I N A ] は P3 の命名まで点灯しない）---
    private void DrawIdentity(Rect2 viewport)
    {
        if (_font == null) return;
        bool blink = ((int)(_t * 3f) % 2) == 0;
        DrawString(_font, new Vector2(viewport.Position.X + 10f, 96f), "> assigning identity ...",
            HorizontalAlignment.Left, viewport.Size.X - 20f, 7, new Color(Code.R, Code.G, Code.B, 0.7f));
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

    // --- フェーズ3：話者の立ち絵を中央に表示（行ごとの表情を反映） ---
    private void DrawTalkSpeakers()
    {
        if (_line >= _talk.Count) return;
        if (_talk[_line].Who == WhoFx) return;   // 演出行のあいだは立ち絵も引く（中央のカードに場を譲る）
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

    // 話者ラベルと額縁の色。ミナ＝シアン／あなた＝暖色／投稿＝Ｙ投稿（Hud と同じ Text3）／システム＝コード緑。
    private static (string label, Color col) SpeakerOf(DLine d) => d.Who switch
    {
        WhoMina => (Hud.MinaLabel, Cool),   // 命名前は「？」（GameManager.MinaNamed）
        WhoYou  => ("あなた", Warm),
        WhoPost => ("Ｙ 投稿", UiKit.Text3),
        _       => ("", Code),
    };

    // 会話ログ（Hud.Backlog）へ積む。行の表示開始時に1回（DriveTalk の既読ゲートと同じタイミング＝
    //   選択で挿し込まれた行も、ページ割りされた長い行も、1行につき1回）。話者名と色は SpeakerOf（画面の額縁）と同じ。
    //   システム表示は画面では無名だが、ログでは「システム」と添えて起動ログだと分かるようにする。
    //   命名前のミナは「？」のまま積む＝読み返してもその時点の見え方が残る。演出行（WhoFx）はここへ来ない
    //   （DriveTalk が先に DriveFx へ抜ける）。本文は表示と同じ文字列をそのまま渡す。
    private static void LogLine(DLine d)
    {
        var (label, col) = SpeakerOf(d);
        var kind = d.Who switch
        {
            WhoYou  => Hud.LineKind.Boy,
            WhoMina => Hud.LineKind.Mina,
            WhoPost => Hud.LineKind.Post,
            _       => Hud.LineKind.Narration,
        };
        Hud.PushLog(kind, d.Who == WhoSys ? "システム" : label, d.Text, col);
    }

    // --- フェーズ3：会話ボックス ---
    private void DrawTalk()
    {
        if (_font == null || _line >= _talk.Count) return;
        var d = _talk[_line];
        if (d.Who == WhoFx) return;
        var (label, edge) = SpeakerOf(d);
        UiKit.BeginDesign(this);
        var box = DialogueBox.FullScreen;
        DialogueBox.DrawFrame(this, box, label, edge);
        DialogueBox.DrawBody(this, box, CurPage, Mathf.Clamp((int)_reveal, 0, CurPage.Length),
            ink: d.Who == WhoSys ? Code : DialogueBox.Ink);
        if (_reveal >= CurPage.Length && !_ffNow)
            DialogueBox.DrawContinue(this, box, !LastPage);
        UiKit.EndDesign(this);
    }

    // --- フェーズ4：タイトル ---
    private void DrawTitle()
    {
        if (_font == null) return;
        float a = Mathf.Clamp((float)_t / 1.0f, 0f, 1f);
        DrawString(_font, new Vector2(0, 78f), "Y — タイムライン", HorizontalAlignment.Center, W, UiKit.CutClimax,
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
