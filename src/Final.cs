using Godot;
using System.Collections.Generic;

// Final : FINAL「汚染」F4 カットシーン「頂点」（案C）。戦闘で解決しない本作ルールの総決算。
// 台詞の正典: wiki/08_仮台本/08_粗い台本_案C_3_FINALと結末.md（ユーザー承認済み・2026-09-05）の F4。
// 案C に少年は居ない（登場は StageMina/BossMina 側の別課題として保留）＝ここは ミナの独白／あなた
// （送信した下書き）の2種のみ。頂点の下書き選択で「最初に散らした言葉」（GameManager.FirstScattered）
// が戻り、送るか拒むか選べるが、20秒の沈黙で自動的にそれが灯って送信される（ChoiceOverlay 既定挙動）。
// 全編エンジン描画のカットシーン。Zで送り、R/Start 長押しで最初から。終了で EPILOGUE へ。
public partial class Final : Node2D
{
    private const float W = 384f, H = 216f;

    private FontFile _font = null!;
    private double _t;
    private int _phase;   // 0:暴走 1:対話 2:帰還(白)
    private bool _zHeld;
    private readonly RetryHold _retry = new(); // R/Start 長押しで最初から（即発の誤爆防止）
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
        _pages.AddRange(UiKit.Paginate(_font, _talk[_line].Text, UiKit.CutBody, TalkWrapW, Hud.DlgMaxLines));
    }
    private void NextPage() { _page++; _reveal = 0; _holdT = 0; _holdAt = -1; }

    // 既読スキップ（#22）：Ctrl/RB 長押しで「既読の行だけ」高速送り（本編HUDと同じ作法・独自レンダラ側の実装）。
    private int _readIdx = -1;     // 既読チェック済みの行 index
    private bool _lineWasRead;     // 現在行が「表示開始時点で」既読だったか
    private bool _ffNow;           // いま高速送り中か（▶▶表示用）

    // ───────── 音楽的解決の同期（光田設計 §7「無音→解決音」）─────────
    //   濁った BgmBoss を全編流すと感情が音楽的に解決しないので、ここで「沈黙→主題の解決変奏」を作る。
    //   ① 頂点の下書き選択の直前（ご主人様への呼びかけ）で BgmBoss を細らせ始める。
    //   ② 【初】を送った直後の絶句「…………。」で完全無音を保証する（選択が長引いても遅くとも確定直後に無音になる）。
    //   ③ 「……その言葉。……ええ。届きました。」の表示と同時に、主題 M.I.N.A. の解決変奏を ppp で立ち上げる。
    //   ④ Final 末尾の余韻まで持続し、Epilogue の BgmMenu（同じ和声圏）へ自然に橋渡しされる。
    //   行は本文一致で検出（配列順を変えても壊れない）。各フェード尺は下の定数で実機調整できる。
    private const string CueFadeLine    = "……ご主人様。…………まだ、いらっしゃいますか。"; // この行で BgmBoss を細らせ始める
    private const string CueSilenceLine = "…………。";                                      // この行で完全無音を保証
    private const string CueResolveLine = "……その言葉。……ええ。届きました。";              // この行と同時に解決音
    private const float SilenceFade   = 1.4f;  // BgmBoss を細らせて無音にする尺（「1拍」の沈黙の入り）
    private const float ResolveFade   = 4.0f;  // 解決音 ppp の立ち上がり（沈黙→解決の落差を活かす）
    private bool _cueSilenceDone;              // 二重発火を防ぐワンショット
    private bool _cueResolveDone;

    // ───────── 三人の名を「一人ずつ沈ませる」溜め（演出のみ・本文は据え置き）─────────
    //   「あかりの。こはるの。レイの。……」の行だけ、各句点「。」の直後でタイプライターを一拍止める。
    //   reveal が句点直後インデックスに達したら _holdT 秒だけ次の文字へ進めない＝あかり／こはる／レイが
    //   一人ずつ間を置いて落ちて見える。Z早送り（_reveal=len）が来ればホールドも飛ぶので待たせ過ぎない。
    private const string DropLine  = "あかりの。こはるの。レイの。……ぜんぶ、ここに。"; // 本文一致で検出（配列順に依存しない）・面の順（案C）
    private const float  DropHold  = 0.35f;  // 各「。」直後で溜める尺（一人ずつ沈む“間”）
    private double _holdT;                     // 句点ホールドの残り時間
    private int    _holdAt = -1;               // 既にホールド済みの reveal 位置（同じ句点で二重に止めない）

    // 配色は UiKit のカットシーントークンへ集約（3画面で同値のコピーだったものを参照に置換）。
    private static readonly Color Cool = UiKit.CutMina;   // ミナ
    private static readonly Color Warm = UiKit.CutWarm;   // あなた（送った下書き）。案C に少年は居ない
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    // 流れ込む悲鳴（背景に薄く流れる断片）。固定10語に加え、【散】＝プレイヤーが実際に散らした
    // 下書きの実文字列も混ぜる（F4：「悲鳴ワードが漂う（現行10語＋【散】の実文字列。説明なし）」）。
    private static readonly string[] Screams =
    {
        "むだだよ", "どうせ", "ごめんなさい", "とどかない", "もういない",
        "わたしのせいだ", "ひとりになる", "なんで", "きえたい", "たすけて",
    };
    private readonly List<(string s, float x, float y, float sp)> _drift = new();

    private struct DLine { public string Who; public string Text; }
    private readonly List<DLine> _talk = new List<DLine>();

    // ───────── 頂点の下書き選択（F4）─────────
    //   並びは（送らない）が先頭・【初】（最初に散らした言葉）が末尾＝沈黙20秒で末尾が灯って自動送信される
    //  （ChoiceOverlay 既定の「沈黙も選択」挙動をそのまま使う。新規の沈黙タイマーは作らない）。
    //   （送らない）は一度だけ拒める。その後は同じ【初】を1択で再提示し、必ず送らせる
    //  （言葉は散らない＝この最終選択で GameManager.RecordChoice は呼ばない＝【散】に計上しない）。
    private ChoiceOverlay? _choice;
    private string _finalWord = "";     // 【初】。GameManager.FirstScattered が空なら例文の既定語にフォールバック
    private bool _refusedOnce;          // （送らない）を一度受けたか
    private bool _choiceResolved;       // 【初】を送り切ったか（この後の会話をすべて読み切ったら白転へ）

    public override void _Ready()
    {
        _rng.Randomize();
        _font = UiKit.Zen; // 非ピクセル（滑らかゴシック）
        // 主題の濁り＝緊張のボスBGM（短調寄り・不協和の変奏）。挿入歌の一点投入はphase5。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBoss);
        // 汚染ゲージの終着点：黒く溶ける。
        _game = GetNodeOrNull<GameManager>("/root/Game");
        _game?.SetContamination(1f);

        // 漂う悲鳴の語プール：固定10語＋【散】（プレイヤーが実際に散らした下書きの実文字列）。説明はしない。
        var scream = new List<string>(Screams);
        if (_game != null) scream.AddRange(_game.ScatteredWords);
        for (int i = 0; i < 22; i++)
            _drift.Add((scream[i % scream.Count],
                _rng.RandfRange(0, W), _rng.RandfRange(0, H), _rng.RandfRange(10f, 34f)));

        // Who: "地"=ミナの語り（ナレ・回想／話者名なし・中央寄せ） / "ミナ"=ミナのセリフ / "あなた"=送った下書き。
        // 台詞の正典: wiki/08_仮台本/08_粗い台本_案C_3_FINALと結末.md の F4「カットシーン 頂点」。
        //   案C に少年は居ない＝ここで戻ってくるのは声ではなく、頂点の下書き選択で送った【初】の一語だけ。
        //   ※ CueFadeLine/CueSilenceLine/CueResolveLine/DropLine と本文一致で音楽が同期しているため、該当行の変更禁止。
        void T(string who, string text) => _talk.Add(new DLine { Who = who, Text = text });
        T("地", "祓うほど、軽くなると思っていました。");
        T("地", "あかりの。こはるの。レイの。……ぜんぶ、ここに。");
        T("ミナ", "……ご主人様。…………まだ、いらっしゃいますか。"); // タイトル IdleTalk の一行を、ここで一度だけ
        // ここから先（【初】の下書き選択とその受け）は DriveFinalChoice が実プレイの選択結果を見て _talk に積む。
    }

    public override void _Process(double delta)
    {
        _t += delta;
        // 会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
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

        // 悲鳴の漂い更新
        for (int i = 0; i < _drift.Count; i++)
        {
            var d = _drift[i];
            d.y -= d.sp * (float)delta;
            if (d.y < -10f) { d.y = H + 8f; d.x = _rng.RandfRange(0, W); }
            _drift[i] = d;
        }

        switch (_phase)
        {
            case 0: if (_t >= 3.2 || zEdge) NextPhase(); break;          // 暴走の見せ
            case 1:                                                       // 対話（手動送り）
                _lineT += delta;
                MusicCue();   // 表示中の行に応じて BgmBoss停止／無音／解決音を1回ずつ発火
                // 台本を読み切ったら、頂点の下書き選択（【初】が戻る／20秒の沈黙で自動送信）へ。
                //   解決済みならここで白転（NextPhase）へ渡す。
                if (_line >= _talk.Count) { DriveFinalChoice(delta); break; }
                EnsurePages();
                // タイプライター送り（本編HUDと同じ MsgCharsPerSec）。現在ページ内を進める。
                string page = CurPage;
                int len = _line < _talk.Count ? page.Length : 0;
                // 三人の名を一人ずつ沈ませる行（句点ホールドは本文一致で判定。DropLine は1ページに収まる想定＝現在ページで動く）。
                bool dropLine = _line < _talk.Count && _talk[_line].Text == DropLine;
                if (_holdT > 0) _holdT -= delta; // 句点ホールド消化中は reveal を進めない
                if (_reveal < len && _holdT <= 0)
                {
                    _reveal = Mathf.Min(len, (float)(_reveal + delta * (_game?.MsgCharsPerSec ?? 48f)));
                    // 対象行のみ：句点「。」を出し切った直後で一拍溜める（同じ句点で一度だけ）。
                    if (dropLine)
                    {
                        int shown = Mathf.Min(len, (int)_reveal);
                        if (shown > _holdAt && shown > 0 && page[shown - 1] == '。')
                        {
                            _holdAt = shown;
                            _holdT = DropHold;
                        }
                    }
                }
                // 既読スキップ（#22）：行の表示開始時に一度だけ既読かを控え、表示と同時に既読へ記録。
                if (_readIdx != _line && _line < _talk.Count)
                {
                    _readIdx = _line;
                    _lineWasRead = _game?.IsLineRead(_talk[_line].Text) ?? false;
                    _game?.MarkLineRead(_talk[_line].Text);
                }
                _ffNow = Hud.SkipHeld && _lineWasRead; // 未読行では効かない
                if ((zEdge || _ffNow) && _lineT >= 0.25)
                {
                    if (_reveal < len) { _reveal = len; _holdT = 0; } // 1回目で現在ページ全文（早送り）＝句点ホールドも飛ばす
                    else if (!LastPage) { NextPage(); _lineT = 0; }   // 後続ページがあれば続きへ（既読FFも同経路で全ページ抜ける）
                    else
                    {
                        _lineT = 0; _reveal = 0; _line++; _holdT = 0; _holdAt = -1; _page = 0; _pagedLine = -1;
                        // _line が _talk.Count に達しても即 NextPhase はしない：次フレームの先頭ガードが
                        // DriveFinalChoice へ渡す（未解決なら選択を出す／解決済みならそこで白転する）。
                    }
                }
                break;
            case 2: // 帰還（白）→ EPILOGUE
                if (_t >= 3.0) GetTree().ChangeSceneToFile("res://Epilogue.tscn");
                break;
        }
        QueueRedraw();
    }

    private void NextPhase() { _phase++; _t = 0; _lineT = 0; _reveal = 0; _holdT = 0; _holdAt = -1; }

    // 頂点の下書き選択（F4）。台本を読み切って _line が _talk.Count に達するたびに毎フレーム呼ばれる。
    //   ・_choiceResolved 済みなら白転へ（NextPhase）。
    //   ・未提示なら ChoiceOverlay を出す（並びは（送らない）が先頭・【初】が末尾＝沈黙20秒で末尾が
    //     灯って自動送信される。ChoiceOverlay 既定の「沈黙も選択」をそのまま使う＝新規タイマーは作らない）。
    //   ・（送らない）は一度だけ拒める。拒んだ直後はミナの一言を _talk へ積んで戻り、次に来たときは
    //     同じ【初】を1択で再提示して必ず送らせる（この最終選択は RecordChoice を呼ばない＝【散】に計上しない）。
    private void DriveFinalChoice(double delta)
    {
        if (_choiceResolved) { NextPhase(); return; }
        if (_choice == null)
        {
            if (string.IsNullOrEmpty(_finalWord))
            {
                string fs = _game?.FirstScattered ?? "";
                _finalWord = string.IsNullOrEmpty(fs) ? "きこえてる" : fs; // 未取得（旧セーブ等）ならプレースホルダの既定語
            }
            var options = _refusedOnce ? new[] { _finalWord } : new[] { "送らない", _finalWord };
            _choice = ChoiceOverlay.Show(this, options, defaultSel: options.Length - 1);
            return;
        }
        if (!_choice.Decided) return;
        int sel = _choice.Selected;
        _choice.QueueFree();
        _choice = null;

        if (!_refusedOnce && sel == 0)
        {
            _refusedOnce = true;
            AppendLine("ミナ", "……いいえ。それだけは、もう、散らせません。");
            return;
        }

        _choiceResolved = true;
        AppendLine("あなた", _finalWord);                                                         // 【初】拾（＝【終】）
        AppendLine("ミナ", "…………。");                                                            // ここで BGM 停止。無音
        AppendLine("ミナ", "……その言葉。……ええ。届きました。");                                    // 正体は言わない
        AppendLine("ミナ", $"……{_finalWord.Length}文字。……ふふ。相変わらず、短いですね。");         // 送信文字列の実数のみ
        AppendLine("地", "——それから、わたくしは、自分の足で。帰るほうへ、泳ぎました。");            // → 白転 → Epilogue
    }

    private void AppendLine(string who, string text) => _talk.Add(new DLine { Who = who, Text = text });

    // 表示中の行（_line）に応じて、音楽の沈黙と解決を一度ずつ発火する。
    //   細らせ → 無音 → （沈黙の1拍）→ 解決音 ppp。Epilogue の BgmMenu へはそのまま溶ける。
    private void MusicCue()
    {
        if (_line >= _talk.Count) return;
        var audio = Audio.Instance;
        if (audio == null) return;
        string text = _talk[_line].Text;

        // ① 選択前の呼びかけでBgmBossを細らせ、② 送信直後の絶句で完全無音を保証（どちらか先に当たった方で停止開始）。
        if (!_cueSilenceDone && (text == CueFadeLine || text == CueSilenceLine))
        {
            _cueSilenceDone = true;
            audio.StopMusic(fade: SilenceFade);   // BgmBoss → 無音（沈黙の1拍をここで作る）
        }

        // ③ 「返事は、ありませんでした。」の表示と同時に、主題の解決変奏を ppp で立ち上げる。
        //    直前で StopMusic 済み＝無音からの立ち上がり。落差が決定打。
        if (!_cueResolveDone && text == CueResolveLine)
        {
            _cueResolveDone = true;
            audio.PlayFinalResolve(fade: ResolveFade);
        }
    }

    public override void _Draw()
    {
        // 背景：暴走中は黒、帰還で白へ。
        if (_phase < 2)
            DrawRect(new Rect2(0, 0, W, H), new Color(0.01f, 0.01f, 0.02f));
        else
        {
            float a = Mathf.Clamp((float)_t / 1.5f, 0f, 1f);
            DrawRect(new Rect2(0, 0, W, H), new Color(0.01f, 0.01f, 0.02f).Lerp(new Color(1f, 1f, 1f), a));
        }

        if (_phase < 2)
        {
            DrawScreams();
            DrawCorruptedCore();
            DrawTalk();
        }
        else
        {
            // 帰還後：自分の足で戻っていくミナの光だけ（案C に少年は居ない＝迎えの光は無い）。
            float a = Mathf.Clamp((float)_t / 1.5f, 0f, 1f);
            DrawCircle(new Vector2(W / 2f, H / 2f), 5f, new Color(Cool.R, Cool.G, Cool.B, 1f - a * 0.3f));
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

    private void DrawScreams()
    {
        if (_font == null) return;
        // 悲鳴ワードは対話ボックスの裏からは出さず、上端で緩く湧き画面上端で緩く消す。
        // （半透明ボックスの上端で急に不透明化して「裏からぐわんと出る」のを防ぎ、他画面のクリーンな見せ方に統一）
        const float boxTop = H - 58f;   // 対話ボックス上端（DrawTalk と一致）
        const float fade = 24f;         // 出現/消失の緩衝距離
        foreach (var d in _drift)
        {
            if (d.y >= boxTop) continue;                                   // ボックスの裏は描かない
            float a = 0.35f
                * Mathf.Clamp((boxTop - d.y) / fade, 0f, 1f)              // ボックス上端から緩くフェードイン
                * Mathf.Clamp(d.y / fade, 0f, 1f);                        // 画面上端で緩くフェードアウト
            if (a <= 0.001f) continue;
            DrawString(_font, new Vector2(d.x, d.y), d.s, HorizontalAlignment.Left, -1, 9,
                new Color(0.5f, 0.18f, 0.3f, a));
        }
    }

    private void DrawCorruptedCore()
    {
        Vector2 c = new Vector2(W / 2f, H / 2f - 6f);
        float pulse = 1f + 0.12f * Mathf.Sin((float)_t * 4f);
        for (int r = 5; r >= 1; r--)
            DrawCircle(c, (6f + r * 5f) * pulse, new Color(0.08f, 0.02f, 0.10f, 0.22f));
        DrawCircle(c, 10f * pulse, new Color(0.04f, 0.02f, 0.06f));
        // にじむ濁った縁
        DrawArc(c, 12f * pulse, 0, Mathf.Tau, 28, new Color(0.32f, 0.12f, 0.28f, 0.5f), 1.5f);
    }

    private void DrawTalk()
    {
        if (_font == null || _phase != 1 || _line >= _talk.Count) return;
        var d = _talk[_line];
        bool narr = d.Who == "地";       // ミナの語り＝話者名なし・中央寄せでセリフと区別
        bool mina = d.Who == "ミナ";
        Color edge = narr ? UiKit.CutNarr : (mina ? Cool : Warm);
        // 現在ページ（2行固定・禁則つき）。ボックスは2行分の固定高さ（行数で伸ばさない＝全ボックス統一）。
        string page = CurPage;
        var lines = UiKit.WrapLines(_font, page, UiKit.CutBody, W - 56);
        float boxTop = H - 58f;   // 2行固定（下余白12px＝額縁を効かせる）
        // ボックス（Hub/Shop と同じ角丸＋話者色の額縁。UiKit.CutBox で3画面共通）
        UiKit.CutBox(this, new Rect2(14, boxTop, W - 28, H - 10f - boxTop), edge);
        if (!narr)
            DrawString(UiKit.ZenBold, new Vector2(24, boxTop + 12), d.Who, HorizontalAlignment.Left, -1, UiKit.CutSpeaker, edge);
        // ナレも左寄せにする＝中央寄せ＋部分文字列で起きる「中央から左右へ広がる」見え方を撤去。
        //   タイプライター自体は残す（左→右の素直な送り。三人の名を一人ずつ沈ませる句点ホールドも保つ）。
        // タイプライターで表示済みの分だけ、確定済みの行に沿って描画。
        int shown = Mathf.Clamp((int)_reveal, 0, page.Length);
        UiKit.TypewriterLines(this, _font, lines, new Vector2(24, boxTop + 27f), W - 56, UiKit.CutBody,
            UiKit.CutInk, shown);
        // 既読高速送り中の控えめな表示（ボックス右上・#22）。
        if (_ffNow)
            DrawString(UiKit.ZenBold, new Vector2(W - 42, boxTop + 12), "▶▶", HorizontalAlignment.Left, -1, UiKit.CutSpeaker,
                new Color(Cool, 0.8f));
        // 送り三角は現在ページの全文表示後だけ点滅（本編と同じ作法。後続ページも同じ▼で示す）。
        if (_reveal >= page.Length && ((int)(_t * 2f) % 2) == 0)
            DrawString(_font, new Vector2(W - 26, H - 16), "▼", HorizontalAlignment.Left, -1, UiKit.CutNote,
                new Color(1f, 1f, 1f, 0.7f));
    }
}
