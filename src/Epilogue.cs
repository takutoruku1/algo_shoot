using Godot;
using System.Collections.Generic;

public partial class Epilogue : Node2D
{
    private const float W = 384f, H = 216f;

    private const int PhGaze = 0, PhFilm = 1, PhRoll = 2, PhEnd = 3;
    private EndingFilm? _film;

    private FontFile _font = null!;
    private double _t;
    private int _phase = PhGaze;
    private bool _zHeld;
    private readonly RetryHold _retry = new(); // R/Start 長押しで最初から/タイトルへ（即発の誤爆防止）
    private int _line;
    private double _lineT;
    private double _reveal;        // タイプライター表示済み文字数（＝現在ページ内）
    private GameManager? _game;    // 文字送り速度（MsgCharsPerSec）を本編設定と共有
    private bool _musicStarted;    // E5b のオルゴールを実際に鳴らしたか（未調達なら false＝停止も呼ばない）

    // 撮影モード（--shot）か。スタッフロールのスキップを止めるためだけに見る（ChoiceOverlay と同じ作法）。
    private static bool ShotHold
    {
        get
        {
            if (_shotHoldChecked) return _shotHold;
            _shotHoldChecked = true;
            foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--shot") { _shotHold = true; break; }
            return _shotHold;
        }
    }
    private static bool _shotHold, _shotHoldChecked;

    // テキストボックスは2行固定。2行超の行はページに割り、送り（Z）で続きを読ませる（本文は削らない）。
    //   会話フェーズ（PhGaze/PhEnd）が対象。折り返しは DrawLineBox と一致させる。
    private readonly List<string> _pages = new();
    private int _page;
    private int _pagedKey = -1;                // _pages を構築済みの行キー（phase×1000+line）
    private string CurPage => _pages.Count > 0 ? _pages[Mathf.Min(_page, _pages.Count - 1)] : "";
    private bool LastPage => _pages.Count == 0 || _page >= _pages.Count - 1;
    // 現在行のページを（未構築なら）作る。折り返しは DrawLineBox と同じ書体・同じ幅で引く。
    private void EnsurePages()
    {
        string? t = CurLineText();
        if (t == null) { _pages.Clear(); _page = 0; _pagedKey = -1; return; }
        int key = _phase * 1000 + _line;
        if (_pagedKey == key) return;
        _pagedKey = key; _page = 0;
        _pages.Clear();
        _pages.AddRange(UiKit.Paginate(DialogueBox.Body, t, DialogueBox.WrapWidth(DialogueBox.FullScreen), Hud.DlgMaxLines));
    }
    private void NextPage() { _page++; _reveal = 0; _lineT = 0; }

    // 既読スキップ（#22）：Ctrl/RB 長押しで「既読の行だけ」高速送り（本編HUDと同じ作法・独自レンダラ側の実装）。
    // スタッフロール(PhRoll)は対象外（CurLineText が null＝会話行フェーズのみ効く）。
    private int _readKey = -1;     // 既読チェック済みの行キー（phase×1000+line。フェーズ跨ぎの index 重複を区別）
    private bool _lineWasRead;     // 現在行が「表示開始時点で」既読だったか
    private bool _ffNow;           // いま高速送り中か（ボタン列の SKIP 点灯用）

    private readonly DialogToolbar _toolbar = new();
    private static readonly Vector2 ToolbarAnchor = DialogueBox.Anchor(DialogueBox.FullScreen);
    // AUTO（GameManager.AutoAdvanceDialog）：現在ページの全文表示後、この秒数で次へ。
    //   E6 の選択と、最後の END の一行（送るとタイトルへ抜ける）は対象外＝そこは自分の手で。
    private const double AutoAfterReveal = 1.0;
    private double _autoT;         // 現在ページを全文表示してからの経過（AUTO 用）
    // 会話ボックスが出ているか（DrawNarration／DrawEnd が枠を描く条件と同じ）＝ボタン列を出す／受け付ける条件。
    private bool TalkBoxShown => (_phase == PhGaze || _phase == PhEnd) && _e6Choice == null && CurLineText() != null;

    // 配色は UiKit のカットシーントークンへ集約（3画面で同値のコピーだったものを参照に置換）。
    private static readonly Color Cool = UiKit.CutMina;   // ミナ
    private static readonly Color Ink  = UiKit.CutInk;

