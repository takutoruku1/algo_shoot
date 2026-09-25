using Godot;
using System.Collections.Generic;
using System.Text;

// FuryDial : 【激情】——盤面のいちばん奥に沈めた、**ボスの下書き（入力欄）**（2026-09-25・第4稿）。
//
//   第3稿はバイクの速度計を模した円形計器（針・目盛り・赤い危険域・波形）だった。作者評価は
//   「キャラクターのイメージとそぐわなすぎる」。機械の計器はこの作品（SNSとスマホの話。ミナの能力は
//   下書きを読むこと＝下書きが本音）の異物だった。**計器は捨て**、残すと決まった二つだけを引き継ぐ:
//     ・背景全体の色の連動（FuryWash＝激情で赤く灼ける／無感情で色が抜けて青白くなる）→ 末尾のまま。
//     ・波形の「振る舞い」（激情で暴れる／無感情でフラットライン）→ 見た目は捨て、作品の語彙に翻訳。
//
//   ── メーター＝ボスの「下書き」の状態 ──────────────────────────────
//   盤面中心・ボスの背後に、ボスの入力欄を大きく薄く敷く。目盛りも針も無い。状態は
//   **文字の量・乱れ・カーソルの速さ・背景の色**だけで伝える。
//
//     0（理想）  … 入力欄に、ボスの本音の言葉（BossPostStory の下書き）が静かに打たれている途中。
//                  句読点で息をつき、打ち終えたらしばらく留まり、そっと消えてまた打ち始める。
//                  カーソルは穏やかに点滅（1.1s 周期＝OpeningFilm の下書き画面と同じ）。
//     激情（+）  … 書きかけの言葉が溢れる。本音の途中から「返して」「見て」「ちゃんと」のような
//                  反復向きの語（危険信号の台詞・RECLOSE から取った断片）が割り込み、同じ語の連打、
//                  句読点の脱落、濁点だけ（゛゛）、消しては打ち直す、が増えていく。行が増えて入力欄の
//                  下と右へ**はみ出す**。カーソルの点滅が速くなる。文字が細かく揺れる。
//                  → 旧波形の「暴れる」。
//     無感情（−）… 打った言葉が一文字ずつ消えていく。打鍵は止まり、消える一方。
//                  最後は**空の入力欄にカーソルだけ**が、遅く・弱く点滅している。
//                  → 旧波形の「フラットライン」。
//     ±90（危険域）… 激情：入力欄が炎上赤に縁取られて震える。無感情：入力欄そのものが薄れて消えかける。
//
//   言葉は**すべて既存の語彙の断片**（BossPostStory の下書き／docs/20260925 の危険信号／各 Boss の
//   RECLOSE・挑発）。ここで新しい台詞は書かない（WordsOf を参照）。
//
//   ── レイヤー ─────────────────────────────────────────────────
//   Hud は CanvasLayer＝常に最前面なので、そこには描けない（弾の上に文字が乗る）。
//   盤面の Z は 背景層 -95..-88 / ScrollFx -70..-55 / StageImagery -50 / WorldGrade -48,-47 /
//   MurkVignette -45 / 引用 -14 / 投稿チップ -12 / 弾 0 / 自機 10。
//   → この入力欄は **ZIndex=-44**（MurkVignette の1つ上・弾より遥かに奥）。第3稿と同じ。
//     ボス本体（Z>-44）は入力欄の**手前**に立つ＝自分の下書きを背負っている絵になる。
//   座標系は世界の 384×216 生座標（Hud の設計座標 1280×720 ではない）。
//
//   ── 弾の視認性（実測値・build/shots_fury3）────────────────────────
//   背景のど真ん中に文字を敷く以上、薄く・暗く。数値は弾幕が出ている実戦スクショで決めた:
//     ・本文は Zen Regular（細い）サイズ 7（世界座標＝1080p で約 20px）。α 0.40（凪）〜 0.50（激情）、
//       入力欄からはみ出した行だけ +0.06（上限 0.56）。
//     ・入力欄の地は暗色 α 0.12（背景を少し沈めるだけ＝弾のピンク・白はむしろ立つ）。縁は α ≤ 0.34。
//     ・カーソルは 1px 幅・α ≤ 0.68。危険域の赤縁でも α ≤ 0.55（外側の滲みは ≤ 0.32）。
//   弾（濃ピンク／白グリフ＋グロー）より明るい面はどこにも出来ない。
//
//   ── 作品のトーン ─────────────────────────────────────────────
//   見た目は QuoteStorm のピン留めカード（暗い地・細い青灰の縁・角丸 4）と OpeningFilm の下書き画面
//   （「下書き」見出し・シアンのカーソル）に揃える。原色・グラデ・反射は持ち込まない。
//
//   数値・挙動は一切持たない。GameManager.FuryValue / FuryActive / FuryBossId を**読むだけ**。
public partial class FuryDial : Node2D
{
    private const float W = 384f, H = 216f;

