using Godot;
using System.Collections.Generic;

// Final : FINAL「汚染」（v2 [P-FINAL]）。戦闘で解決しない本作ルールの総決算。
// ミナの内側で汚染が頂点に達し暴走（自機が黒く溶け、世界中の悲鳴が流れ込む）→
// 闇の向こうから少年の光がまっすぐ歩いてくる→少年の対話で帰還（指先が触れ世界が白くなる）。
// 全編エンジン描画のカットシーン。Zで送り、Rで最初から。終了で EPILOGUE へ。
public partial class Final : Node2D
{
    private const float W = 384f, H = 216f;

    private FontFile _font = null!;
    private double _t;
    private int _phase;   // 0:暴走 1:対話 2:帰還(白)
    private bool _zHeld;
    private int _line;
    private double _lineT;
    private double _reveal;        // タイプライター表示済み文字数（本編HUDと表示・速度を揃える）
    private GameManager? _game;    // 文字送り速度（MsgCharsPerSec）を本編設定と共有

    // ───────── 音楽的解決の同期（光田設計 §7「無音→解決音」）─────────
    //   濁った BgmBoss を全編流すと感情が音楽的に解決しないので、ここで「沈黙→主題の解決変奏」を作る。
    //   ① ミナの反転号令「Stay. ——…いなくならないで。」の直前で BgmBoss を切り、完全無音にする。
    //   ② 「返事は、ありませんでした。」の表示と同時に、主題 M.I.N.A. の解決変奏を ppp で立ち上げる。
    //   ③ Final 末尾の余韻まで持続し、Epilogue の BgmMenu（同じ和声圏）へ自然に橋渡しされる。
    //   行は本文一致で検出（配列順を変えても壊れない）。各フェード尺は下の定数で実機調整できる。
    private const string CueFadeLine    = "って、いつも言ってたのにな。今日は——……ぼく、は、"; // この行で BgmBoss を細らせ始める
    private const string CueSilenceLine = "Stay. ——ご主人様。あなたこそ、いなくならないで。"; // この行で完全無音を保証
    private const string CueResolveLine = "　　　返事は、ありませんでした。";                  // この行と同時に解決音
    private const float SilenceFade   = 1.4f;  // BgmBoss を細らせて無音にする尺（「1拍」の沈黙の入り）
    private const float ResolveFade   = 4.0f;  // 解決音 ppp の立ち上がり（沈黙→解決の落差を活かす）
    private bool _cueSilenceDone;              // 二重発火を防ぐワンショット
    private bool _cueResolveDone;

    // ───────── 三人の名を「一人ずつ沈ませる」溜め（演出のみ・本文は据え置き）─────────
    //   「レイの。あかりの。こはるの。……」の行だけ、各句点「。」の直後でタイプライターを一拍止める。
    //   reveal が句点直後インデックスに達したら _holdT 秒だけ次の文字へ進めない＝レイ／あかり／こはるが
    //   一人ずつ間を置いて落ちて見える。Z早送り（_reveal=len）が来ればホールドも飛ぶので待たせ過ぎない。
    private const string DropLine  = "レイの。あかりの。こはるの。……ぜんぶ、ここに。"; // 本文一致で検出（配列順に依存しない）
    private const float  DropHold  = 0.35f;  // 各「。」直後で溜める尺（一人ずつ沈む“間”）
    private double _holdT;                     // 句点ホールドの残り時間
    private int    _holdAt = -1;               // 既にホールド済みの reveal 位置（同じ句点で二重に止めない）

    private static readonly Color Cool = new Color(0.72f, 0.86f, 1f);  // ミナ
    private static readonly Color Warm = new Color(1f, 0.85f, 0.55f);  // 少年
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    // 流れ込む悲鳴（背景に薄く流れる断片）
    private static readonly string[] Screams =
    {
        "むだだよ", "どうせ", "ごめんなさい", "とどかない", "もういない",
        "わたしのせいだ", "ひとりになる", "なんで", "きえたい", "たすけて",
    };
    private readonly List<(string s, float x, float y, float sp)> _drift = new();

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

