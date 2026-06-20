using Godot;

// StageKoharu : STAGE3「こはる（永遠に夕食を作り続ける台所）」進行（v2 [P-03]）。
//   1: ダイブ前〜着地の会話
//   2: ボス出現
//   3: ボス前の説明
//   4: ボス戦
//   5: 帰還の会話（投稿変化＋伏線④「妹を見ててくれ」）
//   6: FINAL（汚染暴走）へ遷移
public partial class StageKoharu : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private int _step;
    private bool _stepStarted;
    private double _stepTime;
    private double _stageElapsed;   // ステージ全体の経過秒（クリア確定まで・ポーズ中は止まる）。
    private float _clearTime;       // クリア確定時の経過秒。
    private double _lineHold;
    private int _introLine;
    private BossKoharu _boss = null!;
    private bool _bossActive;
    private double _rainT;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private bool _zHeld;
    private bool _zEdge;
    private bool _startBannerShown;

    private const float SpawnX = 300f;
    private const string SCocky = "res://char/shonen_face.png";
    private const string SGentle = "res://char/shonen_gentle.png";

    // 道中ザコ戦（Spawner）。前半→ボスのチラ見せ→後半 の二部構成。
    private Spawner _spawner = null!;
    private int _waveBase;
    private const int MidWaveA = 6;
    private const int MidWaveB = 6;
    // ボスの“チラ見せ”（カメオ）＝短いミニボス戦。こはる＝無力・他責で、弾は“落ちる祈り”。
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

    // ダイブ前〜着地（v2 [P-03]）。
    // who: 0=少年 / 1=ミナ / 2=こはる / 3=地の文 / 4=投稿 / 5=中継。
    private static readonly (int who, string text, string face)[] Intro =
    {
        (4, "「ぜんぶ食べてね。のこしちゃだめ。……そしたら、いなくならないでしょ?」", ""),  // 投稿
        (0, "……ミナ。Stay——だ。", SGentle),                                  // 合言葉の回帰（今度は少年自身の祈りとして滲む）
        (1, "……はい。今日は、ちゃんと言ってくれるんですね。", ""),
        (0, "……ああ。きみは、ぼくのそばにいてくれ。", SGentle),
        (3, "——そこは、台所でした。夕食の支度が、永遠に続いている。", ""),     // 地
    };

    // 道中突入の小話（世界観：空席の食卓と「むだ」の声）。道中ザコ戦の前に出す。
    private static readonly (int who, string text, string face)[] Mid =
    {
        (3, "——湯気の向こうに、たくさんの食卓が並んでいました。どれも、空席でした。", ""),
        (1, "この声たちは……「むだだ」と、繰り返しています。", ""),
        (0, "祈るほど、報われない。……そう思い込まされてる声だ。", SGentle),
        (1, "……ご主人様の声、また、すこし掠れていますよ。", "res://char/mina_worried.png"),
        (0, "気のせいさ。……行こう。", SGentle),
    };

    // 道中“前半”の後：ボスのツイートが流れてくる→考察（少年の声が掠れる＝弱り）。
    private const string KFace = "res://char/koharu_face.png";
    private const string KPale = "res://char/koharu_face_pale.png"; // 絶望で蒼白（死蔵を活用）
    private static readonly (int who, string text, string face)[] BossTalk =
    {
        (4, "「ぜんぶ食べてね。のこしちゃだめ。……そしたら、いなくならないでしょ?」", ""), // ボスのツイート
        (1, "……今度の声は、まるで小さな子のようですね。", ""),
        (0, "……こはる。まだ、中学生だ。健気で、料理が、得意で。", SGentle),
        (1, "ご主人様。さっきから、声が……少し、掠れていますよ。", "res://char/mina_worried.png"),
        (0, "……気のせいさ。行こう。", SGentle),
    };

    // チラ見せ：登場（こはる＝無邪気な健気さ）。who=2=こはる。
    private static readonly (int who, string text, string face)[] CameoTalk1 =
    {
        (2, "……あ。おきゃくさん? ……ごめんね、いま、ごはんの途中なの。", KFace),
        (1, "……夕飯の支度ですか。こんなに、たくさん。", ""),
        (0, "……ああ。誰も、食べやしないのにな。", SGentle),
        (2, "ううん、食べるよ。ちゃんと作れば……お兄ちゃん、帰ってくるもん。", KFace),
    };
    private static readonly (int who, string text, string face)[] CameoTalk2 =
    {
        (2, "ねえ、見て。今日は、お兄ちゃんの好きなの、作ったの。", KFace),
        (1, "……ご主人様?", "res://char/mina_worried.png"),
        (0, "————。", SGentle),                                    // 言葉が出ない
    };
    // 山：無力感が他責へ。蒼白(KPale)。
    private static readonly (int who, string text, string face)[] CameoTalk3 =
    {
        (2, "……どうして。ちゃんと作ったのに。のこさず食べてって、言ったのに。", KPale),
        (2, "……あたしのごはんじゃ、お兄ちゃんは、たすからないの……?", KPale),
        (0, "————。", SGentle),                                    // こらえる
        (2, "……むだ、なの? ……ぜんぶ、むだ、なの……っ!", KPale),
    };
    private static readonly (int who, string text, string face)[] CameoPost =
    {
        (2, "……ごめんなさい。もっと、がんばるから。だから……いかないで。", KFace),
        (3, "——こはるは、冷めていく食卓の奥へ、とぼとぼと戻っていきました。", ""),
        (1, "……ご主人様。あの子は——", ""),
        (0, "……ぼくが、行く。最後に、ちゃんと……伝えるんだ。", SGentle),
    };

    // 道中後の小話（ボスへの引き）。
    private static readonly (int who, string text, string face)[] MidEnd =
    {
        (1, "いちばん奥の食卓に、あの子が。", ""),
        (0, "ああ。……こはるだ。", SGentle),
    };

    // ボス登場時の説明（設計書 [P-03] に該当なし＝空。こはるの独白と中継はボス側に集約）
    private static readonly (int who, string text, string face)[] BossIntro =
        System.Array.Empty<(int, string, string)>();

    // 帰還（v2 [P-03] 末尾）。投稿の変化＋伏線④。
    private static readonly (int who, string text, string face)[] Clear =
    {
        (4, "「ちゃんと食べてね。……あたしも、食べるから。」", ""),            // 投稿が変化
        (0, "もしぼくが寝坊して来られない日があったらさ。妹の様子でも、見ててくれよ。", SGentle),
        (1, "妹が、いらしたんですか。", ""),
    };

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        // 道中（前半＋後半）＋ボスで浄化カプセルが満ちる。
        GetNodeOrNull<GameManager>("/root/Game")?.SetStageTarget(MidWaveA + MidWaveB + 1);
    }

    public override void _Process(double delta)
    {
        _stepTime += delta;
        _lineHold += delta;
        if (!_clearing) { _stageElapsed += delta; Hud?.SetElapsed((float)_stageElapsed); }
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        _zEdge = z && !_zHeld;
        _zHeld = z;
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowBanner("STAGE 3 START"); }
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
            case 9: Step_Lines(delta, BossIntro); break;
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

    private void Step_Lines(double delta, (int who, string text, string face)[] lines)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _introLine = 0;
            _lineHold = 0;
            if (lines.Length == 0) { Advance(); return; }
            Hud.HoldBubble = true;
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
            Hud.LineKind.Boy => face,
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? "res://char/koharu_face.png" : face, // 蒼白(KPale)等を行ごとに
            Hud.LineKind.Mina => string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face, // ミナも行ごと表情
            _ => "res://char/mina_face.png",
        };
        Hud.ShowDialog(kind, text, portrait, otherName: "こはる");
    }

    // 道中ザコ戦“前半”：Spawner起動→MidWaveA体浄化でチラ見せへ。
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
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Advance();
        }
    }

    // 道中ザコ戦“後半”：チラ見せの後。MidWaveB体浄化で本ボスへ。
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
        _spawner = new Spawner { Name = "Spawner", World = World, Theme = StageTheme.Koharu };
        AddChild(_spawner);
        _spawner.Begin();
    }

    private void ClearStageEnemies()
    {
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e) e.QueueFree();
    }

    // ボスの“チラ見せ”：登場→（無力の掛け合い→落ちる祈り）×3→とぼとぼ退場。
    private void Step_BossCameo(double delta)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _cameoPhase = 0; _cameoT = 0; _cameoGaugeTaunt = false; _cameoPostLine = -1;
            var tex = ResourceLoader.Load<Texture2D>("res://char/enemy_koharu_pre.png");
            _cameoSprite = new Sprite2D { Name = "KoharuCameo", Texture = tex, Centered = true, FlipH = true,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear, ZIndex = 5 };
            if (tex != null) { float s = 64f / tex.GetHeight(); _cameoSprite.Scale = new Vector2(s, s); }
            World.AddChild(_cameoSprite);
            _cameoSprite.GlobalPosition = new Vector2(440f, 78f);
            _cameoHit = new CameoHit { Name = "KoharuCameoHit" };
            World.AddChild(_cameoHit);
            _cameoHit.Bind(_cameoSprite);
            // カメオ用ボスバー（本戦ボスと同じ複数ゲージ式）を出す。本ボス前なので時系列は重ならない。
            Hud.ShowBossBar("こはる", "@koharu");
            Hud.UpdateBossBar(_cameoHit.BarIndex, _cameoHit.TotalBars, _cameoHit.BarFrac);
        }

        // 被弾判定の追従＋早期離脱判定（2ゲージ削り切ったら捨て台詞→とぼとぼ退場へ）＋ボスバー反映。
        if (IsInstanceValid(_cameoHit))
        {
            _cameoHit.Tick(delta);
            Hud.UpdateBossBar(_cameoHit.BarIndex, _cameoHit.TotalBars, _cameoHit.BarFrac);
        }
        // 戦闘ループ中(phase1)に2ゲージ削り切ったら捨て台詞→退場へ。
        if (_cameoPhase == 1 && IsInstanceValid(_cameoHit) && _cameoHit.Escaped)
            BeginCameoExit();

        // 登場→戦闘ループ→捨て台詞→退場。会話で戦闘を止めない（ShowBossLineの一行オーバーレイのみ）。
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
                    Hud.ShowBossLine("こはる", CameoHit.FirstBossLine(CameoTalk1), UiKit.Kegare, 2.6);
                }
                if (ki >= 1f && _cameoT > 1.0)
                {
                    _cameoPhase = 1; _cameoT = 0;
                    _cameoBattleT = 0; _cameoVolleyT = 0.2; _cameoTauntT = 6.0; _cameoGaugeTaunt = false;
                }
                break;
            case 1: CameoBattleLoop(delta); break;                  // 戦闘ループ（撃ち合い）
            case 2: CameoPostTalk(delta); break;                    // 捨て台詞（一行オーバーレイで順送り）
            case 3: // 退場（とぼとぼ奥へ＝沈まず横へフェード）
                _cameoT += delta;
                float ko = Mathf.Min(1f, (float)_cameoT / 0.9f);
                _cameoSprite.GlobalPosition = new Vector2(Mathf.Lerp(300f, 470f, ko), 78f);
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
            Hud.ShowBossLine("こはる", CameoHit.FirstBossLine(CameoTalk2), UiKit.Kegare, 2.4);
            _cameoTauntT = 5.0;
        }
        else if (_cameoTauntT <= 0)
        {
            Hud.ShowBossLine("こはる", CameoHit.FirstBossLine(CameoTalk3), UiKit.Kegare, 2.4);
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

    // 捨て台詞：CameoPost のボス行(who=2)を一行オーバーレイで順に見せ、終わったら退場へ。
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
            Hud.ShowBossLine("こはる", line, UiKit.Kegare, CameoHit.PostLineDur);
        }
    }
    private void CameoBob()
    {
        if (!IsInstanceValid(_cameoSprite)) return;
        _cameoBobT += 0.016;
        var p = _cameoSprite.GlobalPosition;
        _cameoSprite.GlobalPosition = new Vector2(p.X, 78f + Mathf.Sin((float)_cameoBobT * 3f) * 2.5f);
    }

    // こはるの弾は“落ちる祈り”：上から落ちる弾（食卓へ）＋本人足元から下向きの弱い扇。
    private void CameoFireVolley(int kind)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null || !IsInstanceValid(_cameoSprite)) return;
        var game = GetNodeOrNull<GameManager>("/root/Game");
        Vector2 from = _cameoSprite.GlobalPosition;
        float spd = 78f * (game?.BulletSpeedMul ?? 1f);
        int fall = game?.ScaleBullets(8 + kind * 3) ?? (8 + kind * 3);
        for (int i = 0; i < fall; i++)
        {
            float x = 20f + 344f * (i + 0.5f) / fall;
            pool.Spawn(new Vector2(x, -6f), new Vector2(_rng.RandfRange(-6f, 6f), spd * 0.95f), isEnemy: true, 3f, 1);
        }
        int fan = game?.ScaleBullets(4 + kind * 2) ?? (4 + kind * 2);
        for (int i = 0; i < fan; i++)
        {
            float a = Mathf.Pi / 2f + Mathf.DegToRad(((float)i / Mathf.Max(1, fan - 1) - 0.5f) * (50f + kind * 10f));
            pool.Spawn(from, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (spd * 0.7f), isEnemy: true, 3f, 1);
        }
    }

    private void Step_BossSpawn()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _boss = new BossKoharu { Name = "BossKoharu" };
            World.AddChild(_boss);
            _boss.GlobalPosition = new Vector2(SpawnX, 70f);
            _bossActive = true;
            Advance();
        }
    }

    private void Step_BossWait()
    {
        if (!IsInstanceValid(_boss) || _boss.Finished)
        {
            _bossActive = false;
            Advance();
        }
    }

    private bool _clearBannerShown;
    private void Step_Clear(double delta)
    {
        if (!_clearBannerShown)
        {
            _clearBannerShown = true;
            _clearTime = (float)_stageElapsed;
            var game = GetNodeOrNull<GameManager>("/root/Game");
            var rec = game?.RecordClearTime("koharu", game.Difficulty, _clearTime) ?? (true, (float?)null);
            Hud.ShowClearBanner("STAGE 3 CLEAR", _clearTime, rec.isBest, rec.prev);
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll(); // クリア時に自弾・残弾を一掃(#17)
        }
        Step_Lines(delta, Clear);
    }

    private bool _clearing;
    private void Step_Transition()
    {
        if (_clearing) return;
        _clearing = true;
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetNodeOrNull<GameManager>("/root/Game")?.CompleteStage("koharu");
        GetTree().ChangeSceneToFile("res://Hub.tscn");
    }

    // 道中の言葉弾。会話中は止む。時々、設計書の具体フレーズを“文字の弾”として降らせる。
    private int _wordTick;
    private static readonly string[] Words = { "むだだよ", "なにをつくっても", "もう帰ってこない", "ひとりになる" };
    private void Rain(double delta)
    {
        if (Hud.BubblePaused) return;
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        _rainT += delta;
        float mul = GetNodeOrNull<GameManager>("/root/Game")?.DanmakuIntervalMul ?? 1f;
        if (_rainT < 0.17 * mul) return;
        _rainT = 0;
        if ((++_wordTick % 7) == 0)
        {
            var b = pool.Spawn(new Vector2(_rng.RandfRange(70f, 314f), -8f), new Vector2(0f, 44f), isEnemy: true, 3f, 1);
            b.SetWord(Words[_rng.RandiRange(0, Words.Length - 1)]);
            return;
        }
        float x = _rng.RandfRange(8f, 376f);
        float vx = _rng.RandfRange(-11f, 11f);
        pool.Spawn(new Vector2(x, -6f), new Vector2(vx, 72f), isEnemy: true, 3f, 1);
    }
}