    // ── 配置 ───────────────────────────────────────────────────
    //   盤面（Field.Left=120..384）の中心・ボス徘徊ゾーンの中心高さ（第3稿の計器と同じ位置）。
    //   幅 180（盤面幅 264 の 68%）・高さ 52。3 行ぶんの本文＋見出し行が収まる。激情ではここから溢れる。
    private const float BoxW = 180f, BoxH = 52f;
    private static readonly Rect2 Box = new Rect2(Field.CenterX - BoxW * 0.5f, H * 0.46f - BoxH * 0.5f, BoxW, BoxH);
    private const float PadX = 8f;        // 本文の左余白
    private const float PadTop = 15f;     // 本文の上余白（見出し行のぶん）
    private const int BodySize = 7;       // 本文（384 系で読める最小に近い。6 だと弾幕の上で潰れた）
    private const float LineH = 9.5f;     // 行送り
    private const int LabelSize = 6;      // 「下書き」見出し
    private const int CalmLines = 3;      // 入力欄に収まる行数
    private const int RageLines = 6;      // 激情で溢れる最大行数（4〜6 行目は入力欄の下へはみ出す）
    private const int MaxChars = 160;     // バッファ上限（激情の度合いで 48→160。超えた分は先頭から落とす＝入力欄がスクロールした体）

    // 色（既存トークン／既存部品の色のみ。新色を作らない）。
    private static readonly Color Ink = new Color(0.86f, 0.88f, 0.96f);         // 本文（第3稿の文字盤の線と同じ）
    private static readonly Color Frame = new Color(0.62f, 0.70f, 0.92f);       // 縁（QuoteStorm のピン留めカード）
    private static readonly Color Paper = new Color(0.055f, 0.065f, 0.105f);    // 地（同上）
    private static readonly Color CaretCol = UiKit.Info;                        // カーソル（OpeningFilm の下書き画面）
    private static readonly Color Rage = UiKit.Burn;                            // 激情＝炎上赤
    private static readonly Color Numb = new Color("6f8ea6");                   // 無感情＝色の抜けた鈍青

    // ── 言葉（既存の語彙の断片だけ。新しい台詞は書かない）──────────────
    //   Calm : 0 で静かに打たれている本音。BossPostStory の下書き（Quote／最後の投稿の本音行）。
    //   Frag : 激情で割り込む反復向きの語。危険信号（docs/20260925 §2 上振れ）・RECLOSE・挑発から。
    private readonly record struct Words(string[] Calm, string[] Frag);
    private static Words WordsOf(string bossId) => bossId switch
    {
        // あかり: 下書き「おめでとう　ほんとだよ　元気でね／……好きだよ。いまも。」（BossPostStory）
        //         断片「返して」（RECLOSE「……返して。読んだなら、返してよ。」／+75「返して返して返して」）
        //             「見て」「こっち」「ねえ」（最終域「見て。ねえ、見て。こっち。こっち。こっち。」）
        //             「読んだでしょ」（+75「読んだでしょ。読んだんでしょ!?」）
        "akari" => new(new[] { "おめでとう　ほんとだよ　元気でね", "……好きだよ。いまも。" },
                       new[] { "返して", "見て", "こっち", "読んだでしょ", "ねえ" }),
        // こはる: 下書き「休んでも、好きでいたい。／でも今日は、もう休みたい。」（BossPostStory）
        //         断片「ちゃんと」（+75「ちゃんとする! ちゃんとするってば!」／スペル「ちゃんとしなきゃ」）
        //             「見てる」（+55「見てるよね? ……ねえ、見てるって言って」）
        //             「まだ見てない」（+88）「みんな見てる」（RECLOSE／スペル）「やめないで」（RECLOSE）
        "koharu" => new(new[] { "休んでも、好きでいたい。", "でも今日は、もう休みたい。" },
                        new[] { "ちゃんと", "見てる", "まだ見てない", "みんな見てる", "やめないで" }),
        // レイ: 下書き「好きな話を、聞いてほしい。／わたしの声を、聞いてほしい。」（BossPostStory）
        //       断片「見て」「ちゃんと見てよ」（挑発「戦ってよ。わたしを、ちゃんと見てよ。」）
        //           「見てた?」（+55「いま、見てた?」）「切り抜いて」（+75）「初見さん」（+88「初見さん! 初見さん!」）
        "rei" => new(new[] { "好きな話を、聞いてほしい。", "わたしの声を、聞いてほしい。" },
                     new[] { "見て", "切り抜いて", "初見さん", "ちゃんと見てよ", "見てた?" }),
        // ミナ（BossMina は BeginFury を呼ばない＝実際には出ない。保険として下書きだけ置く）。
        _ => new(new[] { "助けて。いっしょに帰りたい。" }, new[] { "助けて" }),
    };

