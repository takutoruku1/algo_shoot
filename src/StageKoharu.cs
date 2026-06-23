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

    // 道中ザコ戦（Spawner）。三部構成で「後半ほど圧が上がる」緩急を作る：
    //   前半A（緩い導入）→ チラ見せ → 後半B（やや詰める）→ ミッドシナリオ（溜め）→ 終盤C（最大密度）→ 本ボス。
    // 体数より“密度と変化”で長さを作る（§3 緩急）：合計20体だが3波で構成を変えて間延びさせない。
    private Spawner _spawner = null!;
    private int _waveBase;
    private bool _waveSpawnDone;       // 道中ステップ内：規定数浄化でスポーン停止済み（残ザコ全滅待ち）。各ステップ開始でリセット。
    private const int MidWaveA = 21;  // 道中3倍（旧7）
    private const int MidWaveB = 18;  // 道中3倍（旧6）
    private const int MidWaveC = 21;  // 道中3倍（旧7）＝合計60体
    // ボスの“チラ見せ”（カメオ）＝本戦ボスと同じ土台の短いミニボス戦（CameoBoss＝Enemy 派生・シールド制）。
    // こはる＝無力・他責で、弾は“落ちる祈り”。撃破（HP/サイクル削り切り＝改心）まで Stage は進まない。保険退場は廃止。
    private CameoBoss _cameo = null!;

    // ダイブ前〜着地（v2 [P-03]）。
    // who: 0=少年 / 1=ミナ / 2=こはる / 3=地の文 / 4=投稿 / 5=中継。
    private static readonly (int who, string text, string face)[] Intro =
    {
        (0, "なあミナ。……いま、腹が鳴った。聞こえたか?", SCocky),                 // 無目的な雑談（音で分かる＝視点の断絶を守る）
        (1, "ええ、ばっちりと。電脳ごしでも、ご主人様の腹の音だけは、よく届きます。", "res://char/mina_smile.png"),
        (4, "「ぜんぶ食べてね。のこしちゃだめ。……そしたら、いなくならないでしょ?」", ""),  // 投稿
        (0, "……ミナ。Stay——だ。", SGentle),                                  // 合言葉の回帰（今度は少年自身の祈りとして滲む）
        (1, "……はい。今日は、ちゃんと言ってくれるんですね。", ""),
        (0, "……ああ。きみは、ぼくのそばにいてくれ。", SGentle),
        (1, "ご主人様。……ここは、台所ですね。夕食の支度が、永遠に、続いています。", "res://char/mina_face.png"),
    };

    // 道中突入の小話（世界観：空席の食卓と「むだ」の声）。道中ザコ戦の前に出す。
    private static readonly (int who, string text, string face)[] Mid =
    {
        (1, "ご主人様、見てください。湯気の向こうに、たくさんの食卓。……どれも、空席です。一つも、埋まっていない。", "res://char/mina_worried.png"),
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
        (2, "あ、おきゃくさん! いらっしゃい。ごはん、もうすぐできるからね。手、洗ってきた?", KFace),
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
        (2, "あ、こら! じっとしてないと、よそえないでしょ。もう、せっかちさんなんだから。", KFace),
        (2, "お味噌汁の具、108種類あるんだけど。ぜんぶ入れていい? いいよね? 入れちゃうね。", KFace),
    };
    private static readonly (int who, string text, string face)[] CameoPost =
    {
        (2, "もう帰っちゃうの? ……はい、これ、おにぎり。道中で食べてね。残しちゃ、だめだよ?", KFace),
        (1, "……あの子、冷めていく食卓の奥へ。とぼとぼと、戻っていきます。", "res://char/mina_worried.png"),
        (1, "……ご主人様。あの子は——", ""),
        (0, "……ぼくが、行く。最後に、ちゃんと……伝えるんだ。", SGentle),
    };

    // ───────── ミッドシナリオ枠（後半Bと終盤Cの境＝ボス前の“溜め”）─────────
    // シナリオ担当が本文を差し込むスロット（who=Hud.LineKind 0=少年/1=ミナ/2=こはる/3=ナレ/4=投稿/5=中継）。
    // 吹き出し会話（Step_Lines）で出す＝弾は止まる。こはる面のテーマ＝台所/無力感に馴染む位置に。
    // ※プレースホルダ。2〜数行を想定。
    private static readonly (int who, string text, string face)[] MidStory =
    {
        (1, "ご主人様、聞こえますか。トン、トン、と。……誰もいない台所で、刻む音だけが、まだ続いています。", "res://char/mina_worried.png"),  // ザコ＝包丁/まな板・鍋/お玉
        (1, "完璧に作れば、いなくならない。……あの子は、そう祈って、ずっと一人で刻んでいるんですね。", "res://char/mina_worried.png"),
        (0, "ああ。祈るほど、報われない。それでも、手を止められないんだ。止めたら、認めることになるから。", SGentle),
        (1, "「むだ」だなんて、自分を責めて。……ほんとうは、ただ、寂しいだけなのに。", ""),
        (0, "……その祈りは、ちゃんと届いてたって。それだけ、伝えに行くんだ。", SGentle),
        (1, "……ご主人様。お声に、ノイズが。……いえ。電波のせい、ですよね。", "res://char/mina_doubt.png"), // 否認は言語化せず、言い直し＋怪訝顔（doubt）で“異変に気づき飲み込む”を察させる（手がかり＝ノイズは残す）
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
        (0, "……さあ。どうだったかな。", SCocky),                              // はぐらかす（伏線④を未回収のまま引きずる）
        (1, "……さっきの「どなたに」も、はぐらかされたままです。", "res://char/mina_doubt.png"), // はぐらかしを訝る＝怪訝顔（doubt）。直後の:144 doubt と地続きで“察して飲み込む”流れ
        (1, "……ご主人様。……いえ。なんでも、ありません。", "res://char/mina_doubt.png"), // 飲み込めない問いを言い差して飲み込む。怪訝顔（doubt）＝察して飲み込む。心情は説明せず間で察させる（死は描かない）
    };

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        // 道中（A+B+C 三波）＋ボスで浄化カプセルが満ちる。
        var game = GetNodeOrNull<GameManager>("/root/Game");
        game?.SetStageTarget(MidWaveA + MidWaveB + MidWaveC + 1);

        // チェックポイント入口（DiffSelect が SelectedEntry をセット）。道中＆イントロを飛ばしてその戦闘から始める。
        // 中ボスから＝Step_BossCameo(5)／ボスから＝Step_BossSpawn(10)。
        if (game != null && game.SelectedEntry != GameManager.StageEntry.Start)
            _step = game.SelectedEntry switch
            {
                GameManager.StageEntry.Boss => 10,
                GameManager.StageEntry.AfterMidBoss => 6, // 中ボスの直後（道中後半）から＝再戦しない（初回ショップ後の続き）
                _ => 5,
            };
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
            case 3: Step_MidwaveA(delta); break;          // 道中ザコ戦A（導入）
            case 4: Step_Lines(delta, BossTalk); break;   // ボスのツイート→考察
            case 5: Step_BossCameo(delta); break;         // ボスのチラ見せ
            case 6: Step_MidwaveB(delta); break;          // 道中ザコ戦B（やや詰める）
            case 7: Step_Lines(delta, MidStory); break;   // ★ミッドシナリオ枠（ボス前の溜め）
            case 8: Step_MidwaveC(delta); break;          // 道中ザコ戦C（終盤＝最大密度の山）
            case 9: Step_Lines(delta, MidEnd); break;     // 道中後の小話
            case 10: Step_BossSpawn(); break;
            case 11: Step_Lines(delta, BossIntro); break;
            case 12: Step_BossWait(); break;
            case 13: Step_Clear(delta); break;
            case 14: Step_Transition(); break;
        }
        // ボス戦中の ambient は、全ボス共通の投稿弾（X投稿モチーフの言葉弾）に統一。
        // 旧「言葉弾＋ただの落下弾」混在から、Rei と同じく投稿弾のみ降らせる（難易度で数がスケール）。
        // こはる面は固有の悲鳴フレーズ（Words）を源にする＝その面のテーマ語が降る一体感。
        // ボス本体(BossKoharu)のスペル/予測線/パネル弾はそのまま。
        if (_bossActive) PostBullets.Tick(this, _rng, delta, ref _rainT, ref _wordTick, words: PostWords, fallSpeed: 44f);
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
            _waveSpawnDone = false;
            StartMidwaveSpawner();
        }
        // 規定数浄化（or 目標到達）で節目＝スポーン停止＋倒し残しの居座りザコを片付けて進む。
        // 全滅ハント（60体の中で最後の1体探し）を要求しない＝進行不能を防ぐ。戦闘中の居座りは維持。
        if (game != null && (game.PurifiedCount - _waveBase >= MidWaveA || game.StageCleared))
        {
            _spawner?.Stop(); _spawner = null!;
            ClearStageEnemies();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Advance();
        }
    }

    // 道中ザコ戦“B（後半）”：チラ見せの後。やや詰めて始める。MidWaveB体でミッドシナリオへ。
    private void Step_MidwaveB(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            _waveSpawnDone = false;
            StartMidwaveSpawner(0.35f);
        }
        // 規定数浄化（or 目標到達）で節目＝スポーン停止＋居座り片付け＋ミッドシナリオへ（全滅ハント不要＝進行不能を防ぐ）。
        if (game != null && (game.PurifiedCount - _waveBase >= MidWaveB || game.StageCleared))
        {
            _spawner?.Stop(); _spawner = null!;
            ClearStageEnemies();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Advance();
        }
    }

    // 道中ザコ戦“C（終盤）”：ミッドシナリオの後。最大密度でボス直前の山を作る。MidWaveC体で本ボスへ。
    private void Step_MidwaveC(double delta)
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _waveBase = game?.PurifiedCount ?? 0;
            _waveSpawnDone = false;
            StartMidwaveSpawner(0.7f);
        }
        // 規定数浄化（or 目標到達）で節目＝スポーン停止＋居座り片付け＋本ボスへ（全滅ハント不要＝進行不能を防ぐ）。
        if (game != null && (game.PurifiedCount - _waveBase >= MidWaveC || game.StageCleared))
        {
            _spawner?.Stop(); _spawner = null!;
            ClearStageEnemies();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Advance();
        }
    }

    private void StartMidwaveSpawner(float startIntensity = 0f)
    {
        if (_spawner != null) return;
        _spawner = new Spawner { Name = "Spawner", World = World, Theme = StageTheme.Koharu, StartIntensity = startIntensity };
        AddChild(_spawner);
        _spawner.Begin();
    }

    private void ClearStageEnemies()
    {
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e) e.QueueFree();
    }

    // ボスの“チラ見せ”：CameoBoss（本戦ボスと同じ Enemy 派生・シールド制・BossMover）を1体スポーン。
    // 撃破（HP/サイクル削り切り＝改心）して捨て台詞を流し切る（Finished）まで Stage は進まない。保険退場は無し。
    private void Step_BossCameo(double delta)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _cameo = new CameoBoss
            {
                Name = "KoharuCameo",
                Theme = new CameoTheme
                {
                    DisplayName = "こはる", Handle = "@koharu",
                    PreTex = "res://char/enemy_koharu_pre.png",
                    CryTex = "res://char/enemy_koharu_cry.png",
                    PostTex = "res://char/enemy_koharu_post.png",
                    Face = KFace,
                    SpellTint = new Color("e8a24a"), SpellShape = BulletShape.Orb,
                    Fire = CameoFireTheme.KoharuFalling,
                    Aura = FxLayer.BossAura.Koharu,
                    Bgm = Audio.Instance?.BgmBossKoharu,
                    IntroLines = CameoTalk1, TauntLines = CameoTalk3, DefeatLines = CameoPost,
                },
            };
            World.AddChild(_cameo);
            _cameo.GlobalPosition = new Vector2(SpawnX, 70f);
        }

        // 撃破→捨て台詞を流し切ったら次フェーズへ（道中後半）。
        if (!IsInstanceValid(_cameo) || _cameo.Finished)
        {
            Hud.HideBossBar();                                   // バー出っ放しにしない（後で本ボスが再表示）
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            if (IsInstanceValid(_cameo)) _cameo.QueueFree();
            // 中ボス撃破フック：撃破記録＋初回なら強化ショップ説明へ離脱（その後ハブ）。離脱したら以降の進行は止める。
            if (CheckpointFlow.OnMidBossCleared(this, "koharu", false)) return;
            Advance();
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
            // 本ボス突入：道中の横スクロール背景 → ボス専用背景へ切替（中ボス/カメオでは呼ばない）。
            GetTree().GetFirstNodeInGroup("stagebg")?.Call("EnterBoss");
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

    // 投稿弾（言葉弾）の周期/tick 用アキュムレータ。湧き処理は全ボス共通ヘルパ PostBullets.Tick に集約。
    private int _wordTick;
    // こはる面固有の“声”プール（投稿弾の源）。ハンドルは無し（""）＝この面のテーマ語だけを降らせる。
    private static readonly (string h, string w)[] PostWords =
        { ("", "むだだよ"), ("", "なにをつくっても"), ("", "もう帰ってこない"), ("", "ひとりになる") };
}
