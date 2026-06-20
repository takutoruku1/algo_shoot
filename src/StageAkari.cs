using Godot;

// StageAkari : STAGE1「あかり（雨の教室）」進行。
//   1: 導入会話（少年＝声/テロップ、ミナ＝立ち絵で毒舌）
//   2: あかりボス出現
//   3: ボス戦（自責の弾雨＋あかりの自責弾。浄化＝改心で会話完了まで）
//   4: クリア（灯がともる）
// ボス戦中は天井の自責の雨が降り続ける（会話中は止む）。
public partial class StageAkari : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private int _step;
    private bool _stepStarted;
    private double _stepTime;
    private double _stageElapsed;   // ステージ全体の経過秒（クリア確定まで・ポーズ中は止まる）。
    private float _clearTime;       // クリア確定時の経過秒。
    private double _lineHold;   // 行表示からの経過（誤連打防止の最小表示時間用）
    private int _introLine;
    private bool _zHeld;
    private bool _zEdge;
    private BossAkari _boss = null!;
    private bool _bossActive;
    private double _rainT;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    private const float SpawnX = 300f;

    private const string SCocky = "res://char/shonen_face.png";
    private const string SGentle = "res://char/shonen_gentle.png";

    // 道中ザコ戦（Spawner）。Intro後・ボス前に挿入。前半→ボスのチラ見せ→後半 の二部構成。
    private Spawner _spawner = null!;
    private int _waveBase;
    private const int MidWaveA = 6;
    private const int MidWaveB = 6;
    // ボスの“チラ見せ”（カメオ）＝短いミニボス戦。あかり＝怯え・自責で、攻撃も悲嘆寄り。
    // 退場はHP駆動のみ（_cameoHit.Escaped）。会話は一行オーバーレイ(ShowBossLine)で挟むだけ＝弾も移動も止めない。
    private Sprite2D _cameoSprite = null!;
    private CameoHit _cameoHit = null!; // カメオの被弾判定（撃つと当たる／削り切りで離脱）
    private int _cameoPhase;            // 0=登場 1=戦闘ループ 2=捨て台詞 3=逃走
    private double _cameoT;             // フェーズ内の経過秒
    private double _cameoBattleT;       // 戦闘ループ全体の経過（保険タイマー用）
    private double _cameoVolleyT;       // 次の一斉射までのクールタイム
    private int _cameoPostLine;         // 捨て台詞の表示中インデックス
    private bool _cameoGaugeTaunt;      // 1ゲージ割った時の挑発を出し終えたか
    private double _cameoTauntT;        // 定期挑発のクールタイム
    private double _cameoBobT;

    // ダイブ前の会話（v2 [P-02a]）。少年の様子が普段と違う＝核心の予兆。
    // who: 0=少年 / 1=ミナ / 2=相手 / 3=地の文 / 4=投稿 / 5=中継。
    private static readonly (int who, string text, string face)[] Intro =
    {
        (1, "ご主人様。次の“成敗”は? 今日はずいぶん静かですね。", ""),
        (0, "……ああ、悪い。ちょっと考えごとだ。", SGentle),
        (4, "「すき、すき、すき。……ひとつでいいから、本物になって。」", ""),     // 投稿
        (0, "…………この人の、ところへ行こう。", SGentle),
        (1, "おや。決めゼリフはどうしたんですか。……それに、いつもの「Stay」も。", ""),
        (0, "……いいから。行くぞ。", SCocky),
        // 合言葉の“不在”＝違和感（転の予兆）。少年は動揺して、いつもの「Stay」を言い忘れる。
        (3, "——その日、ご主人様は「Stay」と言わなかった。わたくしは、なぜか少しだけ、落ち着きませんでした。", ""),
        (3, "——雨の、降りやまない教室でした。机も椅子も、天井へ落ちていく。", ""),   // 地
    };

    // ボス登場時の説明（v2 [P-02b]。who: 0=少年 / 1=ミナ）
    private static readonly (int who, string text, string face)[] BossIntro =
    {
        (1, "黒板も、机も、窓も……ぜんぶ「すき」で、埋め尽くされていますね。", ""),
        (0, "……この人は、好きという気持ちを、持て余してる。誰にも渡せないまま。", SGentle),
        (0, "————渡せなかった想いってのは、ああやって、あふれるんだ。この世界ではね。", SGentle),
    };

    // 道中突入の小話（世界観：自責の声の雨）。道中ザコ戦の前に出す。
    private static readonly (int who, string text, string face)[] Mid =
    {
        (3, "——その雨の中を、白い吹き出しが、いくつも漂っていました。", ""),
        (1, "ここの声は……どれも、「ねえ見て」「すき」と、すがりついてきます。", ""),
        (0, "ああ。たった一人に渡せなかったぶん、誰彼かまわず掴もうとしてる。", SGentle),
        (1, "……やけに、詳しいんですね。", "res://char/mina_worried.png"),
        (0, "————行くぞ。", SCocky),
    };

    // 道中“前半”の後：ボスのツイートが流れてくる→考察（少年が思わず情を見せる＝転の核）。
    private const string AFace = "res://char/akari_face.png";
    private static readonly (int who, string text, string face)[] BossTalk =
    {
        (4, "「すき、すき、すき。……ひとつでいいから、本物になって。」", ""), // ボスのツイート
        (1, "……また、あの投稿が流れてきました。奥の“本人”は、ずいぶん思いつめていますね。", ""),
        (0, "……あかり、っていうんだ。やさしくて、笑うと、目が三日月になる。", SGentle),
        (1, "（また、知っている人のように——）……ご主人様?", "res://char/mina_worried.png"),
        (0, "……なんでもない。行くぞ。", SCocky),
    };

    // チラ見せ：登場（あかり＝怯え・拒絶）。who=2=あかり。
    private static readonly (int who, string text, string face)[] CameoTalk1 =
    {
        (2, "……だれ? ……来ないで。あたしに、近づいちゃ、だめ。", AFace),
        (1, "ずいぶん、怯えていますね。", ""),
        (0, "……刺激するな、ミナ。この子は、自分を責めることしか、できないんだ。", SGentle),
        (2, "ごめんなさい……ごめんなさい。あたしなんかが、すきになって……ごめんなさい。", AFace),
    };
    private static readonly (int who, string text, string face)[] CameoTalk2 =
    {
        (2, "見ないで……っ。こんな、みっともないところ。", AFace),
        (1, "……ご主人様。あなた、さっきから、この子を見る目が——", "res://char/mina_worried.png"),
        (0, "……黙っててくれ、ミナ。頼むから。", SGentle),
    };
    // 山：あかりが少年の声に気づきかけ、自分で否定する。
    private static readonly (int who, string text, string face)[] CameoTalk3 =
    {
        (2, "……ねえ。あなたの声……どこかで、聞いた気が、する。", AFace),
        (0, "————。", SGentle),                                    // 沈黙
        (2, "……ううん。気のせい。あたしに、優しくしてくれる人なんて……いない。", AFace),
        (0, "————っ。……行くぞ、ミナ。今は、まだ。", SGentle),
    };
    private static readonly (int who, string text, string face)[] CameoPost =
    {
        (2, "ごめんね。ごめんね。……もう、見ないで。", AFace),
        (3, "——あかりは、降りやまない雨の奥へ、逃げるように消えていきました。", ""),
        (1, "……ご主人様。今の人を、放っておいて、いいんですか。", ""),
        (0, "……いいわけ、ないだろ。だから——奥で、ちゃんと、届けるんだ。", SGentle),
    };

    // 道中後の小話（ボスへの引き）。
    private static readonly (int who, string text, string face)[] MidEnd =
    {
        (1, "黒板の奥に、あの子が。……ご主人様、ほんとうに、いいんですね?", ""),
        (0, "……ぼくが、やらなきゃいけないんだ。", SGentle),
    };

    // 帰還（v2 [P-02c]）。投稿の変化＋あかりの残響＋ミナの核心の問い（伏線③）。
    private static readonly (int who, string text, string face)[] Clear =
    {
        (4, "「ほんと、バカなんだから。……あたしも、だけど。」", ""),       // 投稿が変化
        (2, "……あったかい声が、した。……なんでかな、あの人の声に、似てた。", ""), // あかり残響
        (1, "あなた、この人を——知ってるんですか?", "res://char/mina_worried.png"),
        (0, "…………まさか。赤の他人さ。", SCocky),
        (1, "……即答までに、二秒かかりましたね。", ""),
        (0, "ミナ。シェイクスピアは言った。\"Parting is such sweet sorrow.\"", SCocky),
        (1, "はいはい、教養アピールお疲れさまですね。……で、それは誰の話ですか。", "res://char/mina_smile.png"),
        (0, "————一般論だよ。", SGentle),
    };

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        // 道中（前半＋後半）＋ボスで浄化カプセルが満ちる（部屋が晴れる）。
        GetNodeOrNull<GameManager>("/root/Game")?.SetStageTarget(MidWaveA + MidWaveB + 1);
    }

    private bool _startBannerShown;

    public override void _Process(double delta)
    {
        _stepTime += delta;
        _lineHold += delta;
        // ステージ経過タイム：クリア確定まで積算しHUDへ反映。
        if (!_clearing) { _stageElapsed += delta; Hud?.SetElapsed((float)_stageElapsed); }
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowBanner("STAGE 2 START"); }
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        _zEdge = z && !_zHeld;
        _zHeld = z;
        switch (_step)
        {
            case 1: Step_Lines(delta, Intro); break;
            case 2: Step_Lines(delta, Mid); break;        // 道中突入の小話
            case 3: Step_MidwaveA(delta); break;          // 道中ザコ戦（前半）
            case 4: Step_Lines(delta, BossTalk); break;   // ボスのツイート→考察
            case 5: Step_BossCameo(delta); break;         // ボスのチラ見せ
            case 6: Step_MidwaveB(delta); break;          // 道中ザコ戦（後半）
            case 7: Step_Lines(delta, MidEnd); break;     // 道中後の小話
            case 8: Step_BossSpawn(); break;
            case 9: Step_Lines(delta, BossIntro); break;  // ボスは出現済みだが会話中は止まる
            case 10: Step_BossWait(); break;
            case 11: Step_Clear(delta); break;
            case 12: Step_Transition(); break;
        }
        if (_bossActive) Rain(delta);
    }

    private void Advance()
    {
        _step++;
        _stepStarted = false;
        _stepTime = 0;
    }

    // ---- 会話ステップ（配列を順に流す。Zで手動送り。会話中は弾が止まる） ----
    private void Step_Lines(double delta, (int who, string text, string face)[] lines)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _introLine = 0;
            _lineHold = 0;
            if (lines.Length == 0) { Advance(); return; }
            Hud.HoldBubble = true; // 自動で消えない＝手動送り
            ShowLine(lines);
        }
        if (_zEdge && _lineHold >= 0.15 && !Hud.DialogRevealed)
        {
            Hud.RevealDialogNow();   // 1段目：まず全文表示（読み飛ばし防止）
            _lineHold = 0;
        }
        else if (_lineHold >= 0.15 && Hud.DialogRevealed
                 && (_zEdge || (Hud.AutoAdvance && _lineHold >= 1.4)))
        {
            _lineHold = 0;
            _introLine++;
            if (_introLine >= lines.Length)
            {
                Hud.HoldBubble = false;
                Hud.HideBubble();
                Advance();
                return;
            }
            ShowLine(lines);
        }
    }

    private void ShowLine((int who, string text, string face)[] lines)
    {
        var (who, text, face) = lines[_introLine];
        var kind = (Hud.LineKind)who;
        string portrait = kind switch
        {
            Hud.LineKind.Boy => face,                       // 少年（行ごとの表情）
            Hud.LineKind.Other => "res://char/akari_face.png", // あかり
            Hud.LineKind.Mina => string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face, // ミナも行ごと表情
            _ => "res://char/mina_face.png",                // 中継ほか
        };
        Hud.ShowDialog(kind, text, portrait, otherName: "あかり");
    }

    // ---- 道中ザコ戦“前半”：Spawner起動→MidWaveA体浄化でチラ見せへ ----
    private void Step_MidwaveA(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            StartMidwaveSpawner();
        }
        if (game != null && game.PurifiedCount - _waveBase >= MidWaveA)
        {
            _spawner?.Stop(); _spawner = null!;
            ClearStageEnemies();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll(); // チラ見せ前に片付ける
            Advance();
        }
    }

    // ---- 道中ザコ戦“後半”：チラ見せの後。MidWaveB体浄化で本ボスへ ----
    private void Step_MidwaveB(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            StartMidwaveSpawner();
        }
        if (game != null && game.PurifiedCount - _waveBase >= MidWaveB)
        {
            _spawner?.Stop(); _spawner = null!;
            ClearStageEnemies();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Advance();
        }
    }

    private void StartMidwaveSpawner()
    {
        if (_spawner != null) return;
        _spawner = new Spawner { Name = "Spawner", World = World, Theme = StageTheme.Akari };
        AddChild(_spawner);
        _spawner.Begin();
    }

    private void ClearStageEnemies()
    {
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e) e.QueueFree();
    }

    // ---- ボスの“チラ見せ”：登場→（怯えの掛け合い→悲嘆の弾）×3→『見ないで』→逃走 ----
    private void Step_BossCameo(double delta)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _cameoPhase = 0; _cameoT = 0; _cameoGaugeTaunt = false; _cameoPostLine = -1;
            var tex = ResourceLoader.Load<Texture2D>("res://char/enemy_akari_pre.png");
            _cameoSprite = new Sprite2D { Name = "AkariCameo", Texture = tex, Centered = true, FlipH = true,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear, ZIndex = 5 };
            if (tex != null) { float s = 64f / tex.GetHeight(); _cameoSprite.Scale = new Vector2(s, s); }
            World.AddChild(_cameoSprite);
            _cameoSprite.GlobalPosition = new Vector2(440f, 78f);
            _cameoHit = new CameoHit { Name = "AkariCameoHit" };
            World.AddChild(_cameoHit);
            _cameoHit.Bind(_cameoSprite);
            // カメオ用ボスバー（本戦ボスと同じ複数ゲージ式）を出す。本ボス前なので時系列は重ならない。
            Hud.ShowBossBar("あかり", "@akari.");
            Hud.UpdateBossBar(_cameoHit.BarIndex, _cameoHit.TotalBars, _cameoHit.BarFrac);
        }

        // 被弾判定の追従＋早期離脱判定（2ゲージ削り切ったら捨て台詞→逃走へ）＋ボスバー反映。
        if (IsInstanceValid(_cameoHit))
        {
            _cameoHit.Tick(delta);
            Hud.UpdateBossBar(_cameoHit.BarIndex, _cameoHit.TotalBars, _cameoHit.BarFrac);
        }
        // 戦闘ループ中(phase1)に2ゲージ削り切ったら捨て台詞→逃走へ。
        if (_cameoPhase == 1 && IsInstanceValid(_cameoHit) && _cameoHit.Escaped)
            BeginCameoExit();

        // 登場→戦闘ループ→捨て台詞→逃走。会話で戦闘を止めない（ShowBossLineの一行オーバーレイのみ）。
        switch (_cameoPhase)
        {
            case 0: // スライド登場＋第一声（しっかり見せる演出）
                _cameoT += delta;
                float ki = Mathf.Min(1f, (float)_cameoT / 0.6f);
                _cameoSprite.GlobalPosition = new Vector2(Mathf.Lerp(440f, 300f, (float)Mathf.Ease(ki, 0.4f)), 78f);
                CameoBob();
                if (ki >= 1f && _cameoT > 0.6 && !_cameoGaugeTaunt)
                {
                    _cameoGaugeTaunt = true;
                    Hud.ShowBossLine("あかり", CameoHit.FirstBossLine(CameoTalk1), UiKit.Kegare, 2.6);
                }
                if (ki >= 1f && _cameoT > 1.0)
                {
                    _cameoPhase = 1; _cameoT = 0;
                    _cameoBattleT = 0; _cameoVolleyT = 0.2; _cameoTauntT = 6.0; _cameoGaugeTaunt = false;
                }
                break;
            case 1: CameoBattleLoop(delta); break;                  // 戦闘ループ（撃ち合い）
            case 2: CameoPostTalk(delta); break;                    // 捨て台詞（一行オーバーレイで順送り）
            case 3: // 逃走（奥へスライド＋フェード）
                _cameoT += delta;
                float ko = Mathf.Min(1f, (float)_cameoT / 0.8f);
                _cameoSprite.GlobalPosition = new Vector2(Mathf.Lerp(300f, 470f, ko), Mathf.Lerp(78f, 16f, ko));
                _cameoSprite.SelfModulate = new Color(1f, 1f, 1f, 1f - ko);
                if (ko >= 1f)
                {
                    _cameoSprite.QueueFree();
                    if (IsInstanceValid(_cameoHit)) _cameoHit.QueueFree(); // 残留判定を残さない
                    Hud.HideBossBar();                                     // バー出っ放しにしない（後で本ボスが再表示）
                    GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                    Advance();
                }
                break;
        }
    }

    // 戦闘ループ：弾幕を一定間隔で撃ち続け、ゲージの節目で挑発を一行差し込む。弾も移動も止めない。
    // 退場はHP駆動(BeginCameoExit)のみ。保険タイマー(CameoHit.SafetySec)超過でも退場（詰まらせない）。
    private void CameoBattleLoop(double delta)
    {
        CameoBob();
        _cameoBattleT += delta;
        _cameoVolleyT -= delta;
        _cameoTauntT -= delta;

        int kind = IsInstanceValid(_cameoHit) ? (CameoHit.CameoBars - 1 - _cameoHit.BarIndex) : 0;
        if (_cameoBattleT > 14.0) kind = Mathf.Max(kind, 2);
        kind = Mathf.Clamp(kind, 0, 2);

        if (_cameoVolleyT <= 0)
        {
            CameoFireVolley(kind);
            _cameoVolleyT = 1.4 - kind * 0.2;
        }

        if (!_cameoGaugeTaunt && IsInstanceValid(_cameoHit) && _cameoHit.BarIndex < CameoHit.CameoBars - 1)
        {
            _cameoGaugeTaunt = true;
            Hud.ShowBossLine("あかり", CameoHit.FirstBossLine(CameoTalk2), UiKit.Kegare, 2.4);
            _cameoTauntT = 5.0;
        }
        else if (_cameoTauntT <= 0)
        {
            Hud.ShowBossLine("あかり", CameoHit.FirstBossLine(CameoTalk3), UiKit.Kegare, 2.4);
            _cameoTauntT = 7.0;
        }

        if (_cameoBattleT > CameoHit.SafetySec) BeginCameoExit();
    }

    // 戦闘終了→捨て台詞へ。被弾判定を止めて破棄（以降ノーダメージ）。弾は残したまま（余韻）。
    private void BeginCameoExit()
    {
        if (IsInstanceValid(_cameoHit)) { _cameoHit.Monitoring = false; _cameoHit.QueueFree(); }
        _cameoPhase = 2; _cameoT = 0; _cameoPostLine = -1;
    }

    // 捨て台詞：CameoPost のボス行(who=2)を一行オーバーレイで順に見せ、終わったら逃走へ。
    private void CameoPostTalk(double delta)
    {
        CameoBob();
        _cameoT += delta;
        if (_cameoPostLine < 0 || _cameoT >= CameoHit.PostLineDur)
        {
            _cameoT = 0;
            _cameoPostLine++;
            string? line = CameoHit.NextBossLine(CameoPost, ref _cameoPostLine);
            if (line == null) { _cameoPhase = 3; _cameoT = 0; return; }
            Hud.ShowBossLine("あかり", line, UiKit.Kegare, CameoHit.PostLineDur);
        }
    }
    private void CameoBob()
    {
        if (!IsInstanceValid(_cameoSprite)) return;
        _cameoBobT += 0.016;
        var p = _cameoSprite.GlobalPosition;
        _cameoSprite.GlobalPosition = new Vector2(p.X, 78f + Mathf.Sin((float)_cameoBobT * 3f) * 2.5f);
    }

    // あかりの弾は“悲嘆の雨”寄り：上から降る自責の雨＋本人周りの弱いリング（弾速控えめ・攻撃的でない）。
    private void CameoFireVolley(int kind)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null || !IsInstanceValid(_cameoSprite)) return;
        var game = GetNodeOrNull<GameManager>("/root/Game");
        Vector2 from = _cameoSprite.GlobalPosition;
        float spd = 80f * (game?.BulletSpeedMul ?? 1f);
        int rain = game?.ScaleBullets(7 + kind * 2) ?? (7 + kind * 2);
        for (int i = 0; i < rain; i++)
        {
            float x = 20f + 344f * (i + 0.5f) / rain;
            pool.Spawn(new Vector2(x, -6f), new Vector2(_rng.RandfRange(-8f, 8f), spd * 0.9f), isEnemy: true, 3f, 1);
        }
        int ring = game?.ScaleBullets(6 + kind * 3) ?? (6 + kind * 3);
        for (int i = 0; i < ring; i++)
        {
            float a = Mathf.Tau * i / ring;
            pool.Spawn(from, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (spd * 0.6f), isEnemy: true, 3f, 1);
        }
    }

    // ---- 2: ボス出現 ----
    private void Step_BossSpawn()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _boss = new BossAkari { Name = "BossAkari" };
            World.AddChild(_boss);
            _boss.GlobalPosition = new Vector2(SpawnX, 70f);
            _bossActive = true;
            Advance(); // 出現と同時に説明会話へ（会話中はボス停止・雨も止む）
        }
    }

    // ---- 3: ボス戦（浄化＆会話完了まで） ----
    private void Step_BossWait()
    {
        if (!IsInstanceValid(_boss) || _boss.Finished)
        {
            _bossActive = false;
            Advance();
        }
    }

    // ---- 5: クリア（帰還の会話を手動送り） ----
    private bool _clearBannerShown;
    private void Step_Clear(double delta)
    {
        if (!_clearBannerShown)
        {
            _clearBannerShown = true;
            _clearTime = (float)_stageElapsed;
            var game = GetNodeOrNull<GameManager>("/root/Game");
            var rec = game?.RecordClearTime("akari", game.Difficulty, _clearTime) ?? (true, (float?)null);
            Hud.ShowClearBanner("STAGE 2 CLEAR", _clearTime, rec.isBest, rec.prev);
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll(); // クリア時に自弾・残弾を一掃(#17)
        }
        Step_Lines(delta, Clear);
    }

    // ---- 6: STAGE3（こはる）へ ----
    private bool _clearing;
    private void Step_Transition()
    {
        if (_clearing) return;
        _clearing = true;
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetNodeOrNull<GameManager>("/root/Game")?.CompleteStage("akari");
        GetTree().ChangeSceneToFile("res://Hub.tscn");
    }

    // 天井から降る「自責の雨」（会話中は止む）。
    private void Rain(double delta)
    {
        if (Hud.BubblePaused) return;
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        _rainT += delta;
        float mul = GetNodeOrNull<GameManager>("/root/Game")?.DanmakuIntervalMul ?? 1f;
        if (_rainT < 0.16 * mul) return;
        _rainT = 0;
        float x = _rng.RandfRange(8f, 376f);
        float vx = _rng.RandfRange(-10f, 10f);
        pool.Spawn(new Vector2(x, -6f), new Vector2(vx, 74f), isEnemy: true, 3f, 1);
    }
}