    // ── 状態 ───────────────────────────────────────────────────
    private float _t;
    private float _fade;          // 表示の出入り（ボス戦の開始／終了で 0↔1 を 0.5s でなめらかに）
    private float _shown;         // 表示上の値（実値へ追従。文字の振る舞いが跳ねずに変わる）
    private GameManager? _game;
    private string _bossId = "";
    private Words _words = WordsOf("");

    // 入力欄の中身（打鍵の状態機械）。
    private readonly StringBuilder _buf = new();
    private int _ver;             // _buf の世代（行折りキャッシュの鍵）
    private string _src = "";     // いま打っている元文（_buf はこの先頭部分）
    private bool _srcRage;        // _src が激情の断片を含むか
    private int _calmIdx;         // 静かな一行の何番目か
    private float _typeAcc;       // 打鍵／削除の蓄積（1 で一文字）
    private float _holdT;         // 打ち終えて留まっている時間（凪）
    private float _clearFade = 1f;// 凪の周回で文字がそっと消える 1→0
    private float _churnT;        // 激情の「消して打ち直す」の間隔タイマー
    private int _erase;           // 残り削除文字数（>0 の間は打たずに消す）
    private float _caretPhase;    // カーソルの位相（周期が変わっても位相が跳ばない）
    private uint _seed = 7u;      // 決定的な擬似乱数（イベント時だけ進める＝毎フレーム揺れない）

    // 行折りのキャッシュ（_buf と折り返し幅が変わった時だけ作り直す）。
    private readonly List<string> _lines = new();
    private int _linesVer = -1;
    private int _linesWrapW = -1;
    private readonly Dictionary<char, float> _cw = new();

    // 画面全体の色連動は別ノードに持たせる（合成が Normal と Add で別だから）。
    private FuryWash _wash = null!;

    public override void _Ready()
    {
        ZIndex = -44;                 // MurkVignette(-45) の上・弾(0) の遥か奥
        ZAsRelative = false;
        AddToGroup("furydial");
        _game = GetNodeOrNull<GameManager>("/root/Game");

        // 色連動の板。入力欄より更に奥（-46）に敷く＝背景を染めるだけで、文字を濁らせない。
        _wash = new FuryWash { Name = "Wash", Host = this, ZIndex = -46, ZAsRelative = false };
        AddChild(_wash);
    }

    // ── 現在の状態（Wash も読む）───────────────────────────────
    public float Value => _shown;
    public float Fade => _fade;
    /// 激情側の度合い 0..1（+90 で 1）。
    public float RageK => Mathf.Clamp(Mathf.Max(0f, _shown) / Fury.RageEdge, 0f, 1f);
    /// 無感情側の度合い 0..1（-90 で 1）。
    public float NumbK => Mathf.Clamp(Mathf.Max(0f, -_shown) / -Fury.NumbEdge, 0f, 1f);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;