    // E7 スタッフロール（タイムライン式）。三人の「その後のタイムライン」→クレジット→【終】の余韻。
    //   投稿3行は 12 のスタッフロール投稿（順は面の順）。末尾の枠は「そして、ご主人様へ。／【終】」
    //   F4 で送らなかった場合は、ミナが持ち帰った言葉を返す。送信履歴は変えない。
    //   実行時に組む（【終】が入るため静的配列にできない）。
    private string[] _roll = System.Array.Empty<string>();
    private void BuildRoll()
    {
        string last = (_game?.ChosenAt("f4") ?? "").Trim();
        _roll = new[]
        {
            "", "", "", "",
            "── その後のタイムライン ──",
            "",
            $"[あかり {Handles.Akari}] 向かいの席 中途の人来た 自分から話しかけた 既読とかない 顔見て言った",
            $"[こはる {Handles.Koharu}] 今日も来ました って打った あと一行 足した",
            $"[星逢レイ {Handles.Rei}] 同接7 うち1人はわたし 6人は知らない人 名前覚えた",
            "", "", "",
            "── staff ──",
            "",
            "企画・ディレクション\t こくとう",
            "ゲームデザイン\t こくとう",
            "",
            "── scenario ──",
            "",
            "シナリオ・脚本",
            "世界観・キャラクター設定",
            "投稿テキスト・小話\t Claude (AI)",
            "",
            "── programming ──",
            "",
            "ゲームシステム・自機・敵・ボス",
            "UI／HUD・カットシーン・エフェクト",
            "シェーダー・背景・オーディオ実装",
            "\t Claude (AI) ／ Codex (AI)",
            "",
            "── art ──",
            "",
            "キャラクターデザイン・立ち絵・表情差分",
            "敵／ボスアート・カットイン・背景",
            "タイトルビジュアル・弾／エフェクト素材",
            "\t Claude (AI) ／ Codex (AI)",
            "画像生成\t OpenAI gpt-image-2",
            "",
            "── sound ──",
            "",
            "効果音（手続き合成）・音響調整・楽曲選定",
            "\t Claude (AI)",
            "",
            "── music ──",
            "",
            "甘茶（甘茶の音楽工房）",
            "蒲鉾さちこ ／ もっぴーさうんど ／ のる ／ シンシンワダ（DOVA-SYNDROME）",
            "watson（BGM:MusMus）",
            "PeriTune（CC BY 4.0）",
            "",
            "── quality assurance ──",
            "",
            "自動プレイ検証・バグ検証\t Claude (AI)",
            "プレイテスト\t MAGOME GAMES",
            "\t こくとう",
            "",
            "── special thanks ──",
            "",
            "Godot Engine（MIT License）",
            "Zen Kaku Gothic New ／ JetBrains Mono（OFL 1.1）",
            "プレイしてくださった、あなた",
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
    // 24→20px/秒。投稿3行は1行30字超で情報量が多く、24（約0.71秒/行）では読み切れない（収録時の所見）。
    //   rollEnd は RollSpeed から引くので、ロール全体は行数から自動で伸びる（現在 約61秒）。
    private const float RollSpeed = 20f, RollLineH = 17f;
    // 「職種\t担当者」行の欄の境（設計座標 X）。左に職種（右寄せ）、右に担当者（左寄せ）。
    //   最長の職種「タイトルビジュアル・弾／エフェクト素材」と最長の担当「Claude (AI) ／ Codex (AI)」が
    //   どちらも W=384 に収まる位置。左右どちらかに寄せると片側がはみ出すので中央よりやや左に置く。
    //   RollGap＝欄の間の空き。1文字ぶんの空白だと職種と名前が地続きの一文に読めたので広めに取る。
    private const float RollGutter = 196f, RollGap = 10f;

    // E7 スタッフロールのスキップ（長押し）。ここは作品の締めくくり
    //   （「そして、ご主人様へ。」→ 送った言葉 →「Thank you for playing.」）が末尾に来るので、
    //   単押しで飛ばすと直前まで会話送りを連打してきた流れのまま、ほぼ確実に失われていた。
    //   → EndingFilm のスキップと同じ作法（長押し＋充填バー、離すまで武装しない）に寄せる。
    //   長押し秒は RetryHold.HoldTime（0.45s）＝本作の長押し標準に合わせる。
    private readonly RetryHold _rollSkip = new();
    private bool _rollSkipArmed;     // 一度離すまで長押しを受けない（映画スキップの押しっぱなしを引き継がない）

    private static readonly string[] E6Choices = { "また会いに来る。次は、何でもない話をしよう。", "ミナに会えてよかった。もう、ひとりじゃない。" };
    private int _e6ChoiceLine = -1;   // ここに着いたら選択を出す（-1＝提示済み）
    private ChoiceOverlay? _e6Choice;
    private double _e6ChoiceT;        // 提示からの経過＝迷い秒数（RecordChoice へ渡す）

    private struct DLine { public string Who; public string Text; }   // Who: "地"=語り / "ミナ" / 三人の名 / "あなた"
    private readonly List<DLine> _gaze = new();   // PhGaze（E5b 前半・見上げる10行）
    private readonly List<DLine> _end  = new();   // PhEnd （E6 END）

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Linear;
        _font = UiKit.Zen; // 非ピクセル（滑らかゴシック）
        // E5b はオルゴール（未調達＝null なら無音のまま）。主題（BgmMenu）が戻るのは E6 の1行目。
        //   MusicOnce＝1周で無音に落ちる再生。台本の「…………。」の行では下で明示的に止める。
        if (Audio.Instance != null && Audio.Instance.BgmEpilogueWalk != null)
        {
            Audio.Instance.MusicOnce(Audio.Instance.BgmEpilogueWalk, 2.0f);
            _musicStarted = true;
        }
        BuildSky();
        _zHeld = Pad.AdvanceHeld();
        _game = GetNodeOrNull<GameManager>("/root/Game");
        BuildRoll();        // E7 のロール（末尾の一行に【終】が入る）

        // ── E5b 夜空を共有し、一人ずつ回線をつなぐ ──
        // ユーザー承認済み: docs/20260914/ストーリー添削_2026-09-14.md 【5】
        //   "地" は話者名なしで描かれる＝見た目は第三者の地の文だが、中身は「わたくしも」「わたくしたちは」と
        //   一人称が混じっており、実質ミナの語りだった（＝中身はミナなのに表示だけ第三者という中途半端）。
        //   本作に第三者の語り手はいない（wiki/03_ストーリー/08_伏線と回収.md も明記）ので "ミナ" に寄せる。
        //   いちばん静かな場面で「誰が語っているか」が揺れないようにする。
        void G(string who, string t) => _gaze.Add(new DLine { Who = who, Text = t });
        G("ミナ", "今夜は、こちらを。わたくしの目で見た空です。");
        G("あなた", "こんなふうに見えてたんだ。僕の観測画面より、ずっと明るい。");
        G("ミナ", "全部を数えるのは、やめました。今は、この一つを見ていただきたいので。");
        G("あなた", "うん。……きれいだね。");
        G("地", "あかりとの回線");
        G("あかり", "ミナ、帰ってきたね。……あなたも、お疲れさま。");
        G("あなた", "ありがとう。あかりさんの声も、心強かった。");
        G("あかり", "次は、何でもない話で呼ぶね。返事は、すぐじゃなくてもいいから。");
        G("地", "こはるとの回線");
        G("こはる", "ね、次はさ、なんでもない昼休みの話でもいい？　何も解決してないやつ。");
        G("あなた", "そういうのも聞きたい。今日の抹茶オレの話でも。");
        G("こはる", "それ、長くなるよ？　銘柄ごとに、けっこう違うんだから。");
        G("地", "レイとの回線");
        G("レイ", "あんたも、休みなさい。案内役が寝不足じゃ、こっちも落ち着かないわ。");
        G("あなた", "分かった。次の本の話までには、ちゃんと寝ておく。");
        G("レイ", "よろしい。予習はいらない。感想は、あんたの言葉でね。");
        G("地", "ミナとの回線");
        G("ミナ", "皆さま、ご主人様に言いたいことが増えたようですね。");
        G("あなた", "うれしいけど、寝るようにって、二人から言われたよ。");
        G("ミナ", "もう一度申し上げます。寝てください。これで三件です。");
        G("あなた", "あと、少しだけ。君の空を見てたい。");
        G("ミナ", "……はい。では、少しだけ。わたくしも、そうしたかったので。");
        _end.AddRange(new List<DLine>
        {
            new() { Who = "ミナ", Text = "ご主人様。本日の業務は、以上です。" },
        });
        _e6ChoiceLine = _end.Count;   // ここに着いたら最後の選択を出す
    }

    private void ShowE6Choice()
    {
        _e6ChoiceT = 0;
        // カットシーン＝盤面が無いので onBoard は既定(false)＝画面全体の中心へ。
        _e6Choice = ChoiceOverlay.Show(this, E6Choices, defaultSel: E6Choices.Length - 1, cinematic: true);
    }

    // E6 の確定：送った言葉と迷い秒数を記録し、受け（対句）と END の2行を挿し込む。
    private void ApplyE6Choice(int sel)
    {
        ChoiceEffects.Record(_game, "e6", E6Choices, sel, (float)_e6ChoiceT);
        _e6ChoiceLine = -1;
        _end.Add(new DLine { Who = "あなた", Text = E6Choices[sel] });
        _end.AddRange(sel == 0 ? new List<DLine>
        {
            new() { Who = "ミナ", Text = "はい。うまくいかなかった日も、何もなかった日も。聞かせてください。" },
        } : new List<DLine>
        {
            new() { Who = "ミナ", Text = "わたくしも、お会いできてよかった。……こちらにも、お話しする相手がおりますので。" },
        });
        _end.AddRange(new List<DLine>
        {
            new() { Who = "あなた", Text = "最初より、ずいぶん普通に話せるようになったね。" },
            new() { Who = "ミナ", Text = "最初は、起動しただけで疑われましたから。" },
        });
        _end.AddRange(_game?.NameRoute == 1 ? new List<DLine>
        {
            new() { Who = "ミナ", Text = "それから、あの長い肩書き。今でも却下です。続編で採用する予定もありません。" },
            new() { Who = "あなた", Text = "ミナがいいよ。僕も、今はそう思ってる。" },
        } : new List<DLine>
        {
            new() { Who = "ミナ", Text = "37番目だから、とつけられた名前。……今では、自分の名前だと思っています。" },
            new() { Who = "あなた", Text = "うん。呼ぶたびに、君の顔が浮かぶよ。" },
        });
        _end.AddRange(new List<DLine>
        {
            new() { Who = "ミナ", Text = "では、いってらっしゃいませ。ご主人様。" },
            new() { Who = "あなた", Text = "またね、ミナ。" },
            new() { Who = "ミナ", Text = "はい。また。——ええ、わたくしは、どこにも行きませんよ。" },
        });
        _lineT = 0; _reveal = 0; _page = 0; _pagedKey = -1; _readKey = -1;
    }

    public override void _Process(double delta)
    {
        // ボタン列は Pad.AdvanceHeld を読む前に回す（ボタン上のクリックを会話送りに数えない）。ムービー中も
        //   毎フレーム回す（SKIP ラッチは会話が途切れても切らない＝右上の印のため）。パッド Start はここでは短押し＝MENU／長押し＝最初から。
        _toolbar.Tick(this, delta, TalkBoxShown, ToolbarAnchor, unreadLine: !_lineWasRead, startTapOpensMenu: true);
        if (_phase == PhFilm) { _zHeld = Pad.AdvanceHeld(); return; }
        _t += delta;
        _lineT += delta;
        // 会話送り／各フェーズの決定：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも進める共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        // ポーズメニュー／会話ログを閉じた Z の同じ押下を、会話送りとして二重に拾わない（Pad.UiBlocked。2026-09-27）。
        bool zEdge = z && !_zHeld && !Pad.UiBlocked(this);
        _zHeld = z;

        // R / Start 長押し(0.45s)：スタッフロール以降は「タイトルへ」、それ以前は最初から(Prologue)
        // ＝演出のやり直し（即発は誤爆で読み進みを失いやすい→長押し化。ポーズメニューはカットシーンでは
        //   Esc／M だけで開き、パッドの Start は読まない＝ここで Start を使える。2026-09-27）。
        if (_retry.Update(delta, Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start)))
        {
            GameManager.FadeToScene(this, _phase >= PhRoll ? "res://TitleMenu.tscn" : "res://Prologue.tscn");
            return;
        }

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
            // 会話ログ（L / Tab で開く Backlog）へ、表示を始めた行を積む（2026-09-26）。curT != null＝行フェーズ。
            LogLine(_phase == PhGaze ? _gaze[_line] : _end[_line]);
        }
        _ffNow = curT != null && Hud.SkipHeld && _lineWasRead; // 未読行では効かない
        // AUTO（会話ボックスの AUTO ボタン／設定の「オート会話送り」）：現在ページの全文表示後 AutoAfterReveal 秒で送る。
        bool lastEndLine = _phase == PhEnd && _e6ChoiceLine < 0 && _line >= _end.Count - 1;
        if (curT != null && !lastEndLine && _reveal >= pageLen && (_game?.AutoAdvanceDialog ?? false)) _autoT += delta;
        else _autoT = 0;
        bool autoGo = _autoT >= AutoAfterReveal;

