using Godot;
using System.Collections.Generic;

// Final : FINAL F4「頂点」（案C・仮台本 wiki/08_仮台本/08。ユーザー承認済み・2026-09-05）。
// 戦闘で解決しない本作ルールの総決算。悲鳴ワードが漂う中、ミナの語りのあとに最後の下書き選択が出る。
// 戻ってくるのは【初】＝あなたが冒頭 P2 で最初に散らした言葉。なぜその言葉かは知らされない。
// 沈黙 20 秒でその言葉がひとりでに灯って送られる（既読プレイでも短縮しない）。
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
    //   ① 【初】が送られた直後のミナの絶句「…………。」で BgmBoss を切り、完全無音にする（08 の指定）。
    //   ② 「……その言葉。……ええ。届きました。」と同時に、主題 M.I.N.A. の解決変奏を ppp で立ち上げる。
    //   ③ Final 末尾の余韻まで持続し、Epilogue の BgmMenu（同じ和声圏）へ自然に橋渡しされる。
    //   行は本文一致で検出（配列順を変えても壊れない）。各フェード尺は下の定数で実機調整できる。
    private const string CueSilenceLine = "…………。";                        // この行で完全無音（BGM 停止）
    private const string CueResolveLine = "……その言葉。……ええ。届きました。"; // この行と同時に解決音
    private const float SilenceFade   = 1.4f;  // BgmBoss を細らせて無音にする尺（「1拍」の沈黙の入り）
    private const float ResolveFade   = 4.0f;  // 解決音 ppp の立ち上がり（沈黙→解決の落差を活かす）
    private bool _cueSilenceDone;              // 二重発火を防ぐワンショット
    private bool _cueResolveDone;

    // ───────── 三人の名を「一人ずつ沈ませる」溜め（演出のみ・本文は据え置き）─────────
    //   「あかりの。こはるの。レイの。……」の行だけ、各句点「。」の直後でタイプライターを一拍止める。
    //   reveal が句点直後インデックスに達したら _holdT 秒だけ次の文字へ進めない＝あかり／こはる／レイが
    //   一人ずつ間を置いて落ちて見える。Z早送り（_reveal=len）が来ればホールドも飛ぶので待たせ過ぎない。
    private const string DropLine  = "あかりの。こはるの。レイの。……ぜんぶ、ここに。"; // 本文一致で検出（配列順に依存しない）
    private const float  DropHold  = 0.35f;  // 各「。」直後で溜める尺（一人ずつ沈む“間”）
    private double _holdT;                     // 句点ホールドの残り時間
    private int    _holdAt = -1;               // 既にホールド済みの reveal 位置（同じ句点で二重に止めない）

    // 配色は UiKit のカットシーントークンへ集約（3画面で同値のコピーだったものを参照に置換）。
    private static readonly Color Cool = UiKit.CutMina;   // ミナ
    private static readonly Color Warm = UiKit.CutWarm;   // 「あなた」（送られた下書き）
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    // 流れ込む悲鳴（背景に薄く流れる断片）
    private static readonly string[] Screams =
    {
        "むだだよ", "どうせ", "ごめんなさい", "とどかない", "もういない",
        "わたしのせいだ", "ひとりになる", "なんで", "きえたい", "たすけて",
    };
    private readonly List<(string s, float x, float y, float sp)> _drift = new();
    // 頂点で漂う語の総数の上限（固定の悲鳴 22 ＋ 散った言葉 16）。旧 30（散った語 8）から、
    //   道中の選択が6か所増えたぶん（17）だけ散った語の枠を広げた。
    private const int DriftMax = 38;

    private struct DLine { public string Who; public string Text; }
    private readonly List<DLine> _talk = new List<DLine>();

    public override void _Ready()
    {
        _rng.Randomize();
        _font = UiKit.Zen; // 非ピクセル（滑らかゴシック）
        // 主題の濁り＝緊張のボスBGM（短調寄り・不協和の変奏）。挿入歌の一点投入はphase5。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBoss);
        // 汚染ゲージの終着点：黒く溶ける。
        _game = GetNodeOrNull<GameManager>("/root/Game");
        _game?.SetContamination(1f);

        for (int i = 0; i < 22; i++)
            _drift.Add((Screams[i % Screams.Length],
                _rng.RandfRange(0, W), _rng.RandfRange(0, H), _rng.RandfRange(10f, 34f)));

        // Who: "地"=ミナの語り（ナレ・回想／話者名なし・中央寄せ） / "ミナ"=ミナのセリフ / "あなた"=送られた下書き
        // F4 頂点（仮台本 08）。少年・Stay・録音の要素は案C ですべて落とした。
        //   ※ CueSilenceLine/CueResolveLine/DropLine と本文一致で音楽・演出が同期しているため、該当行の変更禁止。
        // 語り3行 → ここで下書き選択（_choiceLine）→ 送られた【初】→ 絶句 → 受け → 軽口 → 語り、の順。
        void T(string who, string text) => _talk.Add(new DLine { Who = who, Text = text });
        T("地", "祓うほど、軽くなると思っていました。");
        T("地", DropLine);                                       // 「あかりの。こはるの。レイの。……ぜんぶ、ここに。」
        T("ミナ", "……ご主人様。…………まだ、いらっしゃいますか。"); // タイトル IdleTalk の一行を、ここで一度だけ
        _choiceLine = _talk.Count;                                // ここに着いたら選択を出す（送信行はそのとき挿し込む）

        // 悲鳴ワードに【散】の実文字列を混ぜる（説明はしない。08 F2 の「拾う」弾の系譜）。
        //   現行10語はそのまま残し、散った言葉を後ろへ足して漂わせる。
        //   17（道中の選択肢 案C）: 道中の選択が6か所増えて散った言葉が二十数件になるので、
        //   散った言葉の枠を 8 → 16 に広げ（固定22＋散った語16＝38）、
        //   「送れない」の場面（S2-4）で散った語を**先頭**に入れる＝必ず混ざるようにする。
        var scattered = new List<string>(ChoiceEffects.PriorityScattered(_game));
        foreach (var w in _game?.ScatteredWords ?? new List<string>())
            if (!scattered.Contains(w)) scattered.Add(w);
        foreach (var w in scattered)
            if (!string.IsNullOrEmpty(w) && _drift.Count < DriftMax)
                _drift.Add((w, _rng.RandfRange(0, W), _rng.RandfRange(0, H), _rng.RandfRange(10f, 34f)));
    }

    // ───────── F4 の下書き選択（頂点）─────────
    //   戻ってくるのは【初】＝GameManager.FirstScattered（冒頭 P2 で最初に散らした言葉）。
    //   並びは（送らない）が先頭で、末尾＝【初】。ChoiceOverlay の沈黙タイマーは末尾を自動決定するので、
    //   14 秒で【初】が灯りはじめ、20 秒でひとりでに送られる（08 の指定どおり）。
    //   （送らない）を選んだら一度だけ受けて、同じ【初】を1択で再提示＝必ず送らせる。
    //   このとき言葉は散らないので【散】には計上しない（05「F4 は例外で計上しない」）。
    private const string FirstWordFallback = "ミナ";   // 【初】が空（旧セーブ・ボス直行）のときに戻す言葉
    private string FirstWord => string.IsNullOrEmpty(_game?.FirstScattered) ? FirstWordFallback : _game!.FirstScattered;
    private int _choiceLine = -1;      // ここに着いたら選択を出す（-1＝提示済み）
    private ChoiceOverlay? _choice;
    private double _choiceT;           // 提示からの経過＝迷い秒数（RecordChoice へ渡す）
    private bool _refused;             // （送らない）を一度受けた＝次は1択で必ず送らせる

    private void ShowFinalChoice()
    {
        _choiceT = 0;
        // 一度断られたあとは1択（【初】だけ）。初回は（送らない）が先頭・【初】が末尾。
        _choice = ChoiceOverlay.Show(this,
            _refused ? new[] { FirstWord } : new[] { "（送らない）", FirstWord },
            defaultSel: _refused ? 0 : 1);
    }

    // 選択の確定。送ったら以降の受けを挿し込み、（送らない）なら一度だけ受けて再提示する。
    private void ApplyFinalChoice(int sel)
    {
        bool sent = _refused || sel == 1;
        if (!sent)
        {
            // 一度だけ受けて、同じ【初】を1択で再提示（必ず送る）。言葉は散らない＝記録もしない。
            _refused = true;
            _talk.Insert(_line, new DLine { Who = "ミナ", Text = "……いいえ。それだけは、もう、散らせません。" });
            _choiceLine = _line + 1;   // 受けを1行送ってから再提示（同フレームで出し直さない）
            _pagedLine = -1; _page = 0; _reveal = 0; _lineT = 0; _readIdx = -1;
            return;
        }
        string word = FirstWord;
        // 【終】＝最後に送った言葉（E2 の合言葉・E7 の一行）。【散】は F4 では計上しない＝others は空。
        _game?.RecordChoice("f4", word, System.Array.Empty<string>(), (float)_choiceT);
        _choiceLine = -1;
        var after = new List<DLine>
        {
            new() { Who = "あなた", Text = word },
            new() { Who = "ミナ",  Text = CueSilenceLine },                  // 絶句＝ここで BGM 停止。無音
            new() { Who = "ミナ",  Text = CueResolveLine },                  // 正体は言わない。届いたことだけ
            // 最後の軽口。送信文字列の実数だけを差し込む観測（人格の断定は置かない）。
            new() { Who = "ミナ",  Text = $"……{word.Length}文字。……ふふ。相変わらず、短いですね。" },
            new() { Who = "地",   Text = "——それから、わたくしは、自分の足で。帰るほうへ、泳ぎました。" },
        };
        _talk.InsertRange(_line, after);
        _pagedLine = -1; _page = 0; _reveal = 0; _lineT = 0; _readIdx = -1;
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
                // 下書き選択の提示中は会話送りを止め、決定だけを待つ（既読スキップも効かせない
                //   ＝08「既読プレイでも短縮しない」。沈黙20秒の自動送信は ChoiceOverlay 側が持つ）。
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
                        // 未提示の選択点に着いたら会話の途中＝次フレームの提示に譲る（Prologue と同じ作法）。
                        if (_line >= _talk.Count && _line != _choiceLine) NextPhase();
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

    // 表示中の行（_line）に応じて、音楽の沈黙と解決を一度ずつ発火する。
    //   細らせ → 無音 → （沈黙の1拍）→ 解決音 ppp。Epilogue の BgmMenu へはそのまま溶ける。
    private void MusicCue()
    {
        if (_line >= _talk.Count) return;
        var audio = Audio.Instance;
        if (audio == null) return;
        string text = _talk[_line].Text;

        // ① 送信直後のミナの絶句で BgmBoss を細らせ、完全無音にする（沈黙の1拍をここで作る）。
        if (!_cueSilenceDone && text == CueSilenceLine)
        {
            _cueSilenceDone = true;
            audio.StopMusic(fade: SilenceFade);   // BgmBoss → 無音
        }

        // ③ 「……その言葉。……ええ。届きました。」の表示と同時に、主題の解決変奏を ppp で立ち上げる。
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
            // 帰還後：ミナの光がひとつだけ残る（案C では隣に立つ少年の光は無い）。
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
        // "あなた"＝送られた下書き。他画面と揃えて暖色（Warm）で出す。
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