        bool on = _game is { FuryActive: true };
        _fade = Mathf.MoveToward(_fade, on ? 1f : 0f, dt * 2f);   // 0.5s で出入り
        if (on)
        {
            if (_bossId != _game!.FuryBossId) Rebind(_game.FuryBossId);
            // 表示値は実値へ指数追従（一拍遅れて着く＝文字の振る舞いが段差なく変わる）。
            float v = Mathf.Clamp(_game.FuryValue, Fury.Min, Fury.Max);
            _shown = Mathf.Lerp(_shown, v, 1f - Mathf.Exp(-6f * dt));
            Step(dt);
        }
        QueueRedraw();
        _wash.QueueRedraw();
    }

    // ボスが変わった（＝ボス戦が始まった）ら言葉を差し替えて、入力欄を空から始める。
    private void Rebind(string bossId)
    {
        _bossId = bossId;
        _words = WordsOf(bossId);
        _buf.Clear(); _ver++;
        _src = ""; _srcRage = false; _calmIdx = 0;
        _typeAcc = 0f; _holdT = 0f; _clearFade = 1f; _churnT = 0f; _erase = 0;
    }

    private uint Next() { _seed = _seed * 1664525u + 1013904223u; return _seed >> 8; }
    private int Rand(int n) => n <= 0 ? 0 : (int)(Next() % (uint)n);

    private void Append(char c) { _buf.Append(c); _ver++; }
    private void Backspace() { if (_buf.Length > 0) { _buf.Length--; _ver++; } }

    private static bool IsPause(char c) => c is '、' or '。' or '…' or '　' or '?' or '!';

    // ── 打鍵の状態機械 ─────────────────────────────────────────
    private void Step(float dt)
    {
        float v = _shown;
        float rage = RageK, numb = NumbK;
        // カーソルの周期：凪 1.1s ／激情で 0.28s まで速く／無感情で 2.8s まで遅く。
        float period = numb > 0.001f ? Mathf.Lerp(1.1f, 2.8f, numb) : Mathf.Lerp(1.1f, 0.28f, rage);
        _caretPhase += dt / period;

        if (v <= -2f) StepNumb(dt, numb);
        else StepType(dt, rage);
    }

    // 凪〜激情：打つ。
    private void StepType(float dt, float rage)
    {
        // 断片が割り込み始める境目（+20 前後）。ヒステリシス付き＝境目で行ったり来たりしない。
        bool wantRage = _srcRage ? rage >= 0.15f : rage >= 0.22f;
        string calm = _words.Calm[_calmIdx % _words.Calm.Length];

        // 元文の用意。凪→激情は「いま打っている本音の続きに断片が割り込む」（消さずに続ける）。
        //   激情→凪は「一度ぜんぶ消してから、本音を打ち直す」。
        if (_src.Length == 0 || (_erase == 0 && !calm.StartsWith(_buf.ToString()) && !_srcRage))
        {
            if (_buf.Length > 0 && !_src.StartsWith(_buf.ToString())) { _erase = _buf.Length; }
            else { _src = calm; _srcRage = false; }
        }
        if (wantRage && !_srcRage && _erase == 0)
        {
            _src = _buf.ToString() + BuildTail(rage);
            _srcRage = true; _holdT = 0f; _clearFade = 1f;
        }
        else if (!wantRage && _srcRage && _erase == 0)
        {
            // 激情から戻る：残った断片を（速めに）消し切ってから本音へ。
            _erase = _buf.Length; _srcRage = false;
            _src = calm;
        }

        // 削除中（激情の打ち直し／激情から戻るとき）。
        if (_erase > 0)
        {
            _typeAcc += dt * 26f;
            while (_typeAcc >= 1f && _erase > 0) { Backspace(); _erase--; _typeAcc -= 1f; }
            if (_erase == 0)
            {
                _typeAcc = 0f;
                if (_srcRage) _src = _buf.ToString() + BuildTail(rage);
                else _src = calm;
                if (!_src.StartsWith(_buf.ToString())) { _buf.Clear(); _ver++; }
            }
            return;
        }

        // 打ち終えている。
        if (_buf.Length >= _src.Length)
        {
            if (_srcRage)
            {
                // 激情：留まらない。数文字消して打ち直す（「消しては打ち直す」）。
                _erase = 2 + Rand(3 + (int)(rage * 8f));
                if (_buf.Length > CharCap(rage)) TrimHead();
            }
            else
            {
                // 凪：しばらく留まり、そっと消えて、次の一行へ。
                _holdT += dt;
                if (_holdT > 3.6f)
                {
                    _clearFade = Mathf.Max(0f, _clearFade - dt / 0.7f);
                    if (_clearFade <= 0f)
                    {
                        _buf.Clear(); _ver++;
                        _calmIdx++; _src = ""; _holdT = 0f; _clearFade = 1f; _typeAcc = 0f;
                    }
                }
            }
            return;
        }

        // 打鍵。凪 4.5 字/s → 激情 22 字/s。句読点のあとで息をつく（激情ほど息をつかない）。
        float cps = Mathf.Lerp(4.5f, 22f, rage);
        _typeAcc += dt * cps;
        while (_typeAcc >= 1f && _buf.Length < _src.Length)
        {
            char c = _src[_buf.Length];
            Append(c);
            _typeAcc -= 1f;
            if (IsPause(c)) _typeAcc -= Mathf.Lerp(2.4f, 0.2f, rage);
        }
        if (_buf.Length > CharCap(rage)) TrimHead();

        // 激情：打っている途中でも、ときどき数文字消して打ち直す。
        if (_srcRage && rage > 0.25f)
        {
            _churnT += dt;
            float every = Mathf.Lerp(3.0f, 0.7f, rage);
            if (_churnT > every)
            {
                _churnT = 0f;
                _erase = 1 + Rand(2 + (int)(rage * 5f));
            }
        }
    }

    // 無感情：打たない。消える一方。値が戻れば、本音がまた（ゆっくり）打たれる。
    private void StepNumb(float dt, float numb)
    {
        string calm = _words.Calm[_calmIdx % _words.Calm.Length];
        _holdT = 0f; _clearFade = 1f; _erase = 0; _srcRage = false; _src = calm;
        string cur = _buf.ToString();

        if (!calm.StartsWith(cur))
        {
            // 本音以外（激情の断片）が残っていれば、それを先に消す。
            _typeAcc += dt * 6f;
            while (_typeAcc >= 1f && _buf.Length > 0 && !calm.StartsWith(_buf.ToString())) { Backspace(); _typeAcc -= 1f; }
            return;
        }

        // 残す長さ：-45 で四割、-90 で 0。
        int target = Mathf.RoundToInt(calm.Length * Mathf.Pow(1f - numb, 1.3f));
        if (numb >= 0.985f) target = 0;
        if (_buf.Length > target)
        {
            _typeAcc += dt * Mathf.Lerp(0.8f, 2.6f, numb);      // 消える速さ：端ほど早い
            while (_typeAcc >= 1f && _buf.Length > target) { Backspace(); _typeAcc -= 1f; }
        }
        else if (_buf.Length < target)
        {
            _typeAcc += dt * 2.0f;                               // 戻るときは、ゆっくり打ち直す
            while (_typeAcc >= 1f && _buf.Length < target) { Append(calm[_buf.Length]); _typeAcc -= 1f; }
        }
    }

    // 激情の断片を組む。本音の続きに、反復向きの語が割り込む。
    //   度合いが上がるほど：フレーズ数↑・同じ語の連打↑・句読点が抜ける・濁点だけ（゛゛）・一文字で止まる。
    private string BuildTail(float rage)
    {
        var sb = new StringBuilder();
        int n = 1 + (int)(rage * 8f);                       // 1..9 フレーズ
        if (_buf.Length > 0 && rage < 0.5f) sb.Append("、");
        for (int i = 0; i < n; i++)
        {
            string frag = _words.Frag[Rand(_words.Frag.Length)];
            int rep = 1 + (int)(rage * 2.6f) + Rand(2);     // 1..4 連打
            for (int r = 0; r < rep; r++) sb.Append(frag);
            if (rage < 0.45f) sb.Append("。");
            else if (rage < 0.75f) sb.Append(Rand(2) == 0 ? "、" : "");
            if (rage > 0.6f && Rand(4) == 0) sb.Append("゛゛");
            if (rage > 0.8f && Rand(3) == 0) sb.Append(frag[0]);
        }
        return sb.ToString();
    }

    // 激情の度合いで、入力欄に溜まる文字数の上限を変える（+20 で 48 字≒2 行、+90 で 160 字≒6 行）。
    //   打鍵は激情の間ずっと続くので、上限が無いと +30 でも +90 でも同じだけ溢れてしまう。
    private static int CharCap(float rage) => Mathf.RoundToInt(Mathf.Lerp(48f, MaxChars, rage));

    // バッファが長すぎる：先頭の一行ぶんを落とす（入力欄が上へスクロールした体）。
    private void TrimHead()
    {
        int drop = Mathf.Min(_buf.Length, 24);
        _buf.Remove(0, drop); _ver++;
        // _buf は _src の先頭部分なので、_src も同じだけ頭を落とせば関係が保たれる。
        _src = _src.Length > drop ? _src.Substring(drop) : "";
        if (!_src.StartsWith(_buf.ToString())) _src = _buf.ToString();
    }

    // ── 行折り ─────────────────────────────────────────────────
    private float CharW(Font f, char c)
    {
        if (!_cw.TryGetValue(c, out float w))
        {
            w = f.GetStringSize(c.ToString(), HorizontalAlignment.Left, -1, BodySize).X;
            _cw[c] = w;
        }
        return w;
    }

    private float LineW(Font f, string s)
    {
        float w = 0f;
        foreach (char c in s) w += CharW(f, c);
        return w;
    }

    // 日本語なので単語境界は使わず一文字ずつ詰める。折り返し幅が変わった時だけ作り直す。
    private void Wrap(Font f, int wrapW)
    {
        if (_linesVer == _ver && _linesWrapW == wrapW) return;
        _linesVer = _ver; _linesWrapW = wrapW;
        _lines.Clear();
        var cur = new StringBuilder();
        float w = 0f;
        string s = _buf.ToString();
        foreach (char c in s)
        {
            float cw = CharW(f, c);
            if (w + cw > wrapW && cur.Length > 0)
            {
                _lines.Add(cur.ToString());
                cur.Clear(); w = 0f;
            }
            cur.Append(c); w += cw;
        }
        _lines.Add(cur.ToString());   // 空でも1行（カーソルの居場所）
    }

    // ── 描画 ───────────────────────────────────────────────────
    public override void _Draw()
    {
        if (_fade <= 0.01f) return;
        var f = UiKit.Zen;
        if (f == null) return;

        float a = _fade;
        float v = Mathf.Clamp(_shown, Fury.Min, Fury.Max);
        float rage = RageK, numb = NumbK;
        // 危険域（±90）に入った度合い。88→92 で立ち上がる＝「線を越えた」がはっきり分かる。
        float rageEdge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((v - 88f) / 4f, 0f, 1f));
        float numbEdge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((-v - 88f) / 4f, 0f, 1f));
        float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 9f);
        float breath = 0.5f + 0.5f * Mathf.Sin(_t * 1.6f);

        // 危険域（激情）：入力欄ごと震える。
        Vector2 shake = rageEdge > 0.001f
            ? new Vector2(Mathf.Sin(_t * 41f) * 1.1f, Mathf.Cos(_t * 31f) * 0.7f) * rageEdge
            : Vector2.Zero;
        Rect2 box = new Rect2(Box.Position + shake, Box.Size);

        // ── 入力欄の地と縁 ──
        //   縁：凪 0.26 → 激情 0.34（危険域は炎上赤で脈打ち 0.40..0.55）／無感情は薄れて 0.12、危険域では
        //   消えかける（0.05..0.20 でゆっくり呼吸＝縁が消えては戻る。空の欄とカーソルだけが残る）。
        float frameA = Mathf.Lerp(0.26f, 0.34f, rage) * (1f - 0.55f * numb);
        Color frameC = Frame.Lerp(Rage, rage * 0.7f).Lerp(Numb, numb);
        if (rageEdge > 0.001f) { frameA = Mathf.Lerp(frameA, 0.40f + 0.15f * pulse, rageEdge); frameC = frameC.Lerp(Rage, rageEdge); }
        if (numbEdge > 0.001f) frameA = Mathf.Lerp(frameA, 0.05f + 0.15f * breath, numbEdge);
        float paperA = 0.12f * (1f - 0.6f * numb) * (1f - 0.5f * numbEdge);
        UiKit.Box(this, box, new Color(Paper, paperA * a), 4f, new Color(frameC, frameA * a), 1f);
        // 危険域（激情）：縁の外側にもう一段、赤い縁が滲む（塗りは持たない）。
        if (rageEdge > 0.001f)
            UiKit.Box(this, box.Grow(1.5f), null, 5f, new Color(Rage, (0.18f + 0.14f * pulse) * rageEdge * a), 1f);

        // ── 見出し「下書き」──
        float labelA = 0.50f * (1f - 0.60f * numb) * a;
        if (numbEdge > 0.001f) labelA *= (1f - 0.25f * numbEdge);
        Color labelC = Frame.Lerp(Rage, 0.5f * rage).Lerp(Numb, numb);
        DrawString(f, box.Position + new Vector2(6f, 4f + f.GetAscent(LabelSize)), "下書き",
            HorizontalAlignment.Left, -1, LabelSize, new Color(labelC, labelA));

        // ── 本文 ──
        //   折り返し幅：凪では入力欄に収まる／激情では右へ最大 36px はみ出す。行数も 3 → 6 へ溢れる。
        int wrapW = Mathf.RoundToInt(BoxW - PadX * 2f + rage * 36f);
        Wrap(f, wrapW);
        int maxLines = CalmLines + (int)(rage * 3.99f);
        int first = Mathf.Max(0, _lines.Count - maxLines);
        float textA = Mathf.Lerp(0.40f, 0.50f, rage) * (1f - 0.45f * numb) * _clearFade * a;
        Color textC = Ink.Lerp(Rage, 0.45f * rage).Lerp(Numb, numb);
        float x0 = box.Position.X + PadX;
        float y0 = box.Position.Y + PadTop;
        float ascent = f.GetAscent(BodySize);
        float jitterK = Mathf.Clamp((rage - 0.4f) / 0.6f, 0f, 1f);    // +36 を過ぎたあたりから文字が揺れ始める
        int shown = 0;
        string last = "";
        for (int i = first; i < _lines.Count; i++, shown++)
        {
            string line = _lines[i];
            last = line;
            float dx = jitterK > 0f ? Mathf.Sin(_t * 23f + shown * 1.7f) * 0.9f * jitterK : 0f;
            float dy = jitterK > 0f ? Mathf.Cos(_t * 17f + shown * 2.3f) * 0.4f * jitterK : 0f;
            // 入力欄からはみ出した行（4 行目以降）は少し濃く＝溢れている側が目に入る。
            float la = shown >= CalmLines ? Mathf.Min(0.56f, textA + 0.06f) : textA;
            if (line.Length > 0)
                DrawString(f, new Vector2(x0 + dx, y0 + shown * LineH + ascent + dy), line,
                    HorizontalAlignment.Left, -1, BodySize, new Color(textC, la));
        }

        // ── カーソル ──
        //   最後の行の末尾で点滅。凪 1.1s／激情 0.28s／無感情 2.8s（位相は Step で積んでいる）。
        //   無感情の端では、空の入力欄にこれだけが遅く弱く残る。
        float duty = numb > 0.001f ? 0.45f : 0.55f;
        bool on = (_caretPhase - Mathf.Floor(_caretPhase)) < duty;
        if (on)
        {
            int li = Mathf.Max(0, shown - 1);
            float cx = x0 + LineW(f, last) + 1f;
            float cy = y0 + li * LineH;
            float caretA = (0.58f + 0.10f * rage) * (1f - 0.55f * numb) * a;
            if (numbEdge > 0.001f) caretA = Mathf.Lerp(caretA, 0.52f, numbEdge);
            Color cc = CaretCol.Lerp(Rage, 0.6f * rage).Lerp(Numb, 0.45f * numb);
            DrawLine(new Vector2(cx, cy + 1f), new Vector2(cx, cy + 8.5f), new Color(cc, caretA), 1f, true);
        }
    }
}