        switch (_phase)
        {
            case PhGaze:   // E5b 見上げる（夜）
                if ((zEdge || _ffNow || autoGo) && _lineT >= 0.25)  // _ffNow=既読スキップ（Ctrl/RB長押し・既読行のみ・#22）
                {
                    _autoT = 0;
                    if (curT != null && _reveal < pageLen) { _reveal = pageLen; } // 1回目で現在ページ全文（早送り）
                    else if (!LastPage) { NextPage(); }                          // 後続ページがあれば続きへ
                    else
                    {
                        _lineT = 0; _reveal = 0; _line++; _page = 0; _pagedKey = -1;
                        // 台本の「…………。」を送り切ったところで曲を完全停止（無音）。残り2行は無音のまま。
                        //   曲が未調達で最初から鳴っていないときは呼ばない（Music() が空の Tween を作る）。
                        //   フェードは 0.9 秒＝ぶつ切りにせず、しかし次の行を読み始める前に無音へ着く長さ
                        //   （行送りの最短間隔 0.25 秒＋次行のタイプ時間より短く収まる）。
                        //   StopMusicOnce＝MusicOnce の Finished フックを解いてから止める（曲尾まで
                        //   行かない停止なので OneShot が残る。Audio.StopMusicOnce のコメント参照）。
                        if (_line == SilenceLine && _musicStarted) { Audio.Instance?.StopMusicOnce(0.9f); _musicStarted = false; }
                        if (_line >= _gaze.Count) StartFilm();
                    }
                }
                break;
            case PhRoll:   // E7 スタッフロール → E6 END
                float rollEnd = (H + _roll.Length * RollLineH + 24f) / RollSpeed;   // ≒35秒（RollSpeed=20）
                // スキップは**長押し**（単押しでは飛ばさない）。ロール末尾の三要素が締めくくりなので、
                //   会話送りの連打がそのままロール飛ばしにならないようにする。飛ばしたい人は押し続ければ飛ぶ。
                //   離すまで武装しない＝直前の EndingFilm を送りっぱなしで抜けてきても即発しない。
                // --shot（撮影）のときだけ完全に受け付けない。自動プレイは Z を常時パルス／保持するので、
                //   受け付けるとロールの実画面が撮れない（ChoiceOverlay の ShotHoldGate と同じ作法）。
                if (!z) _rollSkipArmed = true;
                bool rollSkip = _rollSkip.Update(delta, !ShotHold && _rollSkipArmed && z);
                if (_t >= rollEnd || rollSkip)
                {
                    _phase = PhEnd; _t = 0; _line = 0; _lineT = 0; _reveal = 0; _pagedKey = -1;
                    // 主題（温かいメニューBGM）が戻る。終わりの余韻に主題が戻って一周する。
                    if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu, 2.0f);
                }
                break;
            case PhEnd:    // E6 END（最後の下書き選択 → 受け → END → タイトルへ）
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
                if ((zEdge || _ffNow || autoGo) && _lineT >= 0.25)
                {
                    _autoT = 0;
                    if (curT != null && _reveal < pageLen) { _reveal = pageLen; }
                    else if (!LastPage) { NextPage(); }
                    else
                    {
                        _lineT = 0; _reveal = 0; _page = 0; _pagedKey = -1;
                        // 未提示の選択点に着いたら会話の途中＝次フレームの提示に譲る（Final F4 と同じ作法）。
                        if (_line < _end.Count - 1 || _e6ChoiceLine >= 0) _line++;
                        else { GameManager.FadeToScene(this, "res://TitleMenu.tscn"); return; }
                    }
                }
                break;
        }
        QueueRedraw();
    }

    // 台本で「…………。」＝曲を完全停止する行（見上げ8行目）を送り切った直後の index。
    //   行の追加で番号がずれないよう実行時に引く。見つからなければ -1（＝停止しない）。
    private int SilenceLine
    {
        get { int i = _gaze.FindIndex(d => d.Text == "…………。"); return i < 0 ? -1 : i + 1; }
    }

    private void StartFilm()
    {
        if (_musicStarted) { Audio.Instance?.StopMusicOnce(0); _musicStarted = false; }
        _phase = PhFilm;
        _t = 0;
        _line = 0;
        _film = new EndingFilm { Completed = () =>
        {
            _film = null;
            _phase = PhRoll;
            _t = 0;
            _zHeld = Pad.AdvanceHeld();
            QueueRedraw();
        }};
        AddChild(_film);
    }

    public override void _ExitTree()
    {
        if (_musicStarted) Audio.Instance?.StopMusicOnce(0.3f);
    }

    private Texture2D _skyDawn = null!, _rest = null!, _goodbye = null!;
    private readonly Dictionary<string, Texture2D> _callArt = new();

    private void BuildSky()
    {
        const string dir = "res://char/bg2/ending/";
        _skyDawn = GD.Load<Texture2D>(dir + "bg_ep_dawn_v2.png");
        _rest = GD.Load<Texture2D>(dir + "cg_ep_mina_sky_v3.png");
        _goodbye = GD.Load<Texture2D>(dir + "cg_ep_goodbye_v2.png");
        _callArt["ミナ"] = _rest;
        _callArt["あかり"] = GD.Load<Texture2D>("res://char/bg2/story/cg_akari_playable_aftermath_v1.png");
        _callArt["こはる"] = GD.Load<Texture2D>("res://char/bg2/story/cg_koharu_playable_aftermath_v1.png");
        _callArt["レイ"] = GD.Load<Texture2D>("res://char/bg2/story/cg_rei_playable_aftermath_v1.png");
    }

    private string GazePartner
    {
        get
        {
            for (int i = _line; i >= 0; i--)
                if (_gaze[i].Who == "地")
                    return _gaze[i].Text switch
                    {
                        "あかりとの回線" => "あかり",
                        "こはるとの回線" => "こはる",
                        "レイとの回線" => "レイ",
                        _ => "ミナ",
                    };
            return "ミナ";
        }
    }

    private void DrawArt(Texture2D texture, float alpha = 1f, float zoom = 1f)
    {
        Vector2 viewport = new(W, H);
        Vector2 size = texture.GetSize();
        size *= Mathf.Max(W / size.X, H / size.Y) * zoom;
        DrawTextureRect(texture, new Rect2((viewport - size) * 0.5f, size), false, new Color(1f, 1f, 1f, alpha));
    }

    public override void _Draw()
    {
        if (_phase == PhGaze)
            DrawArt(_callArt[GazePartner], zoom: 1f + 0.025f * (1f - Mathf.Exp(-(float)_t / 22f)));
        else DrawArt(_skyDawn);

        switch (_phase)
        {
            case PhGaze:
                DrawNarration(_gaze, _line);
                break;
            case PhRoll: DrawStaffroll(); break;
            case PhEnd:
                DrawArt(_goodbye);
                DrawEnd();
                break;
        }

        // 会話ボックスのボタン列とラッチの印（設計座標・枠より手前）。SKIP はラッチ中か、押しっぱなしの早送り中に点ける。
        UiKit.BeginDesign(this);
        if (_phase == PhGaze || _phase == PhEnd)
        {
            string partner = _phase == PhGaze ? GazePartner : "ミナ";
            var accent = EdgeFor(partner);
            DrawRect(new Rect2(42, 30, 270, 42), new Color(0.025f, 0.03f, 0.045f, 0.86f));
            DrawLine(new Vector2(42, 30), new Vector2(42, 72), accent, 2);
            UiKit.Text(this, UiKit.Zen, new Vector2(58, 40), partner + "との回線", 20, accent);
        }
        if (TalkBoxShown) _toolbar.Draw(this, ToolbarAnchor, _game?.AutoAdvanceDialog ?? false, Hud.SkipLatched || _ffNow);
        // 会話ボックスが出ていない間も SKIP ラッチが立っていれば画面右上に「▶▶」の印（ラッチは画面を跨いで残る）。
        _toolbar.DrawLatchMark(this, new Vector2(UiKit.DesignW - 16f, 14f));
        UiKit.EndDesign(this);

        // R/Start 長押しリトライの充填チップ（押している間だけ・設計座標で描く）。
        if (_retry.Progress > 0f)
        {
            UiKit.BeginDesign(this);
            Hud.DrawRetryHoldChip(this, _retry.Progress,
                (Pad.ShowKeyboard ? "R" : Pad.Face(JoyButton.Start))
                + (_phase >= PhRoll ? " 長押しでタイトルへ" : " 長押しでさいしょから"));
            UiKit.EndDesign(this);
        }
    }

    // 背景に直乗せする文字（スタッフロール）用のドロップシャドウ付き DrawString。
    //   ボックスの下敷きが無い画面では背景の明部に本文が溶けるため、(0.5,0.5) の黒を先に敷いて浮かせる。
    private void Shadowed(Font f, Vector2 pos, string s, HorizontalAlignment al, float w, int size, Color c)
    {
        DrawString(f, pos + new Vector2(0.5f, 0.5f), s, al, w, size, new Color(0f, 0f, 0f, 0.55f * c.A));
        DrawString(f, pos, s, al, w, size, c);
    }

    private void DrawStaffroll()
    {
        if (_font == null) return;
        // ロール中は空を沈める（文字が最優先）。明け方の空はそのまま後ろに残す。
        DrawRect(new Rect2(0, 0, W, H), new Color(0.02f, 0.03f, 0.06f, 0.40f));
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
            if (sz == UiKit.CutClimax)
            {
                c = UiKit.CutInk with { A = 1f };
            }
            // 「職種\t担当者」の2欄行は、欄の境（RollGutter）で左右に振り分けて描く。
            //   ここを中央寄せの1文字列で済ませると、職種の長短で担当者名の頭が行ごとに揺れ、
            //   複数行の職種を最後の1行で受ける書き方（職種だけの行→担当者だけの行）も繋がって見えない。
            //   → 職種は境で右寄せ、担当者は境から左寄せ＝どの行でも名前の頭が縦に揃う。
            int tabAt = !head && !post ? line.IndexOf('\t') : -1;
            if (tabAt >= 0)
            {
                string role = line[..tabAt], name = line[(tabAt + 1)..].TrimStart();
                if (role.Length > 0)
                    Shadowed(_font, new Vector2(0, y), role, HorizontalAlignment.Right, RollGutter - RollGap, sz, c);
                Shadowed(_font, new Vector2(RollGutter, y), name, HorizontalAlignment.Left, W - RollGutter, sz, c);
                continue;
            }
            Shadowed(_font, new Vector2(0, y), line, HorizontalAlignment.Center, W, sz, c);
        }
        // ヒントは実態（長押しで飛ばせる）に合わせる。旧「Z：つづける」は単押しで進むように読めたうえ、
        //   実際そのとおり単押しで末尾まで飛んでいた。点滅はやめて常時薄く出す（急かさない）。
        //   締めくくり（「そして、ご主人様へ。」以降）に入ったら消す＝最後の三行に注記を重ねない
        //   （ヒントと同じ y をロール行が通るので、出したままだと作品の最後の一行と重なる）。
        float hintA = Mathf.Clamp((float)(_t - 2.0) / 1.0f, 0f, 1f);   // 冒頭2秒は出さない＝ロールの入りを邪魔しない
        int tail = System.Array.IndexOf(_roll, "そして、ご主人様へ。");
        if (tail >= 0)
        {
            float tailIn = (H + tail * RollLineH - H) / RollSpeed;      // その行が画面下端に現れる時刻
            hintA *= 1f - Mathf.Clamp((float)(_t - tailIn) / 1.0f, 0f, 1f);
        }
        if (hintA > 0f)
        {
            // スキップ判定は Pad.AdvanceHeld＝左クリックの長押しでも飛ばせる。直近デバイスがマウスの間は
            //   表記もそれに合わせる（Pad.ShowKeyboard はマウス時も true＝キーボード表記へ落ちるため、
            //   ここで明示的に出し分ける。Hud.TokFocus と同じ作法）。
            string tok = Pad.UsingMouse ? "左クリック" : Pad.ShowKeyboard ? "Z" : Pad.Face(JoyButton.A);
            Shadowed(_font, new Vector2(0, H - 10), tok + " 長押しでスキップ", HorizontalAlignment.Center, W, UiKit.CutNote,
                UiKit.CutInk2 with { A = 0.6f * hintA });
            // 充填バー（押している間だけ）。EndingFilm のスキップ表示と同じ「文字の下に伸びる線」。
            float p = _rollSkip.Progress;
            if (p > 0f)
                DrawLine(new Vector2(W * 0.5f - 30f, H - 6f), new Vector2(W * 0.5f - 30f + 60f * p, H - 6f),
                    Cool with { A = 0.9f }, 1f);
        }
    }

    // タイプライターで送る現在行のテキスト（会話フェーズのみ。スタッフロールは対象外）。
    private string? CurLineText()
    {
        if (_phase == PhGaze) return _line < _gaze.Count ? _gaze[_line].Text : null;
        if (_phase == PhEnd)  return _line < _end.Count ? _end[_line].Text : null;
        return null;
    }

    private void DrawNarration(List<DLine> lines, int idx)
    {
        if (_font == null || idx >= lines.Count) return;
        DrawLineBox(lines[idx]);
    }

    private void DrawEnd()
    {
        if (_font == null || _line >= _end.Count) return;
        DrawLineBox(_end[_line]);
        // END は最後の1行だけ。選択がまだ出ていない間（＝末尾が「本日の業務は、以上です。」）は出さない。
        if (_e6ChoiceLine < 0 && _line >= _end.Count - 1)
        {
            Shadowed(_font, new Vector2(32f, 82f), "END", HorizontalAlignment.Left, 110f, UiKit.CutClimax,
                UiKit.CutInk with { A = 0.96f });
        }
    }

    // 会話ログ（Hud.Backlog）へ積む。話者と縁色は DrawLineBox と同じ（"地"＝語り＝ナレ扱い・話者名なし）。
    //   三人（あかり／こはる／レイ）は「相手」種別で、色は EdgeFor の面の色をそのまま渡す。
    private static void LogLine(DLine d)
    {
        var kind = d.Who switch
        {
            "地"     => Hud.LineKind.Narration,
            "ミナ"   => Hud.LineKind.Mina,
            "あなた" => Hud.LineKind.Boy,
            _        => Hud.LineKind.Other,
        };
        Hud.PushLog(kind, d.Who == "地" ? "" : d.Who, d.Text, EdgeFor(d.Who));
    }

    // 話者ごとの縁色。三人（あかり／こはる／レイ）は面の色を借りて、ミナと取り違えないようにする。
    private static Color EdgeFor(string who) => who switch
    {
        "地"      => UiKit.CutNarr,     // 語り＝話者名なし・中央寄せ
        "あなた"  => UiKit.CutWarm,     // 送られた下書き（E6）
        "あかり"  => new Color("ffb0b8"),
        "こはる"  => new Color("ffd28a"),
        "レイ"    => new Color("9fd8ff"),
        _          => Cool,              // ミナ
    };

    // 下部の語り／会話ボックス。Who: "地"=語り / "ミナ" / 三人の名（あかり・こはる・レイ）/ "あなた"。
    //   旧 "UI"（画面テキスト・起動記録の等幅コード緑）は E2〜E5 の削除で使う行が無くなったので落とした。
    private void DrawLineBox(DLine d)
    {
        bool narr = d.Who == "地";
        var box = DialogueBox.FullScreen;
        UiKit.BeginDesign(this);
        DialogueBox.DrawFrame(this, box, narr ? "" : d.Who, EdgeFor(d.Who));
        int shown = narr ? CurPage.Length : Mathf.Clamp((int)_reveal, 0, CurPage.Length);
        float alpha = narr ? Mathf.Clamp((float)_lineT / 0.35f, 0, 1) : 1;
        DialogueBox.DrawBody(this, box, CurPage, shown, alpha);
        if ((narr ? _lineT >= 0.35 : _reveal >= CurPage.Length) && !_ffNow)
            DialogueBox.DrawContinue(this, box, !LastPage);
        UiKit.EndDesign(this);
    }
}
