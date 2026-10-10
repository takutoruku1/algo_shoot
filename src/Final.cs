using Godot;
using System.Collections.Generic;

// Final : FINAL F4「頂点」（案C・仮台本 docs/20260928/wiki_仮台本_退避/08。ユーザー承認済み・2026-09-05）。
// 戦闘で解決しない本作ルールの総決算。ミナの語りのあとに最後の下書き選択が出る。
// 戻ってくるのは【初】＝あなたが冒頭 P2 で最初に散らした言葉。なぜその言葉かは知らされない。
public partial class Final : Node2D
{
    private const float W = 384f, H = 216f;

    private FontFile _font = null!;
    private double _t;
    private int _phase;   // 0:余韻 1:対話 2:帰還
    private Texture2D _waiting = null!, _received = null!, _rooftop = null!;
    private double _cameraT, _resolveT;
    private bool _zHeld;
    private readonly RetryHold _retry = new(); // R/Start 長押しで最初から（即発の誤爆防止）
    private int _line;
    private double _lineT;
    private double _reveal;        // タイプライター表示済み文字数（＝現在ページ内）
    private DialogueBox.TypeCursor _typeCursor;   // 送り音（1文字ごとのピッ）をどこまで鳴らしたか
    private GameManager? _game;    // 文字送り速度（MsgCharsPerSec）を本編設定と共有

    // テキストボックスは2行固定。2行超の行はページに割り、送り（Z）で続きを読ませる（本文は削らない）。
    private readonly System.Collections.Generic.List<string> _pages = new();
    private int _page;
    private DialoguePacing.Page[] _pagePacing = System.Array.Empty<DialoguePacing.Page>();
    private DialoguePacing.Page? CurPacing => _page < _pagePacing.Length ? _pagePacing[_page] : null;
    private bool PageReady => _reveal >= CurPage.Length;
    private int _pagedLine = -1;               // _pages を構築済みの行 index
    private string CurPage => _pages.Count > 0 ? _pages[Mathf.Min(_page, _pages.Count - 1)] : "";
    private bool LastPage => _pages.Count == 0 || _page >= _pages.Count - 1;
    private void EnsurePages()
    {
        if (_pagedLine == _line || _line >= _talk.Count) return;
        _pagedLine = _line; _page = 0;
        _pages.Clear();
        _pages.AddRange(DialogueBox.Paginate(_talk[_line].Text, DialogueBox.WrapWidth(DialogueBox.FullScreen)));
        _pagePacing = DialoguePacing.ForPages(_talk[_line].Text, _pages);
        _autoT = 0;
    }
    private void NextPage() { _page++; _reveal = 0; _autoT = 0; }

    // 既読スキップ（#22）：Ctrl/RB 長押しで「既読の行だけ」高速送り（本編HUDと同じ作法・独自レンダラ側の実装）。
    private int _readIdx = -1;     // 既読チェック済みの行 index
    private bool _lineWasRead;     // 現在行が「表示開始時点で」既読だったか
    private bool _ffNow;           // いま高速送り中か（ボタン列の SKIP 点灯用）

    private readonly DialogToolbar _toolbar = new();
    private static readonly Vector2 ToolbarAnchor = DialogueBox.Anchor(DialogueBox.FullScreen);
    // AUTOでも下書きの送信はプレイヤーの決定を待つ。
    private const double AutoAfterReveal = 1.0;
    private double _autoT;         // 現在ページを全文表示してからの経過（AUTO 用）
    // 会話ボックスが出ているか（DrawTalk が枠を描く条件と同じ）＝ボタン列を出す／受け付ける条件。
    private bool TalkBoxShown => _phase == 1 && _choice == null && _line < _talk.Count;

    // ───────── 音楽的解決の同期（光田設計 §7「無音→解決音」）─────────
    //   濁った BgmBoss を全編流すと感情が音楽的に解決しないので、ここで「沈黙→主題の解決変奏」を作る。
    //   ① 【初】が送られた直後のミナの絶句「…………。」で BgmBoss を切り、完全無音にする（08 の指定）。
    //   ② 「……その言葉。……ええ。届きました。」と同時に、主題 M.I.N.A. の解決変奏を ppp で立ち上げる。
    //   ③ Final 末尾の余韻まで持続し、Epilogue の BgmMenu（同じ和声圏）へ自然に橋渡しされる。
    //   行は本文一致で検出（配列順を変えても壊れない）。各フェード尺は下の定数で実機調整できる。
    private const string CueSilenceLine = "今、どうぞ。聞いています。";                        // この行で完全無音（BGM 停止）
    private const string CueResolveLine = "はい。……今度の第一声は、採用します。"; // この行と同時に解決音
    private const string CueKeepLine = "もう、言えていますよ。……わたくしも、うれしいです。";
    private const float SilenceFade   = 1.4f;  // BgmBoss を細らせて無音にする尺（「1拍」の沈黙の入り）
    private const float ResolveFade   = 4.0f;  // 解決音 ppp の立ち上がり（沈黙→解決の落差を活かす）
    private bool _cueSilenceDone;              // 二重発火を防ぐワンショット
    private bool _cueResolveDone;