// FuryWash : 【激情】の**画面全体の色**（このメーターでいちばん大事な部分）。
//
//   プレイヤーが計器を見ていなくても、視界の色だけで今どちらに傾いているか分かること。
//     激情へ寄るほど …… 画面が赤く灼ける（端ほど濃い＝視界が炎上に囲まれる）
//     無感情へ寄るほど… 色が抜けて青白く冷える（彩度が落ちた冷光が端から寄る）
//
//   やり方は既存の MurkVignette / WorldGrade と同じ「中央を抜いた同心リング」。
//   ・中央（±ClearR=60px＝主戦場）は端αの 0.25 倍までしか乗らない＝弾の上に色が溜まらない。
//   ・端でも α ≤ 0.17（激情）/ 0.13（無感情）。MurkVignette の上限 0.21 より薄い。
//   ・激情は Normal 合成の赤み（暗く締まる＝背景イラストを白飛びさせない）、
//     無感情は**彩度を抜く**ことが本体なので、青白をごく薄く Normal で乗せて色を殺す。
//   ・どちらも ±25（安全域）の内側では完全に 0＝普段の画面はまったく変わらない。
//
//   ZIndex=-46：背景・グレーディング・MurkVignette(-45) より奥ではなく“間”に置く。
//   弾(0)・自機(10)・HUD の遥か奥＝弾の色に干渉しない。
public partial class FuryWash : Node2D
{
    private const float W = 384f, H = 216f;
    private static readonly Vector2 C = new Vector2(W * 0.5f, H * 0.5f);
    private const float ClearR = 60f;     // 主戦場。ここより内は端αの CenterMul 倍
    private const float FullR = 236f;
    private const float CenterMul = 0.25f;