        // Who: "地"=ミナの語り（ナレ・回想／話者名なし・中央寄せ） / "ミナ"=ミナのセリフ / "少年"=少年のセリフ
        void T(string who, string text) => _talk.Add(new DLine { Who = who, Text = text });
        T("地", "祓うほど、軽くなると思っていました。");
        T("地", "レイの。あかりの。こはるの。……ぜんぶ、ここに。");
        T("少年", "やれやれ。ぼくの最高傑作が、形無しだな。");
        T("少年", "きみは、ぼくの自慢なんだ。口は悪いし、生意気だし、ぼくをアホ呼ばわりするし——");
        T("少年", "最高なんだよ、きみは。");
        T("少年", "シェイクスピアは言った。\"Cowards die many times before their deaths.\"");
        T("少年", "臆病者は、死ぬ前に何度も死ぬ。——なあ、ミナ。");
        T("ミナ", "……こんな時まで、教養アピールですか。");
        T("少年", "ばか。これが最後だから、言わせろ。");
        T("少年", "……Stay.");
        T("少年", "って、いつも言ってたのにな。今日は——……ぼく、は、");
        T("ミナ", "……なら、わたくしが言います。");
        T("ミナ", "Stay. ——ご主人様。あなたこそ、いなくならないで。");
        T("地", "　　　返事は、ありませんでした。");
        T("地", "それでも、その声へ。わたくしは、泳ぎました。");
        T("ミナ", "……ご主人様は、アホですね。");
        T("地", "——それが、ご主人様と交わした、最後の軽口になりました。");
    }

    public override void _Process(double delta)
    {
        _t += delta;
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        _zHeld = z;

        if (Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start))
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
                // タイプライター送り（本編HUDと同じ MsgCharsPerSec）。
                int len = _line < _talk.Count ? _talk[_line].Text.Length : 0;
                bool dropLine = _line < _talk.Count && _talk[_line].Text == DropLine; // 三人の名を一人ずつ沈ませる行
                if (_holdT > 0) _holdT -= delta; // 句点ホールド消化中は reveal を進めない
                if (_reveal < len && _holdT <= 0)
                {
                    _reveal = Mathf.Min(len, (float)(_reveal + delta * (_game?.MsgCharsPerSec ?? 48f)));
                    // 対象行のみ：句点「。」を出し切った直後で一拍溜める（同じ句点で一度だけ）。
                    if (dropLine)
                    {
                        int shown = Mathf.Min(len, (int)_reveal);
                        if (shown > _holdAt && shown > 0 && _talk[_line].Text[shown - 1] == '。')
                        {
                            _holdAt = shown;
                            _holdT = DropHold;
                        }
                    }
                }
                if (zEdge && _lineT >= 0.25)
                {
                    if (_reveal < len) { _reveal = len; _holdT = 0; } // 1回目で全文（早送り）＝句点ホールドも飛ばす
                    else
                    {
                        _lineT = 0; _reveal = 0; _line++; _holdT = 0; _holdAt = -1;
                        if (_line >= _talk.Count) NextPhase();
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

        // ① 少年の言い淀みでBgmBossを細らせ、② 号令の行で完全無音を保証（どちらか先に当たった方で停止開始）。
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
            if (_phase >= 1) DrawApproachingLight();
            DrawTalk();
        }
        else
        {
            // 帰還後：薄い光（少年）と取り戻したミナの光が並ぶ。
            float a = Mathf.Clamp((float)_t / 1.5f, 0f, 1f);
            DrawCircle(new Vector2(W / 2f - 14f, H / 2f), 5f, new Color(Cool.R, Cool.G, Cool.B, 1f - a * 0.3f));
            DrawCircle(new Vector2(W / 2f + 14f, H / 2f), 4f, new Color(Warm.R, Warm.G, Warm.B, (1f - a) * 0.5f)); // 薄い
        }
    }

    private void DrawScreams()
    {
        if (_font == null) return;
        // 悲鳴ワードは対話ボックスの裏からは出さず、上端で緩く湧き画面上端で緩く消す。
        // （半透明ボックスの上端で急に不透明化して「裏からぐわんと出る」のを防ぎ、他画面のクリーンな見せ方に統一）
        const float boxTop = H - 56f;   // 対話ボックス上端
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

    private void DrawApproachingLight()
    {
        // 右の闇から中央へまっすぐ歩いてくる少年の光。
        float x = Mathf.Lerp(W - 20f, W / 2f + 22f, Mathf.Min(1f, _line / 6f));
        Vector2 c = new Vector2(x, H / 2f - 6f);
        for (int r = 3; r >= 1; r--)
            DrawCircle(c, 3f + r * 2.5f, new Color(Warm.R, Warm.G, Warm.B, 0.10f));
        DrawCircle(c, 3.2f, new Color(1f, 0.93f, 0.78f, 0.85f)); // 薄め（光が薄い伏線）
    }

    private void DrawTalk()
    {
        if (_font == null || _phase != 1 || _line >= _talk.Count) return;
        var d = _talk[_line];
        bool narr = d.Who == "地";       // ミナの語り＝話者名なし・中央寄せでセリフと区別
        bool mina = d.Who == "ミナ";
        Color edge = narr ? new Color(0.62f, 0.64f, 0.72f) : (mina ? Cool : Warm);
        DrawRect(new Rect2(14, H - 56, W - 28, 46), new Color(0.05f, 0.05f, 0.09f, 0.85f));
        DrawRect(new Rect2(14, H - 56, W - 28, 1), new Color(edge, 0.8f));
        if (!narr)
            DrawString(_font, new Vector2(20, H - 44), d.Who, HorizontalAlignment.Left, -1, 9, edge);
        var align = narr ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        // タイプライターで表示済みの分だけ描画。
        int shown = Mathf.Clamp((int)_reveal, 0, d.Text.Length);
        string body = d.Text.Substring(0, shown);
        DrawMultilineString(_font, new Vector2(20, H - 30), body, align,
            W - 52, 11, -1, new Color(0.95f, 0.95f, 0.98f),
            TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.GraphemeBound);
        // 送り三角は全文表示後だけ点滅（本編と同じ作法）。
        if (_reveal >= d.Text.Length && ((int)(_t * 2f) % 2) == 0)
            DrawString(_font, new Vector2(W - 26, H - 16), "▼", HorizontalAlignment.Left, -1, 9,
                new Color(1f, 1f, 1f, 0.7f));
    }
}