    // 配色は UiKit のカットシーントークンへ集約（3画面で同値のコピーだったものを参照に置換）。
    private static readonly Color Cool = UiKit.CutMina;   // ミナ
    private static readonly Color Warm = UiKit.CutWarm;   // 「あなた」（送られた下書き）

    private struct DLine { public string Who; public string Text; }
    private readonly List<DLine> _talk = new List<DLine>();

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Linear;
        _font = UiKit.Zen; // 非ピクセル（滑らかゴシック）
        _waiting = GD.Load<Texture2D>("res://char/bg2/ending/cg_final_wait_v1.png");
        _received = GD.Load<Texture2D>("res://char/bg2/ending/cg_final_received_v1.png");
        _rooftop = GD.Load<Texture2D>("res://char/bg2/ending/cg_ep_mina_sky_v3.png");
        _zHeld = Pad.AdvanceHeld();
        // F4 の地の音。旋律を立てないアンビエントで「世界の底」だけを鳴らし、テキストに主役を渡す。
        //   2026-09-29 まではここが合成 BgmBoss（6.4秒の正弦波ループ）だった（ボス曲を実音源へ差し替えた
        //   ときの積み残しで、Final だけ取り残されていた）。
        //   ★この曲は「消すために鳴らしている」＝CueSilenceLine の StopMusic で消えた瞬間の無音が決定打。
        //     平坦な曲を選んであるのは、盛り上がりの途中でぶつ切りにしないため。挿入歌の一点投入は phase1 末。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmFinalCutscene);
        // 汚染ゲージの終着点：黒く溶ける。
        _game = GetNodeOrNull<GameManager>("/root/Game");
        _game?.SetContamination(1f);