    // 端での上限α（実機スクショで詰めた値。これ以上だと弾のピンクが背景に溶ける）。
    private const float RageEdgeA = 0.26f;
    private const float NumbEdgeA = 0.20f;
    // ここより内側では何も乗せない＝保てている間は画面がまったく変わらない。
    //   14 は「安全域(±25)の少し手前」。実測（build/shots_fury2）で ±25 始まり・二乗だと
    //   ±45 で k=0.07 ＝画面がまったく変わらず、中盤に手がかりが無かった。
    private const float DeadZone = 14f;

    private static readonly Color RageTint = new Color(0.88f, 0.10f, 0.12f); // 灼ける赤（暗めで締まる）
    private static readonly Color NumbTint = new Color(0.70f, 0.78f, 0.90f); // 色の抜けた冷光

    public FuryDial Host = null!;

    public override void _Draw()
    {
        if (Host == null || Host.Fade <= 0.01f) return;
        float v = Host.Value;
        // DeadZone の外から効き始め、端(±100)で最大。1.5乗＝中盤(±45)でも薄く色が乗り、
        //   端(±90)で一気に濃くなる（二乗だと ±45 で k=0.07 ＝ 何も起きていないように見えた）。
        float k = Mathf.Clamp((Mathf.Abs(v) - DeadZone) / (Fury.Max - DeadZone), 0f, 1f);
        if (k <= 0.001f) return;
        k = k * Mathf.Sqrt(k);
        bool rage = v > 0f;
        float edgeA = (rage ? RageEdgeA : NumbEdgeA) * k * Host.Fade;
        if (edgeA <= 0.002f) return;
        Overlay(rage ? RageTint : NumbTint, edgeA);
    }

