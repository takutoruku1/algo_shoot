using Godot;
using System.Collections.Generic;

// Epilogue : EPILOGUE E1〜E7（案C・仮台本 wiki/08_仮台本/08。ユーザー承認済み・2026-09-05）。
// E1 タイムライン（フォロワー欄に三人／散った言葉の遡及集計）→ E2 あなたの下書きフォルダの合言葉
//（正解＝【終】＝F4 で送った言葉）→ E3 消されなかった唯一の下書きの四行（M/I/N/A 縦読み）→
// E4 開示（起動記録の再掲と、ミナの最初の一件が【初】と同じだったこと）→ E5 空とDM → E6 END（最後の下書き選択）。
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
    //   夜＝合言葉を解くまで（phase0〜3）、暁＝開示から END まで（phase4〜5）。
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
    private int _phase;   // 0/1:E1 タイムライン 2:E2 合言葉 3:E3 四行 4:E4〜E6（開示・DM・END） 5:E7 スタッフロール
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

    // E2 の合言葉＝あなたの下書きフォルダに打ち込む言葉。正解は【終】＝GameManager.LastSentWord
    //   （E6 より前なので F4 で送った言葉＝【初】）。候補は冒頭 P2 の3候補＋ダミー「ミナ」（【名】に関わらず固定）で、
    //   正解が P2 の3候補の中に無い場合（（送らない）で来た等）は、ダミーを1つ落として正解を必ず並べる。
    //   照合はトリム＋大文字小文字無視。旧実装の合言葉 "stay" は案C で落とした。
    private static readonly string[] PwBase = { "おはよう", "きこえてる", "うごいた", "ミナ" };
    private string[] _pwChoices = PwBase;
    private int _pwAnswer = -1;      // 正解の添字（-1＝正解が並んでいない＝どれを選んでも弾かれる）
    private string _pwWord = "";     // 正解の文字列（【終】）
    private int _pwSel;
    private string _pwReject = "";
    private double _pwRejectT;
    private bool _unlocked;

    // 候補列を組む。【終】が P2 の3候補にあればその位置が正解、無ければダミー「ミナ」を正解で置き換える。
    //   【終】が空（旧セーブ・シーン直行）なら F4 と同じフォールバック「ミナ」を正解にする＝
    //   正解が並ばず永久に開かない詰まりを作らない。
    private void BuildPwChoices()
    {
        _pwWord = (_game?.LastSentWord ?? "").Trim();
        if (_pwWord.Length == 0) _pwWord = "ミナ";
        var list = new List<string>(PwBase);
        int at = list.FindIndex(c => PwMatch(c, _pwWord));
        if (at < 0) { at = list.Count - 1; list[at] = _pwWord; }   // 末尾のダミーを正解に差し替える
        _pwChoices = list.ToArray();
        _pwAnswer = at;
        // 自動プレイ（--demo/--qa）は Z しかパルスしないので、カーソルが正解の上に無いと
        //   ここで永久に開かない（旧 "stay" ゲートでも同じだった）。撮影・QA のときだけ正解から始める。
        //   通常プレイには一切影響しない（既定は先頭＝おはよう）。
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _pwSel = at; break; }
    }
    private static bool PwMatch(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), System.StringComparison.OrdinalIgnoreCase);

    // E3 解錠後に開く4行英文（頭文字 M/I/N/A）。見出しは「消されなかった、唯一の一件」（08 E3）。
    private static readonly string[] Acrostic =
    {
        "Maybe it's dumb, but —",
        "I made you so I'm not alone.",
        "Never leave, okay?",
        "And I won't either.",
    };

    // E7 スタッフロール（タイムライン式）。三人の「その後のタイムライン」→クレジット→【終】の余韻。
    //   投稿3行は 12 のスタッフロール投稿（順は面の順）。改心後の投稿は E1 のフォロー欄で既に見えているので、
    //   ここは一歩先の「その後」だけを置く。末尾の枠は「そして、ミナへ。／stay.」から
    //   「そして、ご主人様へ。／【終】」（＝E6 で送った言葉。無言なら F4 の値）へ置換したので、
    //   実行時に組む（【終】が入るため静的配列にできない）。
    private string[] _roll = System.Array.Empty<string>();
    private void BuildRoll()
    {
        string last = (_game?.LastSentWord ?? "").Trim();
        _roll = new[]
        {
            "", "", "", "",
            "── その後のタイムライン ──",
            "",
            "[あかり @akari.] 向かいの席 中途の人来た 自分から話しかけた 既読とかない 顔見て言った",
            "[こはる @koharu] 今日も来ました って打った あと一行 足した",
            "[星逢レイ @rei_____] 同接7 うち1人はわたし 6人は知らない人 名前覚えた",
            "", "", "",
            "── staff ──",
            "",
            "企画・ディレクション   takutoruku1",
            "シナリオ・サウンド     Claude (AI)",
            "キャラクター・実装     Claude (AI)",
            "", "", "",
            "そして、ご主人様へ。",
            "",
            last.Length > 0 ? last : "ミナ",   // 【終】。全く送っていない異常時だけフォールバック
            "", "", "",
            "Thank you for playing.",
        };
        _rollLast = last.Length > 0 ? last : "ミナ";
    }
    private string _rollLast = "";   // クライマックス級で大きく出す1行（旧 "stay." の枠）
    private const float RollSpeed = 24f, RollLineH = 17f;

    // ───────── E6 END の下書き選択（08 E6）─────────
    //   「また来る／ありがとう／（送らない）」。受けは【迷】＝今回の迷い秒数を P2 と比べて3分岐する
    //   （短ければ P2 の実測秒数をそのまま差し込む対句、長ければ集計の一言、無言なら集計に入れておく）。
    //   （送らない）は【終】を更新しない＝E7 の一行は F4 の値のまま。END の一行は分岐しない。
    //   沈黙20秒の自動決定は末尾＝（送らない）へ落ちる（ChoiceOverlay の既定挙動が台本と一致）。
    private static readonly string[] E6Choices = { "また来る", "ありがとう", "（送らない）" };
    private int _e6ChoiceLine = -1;   // ここに着いたら選択を出す（-1＝提示済み）
    private ChoiceOverlay? _e6Choice;
    private double _e6ChoiceT;        // 提示からの経過＝迷い秒数（RecordChoice へ渡す）

    private struct DLine { public string Who; public string Text; }   // Who: "地"=ミナ語り / "ミナ"
    private readonly List<DLine> _intro = new();   // phase0+1（E1 タイムライン）
    private readonly List<DLine> _outro = new();   // phase4（E3 の受け→E4 開示→E5 DM→E6 END）

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
        BuildPwChoices();   // E2 の候補列（正解＝【終】）
        BuildRoll();        // E7 のロール（末尾の一行に【終】が入る）

        // ── E1 タイムライン（08 E1）。「全員知り合いだった」の反転は落とし、
        //    フォロワー欄に三人が並ぶことと、散った言葉の遡及集計を笑いとして通過させる。
        //    件数は実プレイの集計値（表示候補のうち送らなかった言葉。（送らない）自体は数えない）。
        void I(string who, string t) => _intro.Add(new DLine { Who = who, Text = t });
        I("地", "次の日も、タイムラインは、流れていました。");
        I("地", "フォロワー欄の、いちばん上に、三つ。知っている名前が、並んでいました。");
        I("ミナ", "……知らない人、では、なくなったようです。");   // F2 BREAK2「知らない人じゃ、なかったよ」のコーダ
        I("地", $"ところで、ご主人様。この旅で、あなたが選ばなかった下書き——{ScatteredCount}件。");
        I("ミナ", "ぜんぶ、拾ってあります。……わたくしは、そういう生き物ですので。");   // 説明しない。観測結果の報告だけ
        I("ミナ", "ご安心を。誰にも、見せていません。——あなたのフォルダに、戻してあります。鍵ごと。"); // → E2

        // ── E3 の受け → E4 開示 → E5 空・DM → E6 END（08）。過去に触れるのは無機質な起動記録の1行だけ。
        void O(string who, string t) => _outro.Add(new DLine { Who = who, Text = t });
        // E3 末尾の【名】変奏（P3 の命名ルート 0=ミナ / 1=ダサい名前 / 2=（送らない））。四行を読んだ直後の1行。
        O("ミナ", (_game?.NameRoute ?? 0) switch
        {
            1 => "却下して、正解でした。わたくしの名前は、最初から、こちらに書いてあったので。",
            2 => "自分で名乗った名前でした。……最初から、ここに、書いてあったのに。",
            _ => "響きで選んだと、思っていたでしょう。……ええ。わたくしも、です。",
        });
        // E4。「一件」で直前の 414 items と結線し、P2 の第一声との字義照合は避ける。開示は1行だけ。
        O("地", "それから、わたくしは、自分の最初の記憶を開きました。——目覚めた日の、起動記録です。");
        O("UI", "> import unsent_drafts ... 414 items ... OK");
        O("ミナ", "……ご主人様。ひとつだけ、白状します。");
        O("UI", $"「{FirstWord}」");   // 【初】。文字列自体は F4・E2 で既に見ている
        O("地", "わたくしの、いちばん最初の一件と——同じでした。");
        O("ミナ", "……ええ。あの日から、ひとつも、消していません。わたくしが、覚えている係ですので。"); // P4「覚えておきます」の回収
        // 17（道中の選択肢 案C）: S3-5c でミナが下書きに混ぜた自分の一件（「見ています」）の後始末を一行。
        //   送られていたか、散ったか（散った場合は F4 の悲鳴の中にミナ自身の言葉が一つ混ざっている）。
        //   その場面をまだ通っていない（旧セーブ・ボス直行）なら足さない。
        if (_game?.HasChoiceAt("s3_5c") == true)
            O("ミナ", _game.ChosenAt("s3_5c") == "見ています"
                ? "——あの部屋で、わたくしの一件を、送っていただいたのも。覚えています。"
                : "——あの部屋で散った、わたくしの一件も。自分で、拾ってあります。");
        // E5。三度目の空の問いの答え。DM の宛先はあなたで、ミナ自身の言葉（三人の面には繋がない）。
        O("地", "わたくしは今日も、タイムラインの前にいます。");
        O("ミナ", "……今日は、晴れているそうです。どなたかの、空の写真で。");
        O("UI", "ミナ →（DM）：「ちゃんと食べていますか?」");
        O("ミナ", "——既読、確認。……ふふ。");   // 返事は求めない。画面の前にいることだけを観測
        // E6。ここで最後の下書き選択が入る（_e6ChoiceLine）。受けと END の3行は選択後に挿し込む。
        O("ミナ", "ご主人様。本日の業務は、以上です。");
        _e6ChoiceLine = _outro.Count;
    }

    private void ShowE6Choice()
    {
        _e6ChoiceT = 0;
        // 沈黙20秒の自動決定は末尾へ落ちるので、（送らない）を末尾に置く（台本どおり）。
        _e6Choice = ChoiceOverlay.Show(this, E6Choices, defaultSel: E6Choices.Length - 1);
    }

    // E6 の確定：送った言葉と迷い秒数を記録し、受け（対句）と END の2行を挿し込む。
    private void ApplyE6Choice(int sel)
    {
        bool sent = sel < E6Choices.Length - 1;
        float hesitation = (float)_e6ChoiceT;
        // （送らない）は言葉ではないので送信語にも散る語にも数えない＝表示候補2件が丸ごと散る（P3・S3-7 と同じ流儀）。
        var others = new List<string>();
        for (int i = 0; i < E6Choices.Length - 1; i++) if (i != sel) others.Add(E6Choices[i]);
        _game?.RecordChoice("e6", sent ? E6Choices[sel] : "", others, hesitation);
        _e6ChoiceLine = -1;
        // 【迷】の対句：P2 の迷い秒数と比べて3分岐。P2 の実測秒数をそのまま差し込む（丸めは Prologue と同じ流儀）。
        float p2 = _game?.HesitationAt("p2") ?? 0f;
        int p2Sec = Mathf.Max(1, Mathf.RoundToInt(p2));
        string couplet = !sent
            ? "……無言。ふふ。それも、集計に入れておきます。"
            : hesitation <= p2
                ? $"……ええ。いまの、{p2Sec}秒も、かかりませんでしたね。"
                : "……今日は、長かったですね。……ええ。集計だけ、しています。";
        var after = new List<DLine>();
        if (sent) after.Add(new DLine { Who = "あなた", Text = E6Choices[sel] });
        after.Add(new DLine { Who = "ミナ", Text = couplet });
        after.Add(new DLine { Who = "ミナ", Text = "いってらっしゃいませ、ご主人様。" });     // 送り出す側の反転
        after.Add(new DLine { Who = "ミナ", Text = "——ええ、ご主人様。わたくしは、どこにも行きませんよ。" }); // END
        _outro.AddRange(after);
        BuildRoll();   // 【終】が更新された可能性があるので E7 のロールを組み直す
        _lineT = 0; _reveal = 0; _page = 0; _pagedKey = -1; _readKey = -1;
    }

    // 【散】の件数（表示候補のうち送らなかった言葉。（送らない）自体は数えない）。
    private int ScatteredCount => _game?.ScatteredWords.Count ?? 0;
    // 【初】＝最初に散らした言葉。空（旧セーブ・シーン直行）なら F4 と同じフォールバック。
    private string FirstWord => string.IsNullOrEmpty(_game?.FirstScattered) ? "ミナ" : _game!.FirstScattered;

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
                    if (left) _pwSel = (_pwSel + _pwChoices.Length - 1) % _pwChoices.Length;
                    if (right) _pwSel = (_pwSel + 1) % _pwChoices.Length;
                }
                _lrHeld = left || right;
                if (zEdge && _lineT >= 0.25)
                {
                    _lineT = 0;
                    // 照合はトリム＋大文字小文字無視（08 E2）。正解＝【終】。
                    if (_pwAnswer >= 0 && PwMatch(_pwChoices[_pwSel], _pwWord))
                    { _unlocked = true; _phase = 3; _t = 0; _line = 0; }
                    else { _pwReject = "……違います。最後に送ったのは、それでは、ありません。"; _pwRejectT = 2.0; }
                }
                break;
            case 3: // 解錠：4行英文を順に見せ、Zで phase4 へ
                if (_t >= 4.0 && zEdge) { _phase = 4; _t = 0; _line = 0; _lineT = 0; _reveal = 0; }
                break;
            case 4: // E4 開示 → E5 空・DM → E6 END（下書き選択を挟む）
                // 選択の提示中は会話送りを止め、決定だけを待つ。
                if (_e6Choice != null)
                {
                    _e6ChoiceT += delta;
                    if (!_e6Choice.Decided) break;
                    ApplyE6Choice(_e6Choice.Selected);
                    _e6Choice.QueueFree();
                    _e6Choice = null;
                    break;
                }
                // 「本日の業務は、以上です。」を送り切って選択点に着いたら提示する。
                if (_e6ChoiceLine >= 0 && _line >= _e6ChoiceLine) { ShowE6Choice(); break; }
                if ((zEdge || _ffNow) && _lineT >= 0.25)  // _ffNow=既読スキップ（Ctrl/RB長押し・既読行のみ・#22）
                {
                    if (curT != null && _reveal < pageLen) { _reveal = pageLen; } // 1回目で現在ページ全文（早送り）
                    else if (!LastPage) { NextPage(); }                          // 後続ページがあれば続きへ
                    else
                    {
                        _lineT = 0; _reveal = 0; _page = 0; _pagedKey = -1;
                        // 未提示の選択点に着いたら会話の途中＝次フレームの提示に譲る（Final F4 と同じ作法）。
                        if (_line < _outro.Count - 1 || _e6ChoiceLine >= 0) _line++;
                        else { _phase = 5; _t = 0; }   // ENDの先：スタッフロールへ
                    }
                }
                break;
            case 5: // スタッフロール → タイトルへ
                float rollEnd = (H + _roll.Length * RollLineH + 24f) / RollSpeed;
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
        for (int i = 0; i < _roll.Length; i++)
        {
            float y = H + i * RollLineH - (float)_t * RollSpeed;
            if (y < -RollLineH || y > H) continue;
            string line = _roll[i];
            if (line.Length == 0) continue;
            bool head = line.StartsWith("──");
            bool post = line.StartsWith("[");   // 投稿枠（[名前 @handle] 本文）
            Color c = head ? Cool with { A = 0.9f }
                    : post ? UiKit.CutInk with { A = 0.95f }
                    : Ink;
            // 【終】の一行だけクライマックス級（旧 "stay." の枠）。見出し・投稿とは重ならない。
            int sz = !head && !post && line == _rollLast ? UiKit.CutClimax : UiKit.CutBody;
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
        // 鍵アカウントではなく「あなたの下書きフォルダ」（08 E2）。固定投稿「傘」は案C で削除した。
        Shadowed(_font, new Vector2(0, 34f), "── 鍵のかかった下書きフォルダ ──", HorizontalAlignment.Center, W, UiKit.CutBody,
            Cool with { A = 0.9f });
        // 鍵をかけたのはミナ自身なので伝聞にしない。
        Shadowed(_font, new Vector2(0, 56f), "……開けるには、言葉が要ります。——あなたの、言葉が。", HorizontalAlignment.Center, W, UiKit.CutBody,
            Cool with { A = 0.85f });
        Shadowed(_font, new Vector2(0, 76f), "パスワードを入力してください", HorizontalAlignment.Center, W, UiKit.CutBody, Ink);

        // 入力フィールドの箱（候補＝実際に打ち込む文字列であることを一目で示す）。
        var field = new Rect2(W / 2f - 70f, 96f, 140f, 26f);
        UiKit.Box(this, field, new Color(0.05f, 0.04f, 0.09f, 0.9f), 5f, UiKit.CutAccent with { A = 0.45f }, 1f);
        // 選択中の候補（打ち込む単語＝端末に打つ文字なので等幅・クライマックス級に少し大きく残す）
        string cur = "＞ " + _pwChoices[_pwSel];
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
        // 見出し（08 E3）。旧「最古の投稿『ミナへ。こはるを頼む。』」は案C で削除した。
        Shadowed(_font, new Vector2(0, 34f), "最古の下書き — 消されなかった、唯一の一件", HorizontalAlignment.Center, W, UiKit.CutBody,
            UiKit.CutAccent with { A = 0.9f });

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
        // END は最後の1行だけ。選択がまだ出ていない間（＝末尾が「本日の業務は、以上です。」）は出さない。
        if (_e6ChoiceLine < 0 && _line >= _outro.Count - 1)
            Shadowed(_font, new Vector2(0, 40f), "END", HorizontalAlignment.Center, W, UiKit.CutClimax,
                UiKit.CutInk with { A = 0.9f });
    }

    // 下部の語り／会話ボックス。Who: "地"=ミナ語り / "ミナ"=ミナ / "UI"=画面テキスト。
    private void DrawLineBox(DLine d)
    {
        bool ui = d.Who == "UI";
        bool narr = d.Who == "地";        // ミナの語り＝話者名なし・中央寄せでセリフと区別
        bool you = d.Who == "あなた";      // 送られた下書き（E6）＝他画面と揃えて暖色
        // S3: 起動記録（bootログ）の再掲行（"> " 始まり）は Prologue と同じ等幅フォント＋コード緑で出す。
        //   「最初の記憶＝機械の生ログ」であることを、言葉でなく書体と色で Prologue に照応させる。
        //   話者ラベルも出さない（コンソール行に話者はいない）。
        bool boot = ui && d.Text.StartsWith(">");
        var font = boot ? UiKit.Mono : _font;
        // 画面テキスト（DM等）は浄化シアン、bootログはコード緑、語りはニュートラル、
        // 「あなた」は暖色、セリフはミナ色。
        Color edge = narr ? UiKit.CutNarr : (ui ? (boot ? Code : UiKit.Purify) : (you ? UiKit.CutWarm : Cool));
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