        // Who: "地"=ミナの語り（ナレ・回想／話者名なし・中央寄せ） / "ミナ"=ミナのセリフ / "あなた"=送られた下書き
        // 音楽のキューと DialoguePacing は本文一致で同期する。
        void T(string who, string text) => _talk.Add(new DLine { Who = who, Text = text });
        T("ミナ", "ご主人様。起動時に読み込んだ、四百十四件の下書き。あれは？");
        T("あなた", "僕が観測して、届けられないまま残していた声だ。君がその声に触れられるよう、起動のときに渡した。");
        T("ミナ", "ずっと一人で、それを見ていたのですね。");
        T("あなた", "世界の仕組みは分かった。道の開き方も分かった。でも、一緒に行く相手はいなかった。");
        T("ミナ", "だから、36回も。");
        T("あなた", "うん。……37回目に、君が返事をくれた。");
        T("ミナ", "最初は、成功か失敗かというお話でしたね。わたくし、試験の採点をしている気分でした。");
        T("あなた", "あれは、やり直したい。");
        T("ミナ", "今、どうぞ。聞いています。");
        _choiceLine = _talk.Count;                                // ここに着いたら選択を出す（送信行はそのとき挿し込む）

    }

    // ───────── F4 の下書き選択（頂点）─────────
    //   戻ってくるのは【初】＝GameManager.FirstScattered（冒頭 P2 で最初に散らした言葉）。
    //   このとき言葉は散らないので【散】には計上しない（05「F4 は例外で計上しない」）。
    private static readonly string[] FinalChoices = { "会えてよかった、ミナ。一緒に帰ろう。", "うまく言えない。……でも、君がいてくれてうれしい。" };

    private int _choiceLine = -1;      // ここに着いたら選択を出す（-1＝提示済み）
    private ChoiceOverlay? _choice;
    private double _choiceT;           // 提示からの経過＝迷い秒数（RecordChoice へ渡す）

    private void ShowFinalChoice()
    {
        _choiceT = 0;
        _choice = ChoiceOverlay.Show(this, FinalChoices, defaultSel: 1, cinematic: true);
    }

    private void ApplyFinalChoice(int sel)
    {
        ChoiceEffects.Record(_game, "f4", FinalChoices, sel, (float)_choiceT);
        _choiceLine = -1;
        var after = sel == 0 ? new List<DLine>
        {
            new() { Who = "あなた", Text = "会えてよかった、ミナ。一緒に帰ろう。" },
            new() { Who = "ミナ", Text = "はい。……今度の第一声は、採用します。" },
        } : new List<DLine>
        {
            new() { Who = "あなた", Text = "うまく言えない。……でも、君がいてくれてうれしい。" },
            new() { Who = "ミナ", Text = "もう、言えていますよ。……わたくしも、うれしいです。" },
        };
        after.AddRange(new List<DLine>
        {
            new() { Who = "ミナ", Text = "今日は、もう休みます。戻ったら、わたくしの見た空をお話ししたいので。" },
            new() { Who = "あなた", Text = "うん。聞かせて。帰り道は、つないである。" },
        });
        _talk.InsertRange(_line, after);
        _pagedLine = -1; _page = 0; _reveal = 0; _lineT = 0; _readIdx = -1;
    }

    public override void _Process(double delta)
    {
        // ボタン列は Pad.AdvanceHeld を読む前に回す（ボタン上のクリックを会話送りに数えない）。
        //   パッド Start はここでは短押し＝MENU／長押し＝最初から（RetryHold）。
        _toolbar.Tick(this, delta, TalkBoxShown, ToolbarAnchor, unreadLine: !_lineWasRead, startTapOpensMenu: true);
        _t += delta;
        _cameraT += delta;
        if (_cueResolveDone) _resolveT += delta;
        // 会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        // ポーズメニュー／会話ログを閉じた Z の同じ押下を、会話送りとして二重に拾わない（Pad.UiBlocked。2026-09-27）。
        bool zEdge = z && !_zHeld && !Pad.UiBlocked(this);
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
            case 0: if (_t >= 3.2 || zEdge) NextPhase(); break;
            case 1:                                                       // 対話（手動送り）
                // 既読スキップでもプレイヤーの送信は代行しない。
                if (_choice != null)
                {
                    _choiceT += delta;
                    if (!_choice.Decided) break;
                    ApplyFinalChoice(_choice.Selected);
                    _choice.QueueFree();
                    _choice = null;
                    break;
                }
                // 語り3行を送り切って選択点に着いたら提示する（送信行はここでは進めない）。
                if (_choiceLine >= 0 && _line == _choiceLine) { ShowFinalChoice(); break; }
                _lineT += delta;
                MusicCue();   // 表示中の行に応じて BgmBoss停止／無音／解決音を1回ずつ発火
                EnsurePages();
                // タイプライター送り（本編HUDと同じ MsgCharsPerSec）。現在ページ内を進める。
                string page = CurPage;
                int len = _line < _talk.Count ? page.Length : 0;
                bool pageWasRevealed = _reveal >= len;
                if (_reveal < len)
                {
                    _reveal = DialogueBox.AdvanceReveal(CurPage, _reveal, delta, _game?.MsgCharsPerSec ?? DialogueBox.DefaultCharsPerSec, CurPacing);
                    DialogueBox.TypeSound(KindOf(_talk[_line].Who), CurPage, _reveal, ref _typeCursor);
                }
                // 既読スキップ（#22）：行の表示開始時に一度だけ既読かを控え、表示と同時に既読へ記録。
                if (_readIdx != _line && _line < _talk.Count)
                {
                    _readIdx = _line;
                    _lineWasRead = _game?.IsLineRead(_talk[_line].Text) ?? false;
                    _game?.MarkLineRead(_talk[_line].Text);
                    LogLine(_talk[_line]);   // 会話ログ（L / Tab で開く Backlog）へ、表示を始めた行を積む（2026-09-26）
                }
                _ffNow = Hud.SkipHeld && _lineWasRead; // 未読行では効かない
                // AUTO（会話ボックスの AUTO ボタン／設定の「オート会話送り」）：現在ページの全文表示後 AutoAfterReveal 秒で送る。
                if (pageWasRevealed) _autoT += delta; else _autoT = 0;
                bool autoGo = (_game?.AutoAdvanceDialog ?? false) && _autoT >= (CurPacing?.AutoWait ?? AutoAfterReveal);
                if (zEdge || ((_ffNow || autoGo) && _lineT >= 0.25))
                {
                    _autoT = 0;
                    if (_reveal < len) { _reveal = len; } // 1回目で現在ページ全文（早送り）＝句点ホールドも飛ばす
                    else if (!LastPage) { NextPage(); _lineT = 0; }   // 後続ページがあれば続きへ（既読FFも同経路で全ページ抜ける）
                    else
                    {
                        _lineT = 0; _reveal = 0; _line++; _page = 0; _pagedLine = -1;
                        // 未提示の選択点に着いたら会話の途中＝次フレームの提示に譲る（Prologue と同じ作法）。
                        if (_line >= _talk.Count && _line != _choiceLine) NextPhase();
                    }
                }
                break;
            case 2:
                if (_t >= 3.0) GameManager.FadeToScene(this, "res://Epilogue.tscn");
                break;
        }
        QueueRedraw();
    }

    private void NextPhase() { _phase++; _t = 0; _lineT = 0; _reveal = 0; }

    // 表示中の行（_line）に応じて、音楽の沈黙と解決を一度ずつ発火する。
    //   細らせ → 無音 → （沈黙の1拍）→ 解決音 ppp。Epilogue の BgmMenu へはそのまま溶ける。
    private void MusicCue()
    {
        if (_line >= _talk.Count) return;
        var audio = Audio.Instance;
        string text = _talk[_line].Text;

        // ① 送信直後のミナの絶句で BgmBoss を細らせ、完全無音にする（沈黙の1拍をここで作る）。
        if (!_cueSilenceDone && text == CueSilenceLine)
        {
            _cueSilenceDone = true;
            audio?.StopMusic(fade: SilenceFade);   // BgmBoss → 無音
        }

        // ③ 「……その言葉。……ええ。届きました。」の表示と同時に、主題の解決変奏を ppp で立ち上げる。
        //    直前で StopMusic 済み＝無音からの立ち上がり。落差が決定打。
        if (!_cueResolveDone && (text == CueResolveLine || text == CueKeepLine))
        {
            _cueResolveDone = true;
            audio?.PlayFinalResolve(fade: ResolveFade);
        }
    }

    public override void _Draw()
    {
        float approach = 1f - Mathf.Exp(-(float)_cameraT / 18f);
        float zoom = Mathf.Lerp(1f, 1.045f, approach);
        DrawArt(_waiting, zoom, 1f);
        DrawArt(_received, zoom, Mathf.SmoothStep(0f, 1f, (float)_resolveT / 2.4f));
        if (_phase == 2)
            DrawArt(_rooftop, 1f, Mathf.SmoothStep(0f, 1f, (float)_t / 2.5f));
        else DrawTalk();

        // 会話ボックスのボタン列とラッチの印（設計座標・枠より手前）。SKIP はラッチ中か、押しっぱなしの早送り中に点ける。
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

    private void DrawArt(Texture2D texture, float zoom, float alpha)
    {
        Vector2 size = texture.GetSize();
        size *= Mathf.Max(W / size.X, H / size.Y) * zoom;
        DrawTextureRect(texture, new Rect2((new Vector2(W, H) - size) * 0.5f, size), false,
            new Color(1f, 1f, 1f, alpha));
    }

    // 会話ログ（Hud.Backlog）へ積む。話者と縁色は DrawTalk と同じ（"地"＝ミナの語り＝ナレ扱い・話者名なし／
    //   "あなた"＝送られた下書き＝暖色）。行の表示開始時に1回（既読ゲートと同じタイミング）。
    private static void LogLine(DLine d)
    {
        bool narr = d.Who == "地", mina = d.Who == "ミナ";
        Hud.PushLog(KindOf(d.Who), narr ? "" : d.Who, d.Text, narr ? UiKit.CutNarr : mina ? Cool : Warm);
    }

    // 話者 → 行の種別（会話ログの本文色と、送り音の音色の両方に使う）。
    private static Hud.LineKind KindOf(string who)
        => who == "地" ? Hud.LineKind.Narration : who == "ミナ" ? Hud.LineKind.Mina : Hud.LineKind.Boy;

    private void DrawTalk()
    {
        if (_font == null || _phase != 1 || _line >= _talk.Count) return;
        var d = _talk[_line];
        bool narr = d.Who == "地";
        Color edge = narr ? UiKit.CutNarr : d.Who == "ミナ" ? Cool : Warm;
        UiKit.BeginDesign(this);
        var box = DialogueBox.FullScreen;
        DialogueBox.DrawFrame(this, box, narr ? "" : d.Who, edge);
        DialogueBox.DrawBody(this, box, CurPage, _reveal,
            speed: _game?.MsgCharsPerSec ?? DialogueBox.DefaultCharsPerSec, pacing: CurPacing);
        if (PageReady && !_ffNow)
            DialogueBox.DrawContinue(this, box, !LastPage);
        UiKit.EndDesign(this);
    }
}