    // 中央を薄く抜いた同心リングで全画面に色を乗せる（MurkVignette / WorldGrade と同法）。
    private void Overlay(Color col, float edgeA)
    {
        const int rings = 12;
        for (int i = 0; i < rings; i++)
        {
            float t1 = (float)(i + 1) / rings;
            float r0 = Mathf.Lerp(ClearR, FullR, (float)i / rings);
            float r1 = Mathf.Lerp(ClearR, FullR, t1);
            float a = edgeA * Mathf.Lerp(CenterMul, 1f, t1);
            if (a <= 0.001f) continue;
            Ring(r0, r1, new Color(col, a));
        }
        // 中央のコアにも CenterMul ぶんだけ届かせる（帯の継ぎ目が見えないように）。
        float ca = edgeA * CenterMul;
        if (ca > 0.001f)
            DrawRect(new Rect2(C.X - ClearR, C.Y - ClearR, ClearR * 2f, ClearR * 2f), new Color(col, ca));
    }

    private void Ring(float r0, float r1, Color col)
    {
        float l0 = C.X - r0, r0x = C.X + r0, t0 = C.Y - r0, b0 = C.Y + r0;
        float l1 = C.X - r1, r1x = C.X + r1, t1 = C.Y - r1, b1 = C.Y + r1;
        DrawRect(new Rect2(l1, t1, r1x - l1, t0 - t1), col);
        DrawRect(new Rect2(l1, b0, r1x - l1, b1 - b0), col);
        DrawRect(new Rect2(l1, t0, l0 - l1, b0 - t0), col);
        DrawRect(new Rect2(r0x, t0, r1x - r0x, b0 - t0), col);
    }
}
