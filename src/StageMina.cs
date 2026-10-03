using Godot;

// StageMina : FINAL「穢れたわたし」進行（案C・仮台本 08 F1〜F3）。三人ぶんの穢れが限界に達したミナ自身が
// 襲ってくる。自機は通信路を通る「あなたの光」で、彼女が抱えた穢れを撃ち祓う。
//   1: 導入（F1。ミナの声が壊れ、三人の投稿が変質して戻る）
//   2: 残響の道中（三波・波ごとに強度と数が上がる）
//   3: ボス出現（BossMina）＝チェックポイント入口「ボスから」の着地点
//   4: ボス戦（撃破＝穢れを祓う／中で短い邂逅セリフ）
//   5: Final（対話で帰還）へ
public partial class StageMina : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private int _step;
    private bool _stepStarted;
    private double _stageElapsed;   // ステージ全体の経過秒（クリア確定まで・ポーズ中は止まる）。
    private double _lineHold;
    private int _introLine;
    private BossMina _boss = null!;
    private Spawner _echoSpawner = null!;   // いま走っている波のスポナー（波ごとに作り直す）
    private bool _bossActive;
    private double _rainT;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private bool _zHeld, _zEdge, _startBannerShown;
    private bool _titleThump; private double _titleThumpT; // タイトルカードの拍（タグが合わさる瞬間の一突き）

    private const float SpawnX = 300f;

    // F1 導入（仮台本 docs/20260928/wiki_仮台本_退避/08。ユーザー承認済み・2026-09-05）。who: 1=ミナ / 3=システム表示 / 4=投稿。
    //   ① 暴走の状況実況は地で説明せず、FINAL タイトルカード＋渦巻くビジュアル＋無音に委ねる。
    //      ミナの声は壊れた断片2行だけ＝言葉が壊れる、を見せる（show-don't-tell）。
    //   ② 三人の投稿が「変質して戻る」＝ミナが吸って抱え込んだ穢れの断片。順は面の順（あかり→こはる→レイ）。
    //      本人の現在の感情ではなく残響。F4「あかりの。こはるの。レイの。」の予感。
    //   ③ 末尾は残った通信路と送信元の提示。自機はミナの身体ではなく「あなたの光」。
    //   ④ S3-7 で送った言葉の引用は1行だけ動的に差し替える（S37Quote）。
    private static readonly (int who, string text, string face)[] IntroHead =
    {
        (1, "……ご主人、様……ごめん、なさい……", MWorried), // 壊れた謝罪＝動揺(worried)。平常顔で言わせない（表情と物語の一致）
        (1, "……とまら、ない……こえ、が……", MWorried), // 声の氾濫の予告。地で説明せず、次の投稿弾で“外から流れ込む”を見せる
        (4, "「既読、ついてる……返事、まだ。……もう、だれに送ったのか、わからない」", ""), // あかり＝「返して」が宛先を失った形
        (4, "「なにしてんだろ、あたし……ぜんぶ、むだ。……画面、もう、つかない」", ""),   // こはる＝「むだ」が全部に広がった形
        (4, "「気づいて……見て……。見られてるの、ガワだけ。……中には、もう、だれも」", ""), // レイ＝「気づいて」が自分にも向かなくなった形
        (1, "……あかり、の。こはる、の。レイ、の……ぜんぶ、わたくし、の……", MWorried), // 面の順。ここでは言い切れず途切れる（回収は F4 の静かな受容）
    };
    private const string MWorried = "res://char/mina_worried.png";
    // S3-7 の分岐受け（送った言葉を一度だけ観測で引用。断定はしない）。
    private static (int who, string text, string face) S37Quote(GameManager? game)
    {
        if (game == null || !game.HasChoiceAt("s3_7"))
            return (1, "……休む、と。ひとこと、言えばよかったのに……。", MWorried);
        return game.ChosenAt("s3_7") switch
        {
            "一緒に行こう。つらくなったら教えて" => (1, "……つらくなったら教えて、と。あの言葉があったから……今度は、隠さずに言います。ご主人様。助けて、ください。", MWorried),
            "ミナまで傷つくのは嫌だ。少し休もう" => (1, "……わたくしが傷つくのは嫌だと、言ってくださいましたね。……また、頼ってもよいですか。もう、ひとりでは……。", MWorried),
            _            => (1, "……あのときは、わたくしが続けると決めました。……でも今は、ひとりでは……。", MWorried),
        };
    }
    // The channel survives even when Mina can no longer move her body.
    private static readonly (int who, string text, string face)[] IntroTail =
    {
        (1, "……わたくしの身体は、もう、動かせません。でも。あなたとの回線だけは、まだ……。", MWorried),
        (1, "いつも、言葉を届けてくださった道です。……切れて、いません。", MWorried),
        (3, "通信先：ミナの内側\n送信元：あなた", ""),
        (3, "入力に応じて、小さな光が動く。\nそこに、ミナの姿はない。", ""),
        (1, "……あなたの光。わたくしを動かさなくても、届いてしまうのですね。", MWorried),
    };
    private (int who, string text, string face)[] _intro = System.Array.Empty<(int, string, string)>();

    // ── ルナティック（2026-09-26 作者指示「回想・エンディング・選択肢はカット、常に敵が出続け、ボス戦は止まらない」）──
    //   GameManager.IsLunatic のとき true。F1 の導入会話を流さず、タイトルカードのあと即・残響の波→ミナ戦へ。
    //   段間のカットシーン（MinaPhaseScene）と回想・邂逅の会話は BossMina 側が畳む。
    //   撃破後は MinaStoryFilm（アフター）も Final（F4）も Epilogue も流さず、記録だけ確定して FINAL CLEAR のリザルトを置き、
    //   通常ならエピローグの末尾（Epilogue.PhEnd）が着地する先＝タイトルへ直行する。従来難易度は _lunatic=false で従来のまま。
    private bool _lunatic;
    private const double LunaticClearHold = 3.0;   // リザルトのバナーを読む間
    private double _lunaticClearT;

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        World.ProcessMode = ProcessModeEnum.Disabled;
        var game = GetNodeOrNull<GameManager>("/root/Game");
        _lunatic = game?.IsLunatic == true;
        game?.SetStageTarget(EchoTotal + 1);   // 道中の残響ぜんぶ＋ボス
        // 導入は S3-7 の分岐受け1行だけが可変。
        // ★FINAL はジョブに関わらず常にミナ本編（2026-09-15 ユーザー承認仕様）。
        //   旧 CompanionDialogue.Final（他ジョブ時に導入をその子の掛け合いへ置換）は廃止した。
        //   他ジョブの専用ストーリーは STAGE1〜3 だけ（CharacterStory 参照）＝この面は固定。
        var intro = new System.Collections.Generic.List<(int who, string text, string face)>(IntroHead);
        intro.Add(S37Quote(game));
        intro.AddRange(IntroTail);
        _intro = intro.ToArray();
        // ───── チェックポイント入口「ボスから」（2026-09-27）─────
        //   ゲームオーバーの「ボスから・スコア半分消費」／R 単体が SelectedEntry をセットしてくる。
        //   導入（F1）と残響の三波（約70秒）を飛ばし、step 3＝Step_BossSpawn からミナ戦で始める
        //   ＝本編3面（StageAkari/StageKoharu/StageRei の _Ready）と同じ作法。
        //   ・FINAL に中ボスは居ないので Start 以外は全部「ボスから」に落とす。
        //   ・導入（Step_Lines）が起こすはずの World を自分で起こす（さもないと誰も動かない）。
        //   ・FINAL の入りの儀式（5.2 秒のタイトルカードと一突き）は出さない。本編3面のバナーは
        //     進行を止めないが、こちらは出ているあいだ _Process ごと止める＝倒れるたび 5.2 秒
        //     待たされる。面の頭に戻ったのではなくボス戦の続きなので、儀式は省く。
        //   ・読んだら消す（本編3面と同じ。残さないと Shift+R の「最初から」が前回の入口で再開する）。
        //     ただし --boss デバッグ中は「毎回ボスから」を保つため貼り直す。
        //   ・道中で得るはずだったパワーアップ（5体撃破ごとの落とし物）は本編3面と同じく補填しない
        //     ＝ボスから始めた分だけ手ぶらになる。スコアは PrepareBossRetry が半分だけ持ち越す。
        if (game != null && game.SelectedEntry != GameManager.StageEntry.Start)
        {
            _step = 3;
            World.ProcessMode = ProcessModeEnum.Inherit;
            _startBannerShown = true; _titleThump = true;
            game.SelectedEntry = game.DebugAlwaysBoss ? GameManager.StageEntry.Boss : GameManager.StageEntry.Start;
        }
        // 導入は「バナー＋暴走ビジュアル＋無音に委ねる」（Intro コメント①）。
        //   Audio はシーンをまたいで常駐するため、ここで止めないとハブ等の BgmMenu が
        //   壊れたミナの声（導入1行目）の上で鳴り続けてしまう。BossMina 出現時に
        //   BgmBossMina が立ち上がるまでの区間を意図どおり沈黙にする（mitsuda style §7 無音）。
        //   「ボスから」再開は導入を踏まない＝沈黙にする区間が無い（次フレームの BossMina が
        //   BgmBossMina を張る）ので止めない＝ゲームオーバー曲からボス曲へ直に渡す。
        if (_step == 1) Audio.Instance?.StopMusic(fade: 1.2f);
        // FINAL は BeginStageRun を通らない（面の頭を無音で始めるので道中曲を即鳴らせない）ため、
        //   Audio が覚えている「いまの面」が前の面（rei 等）のまま残る。そのままだとゲームオーバーで
        //   残機が戻ったときの復帰（GameManager.ClearGameOverChoice → ResumeStageMusic）が
        //   **別の面の道中曲**を蘇らせる。ここで id だけ入れ替える（曲は鳴らさない）。
        Audio.Instance?.SetStageId("mina");
    }

    public override void _Process(double delta)
    {
        if (Hud.CinematicMode) { _zHeld = Pad.AdvanceHeld(); return; }
        _lineHold += delta;
        if (!_clearing && !Hud.BubblePaused) { _stageElapsed += delta; Hud.SetElapsed((float)_stageElapsed); }
        // 会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        _zEdge = z && !_zHeld;
        _zHeld = z;
        // バナー副題は「暴走」（機械の故障＝外から見た説明）を避ける。ミナは壊れたのではなく“満ちた”＝
        // 三人ぶんの穢れを抱えきれなくなった。旧副題「いなくならないで」は合言葉 Stay に紐づく語で、
        // 案C では Stay ごと落としたため不採用（仮台本 08 F1）。副題そのものは未決なので空で出す
        // 副題「まだ、いますか」＝ユーザー決定（2026-09-06）。F4 の「まだ、いらっしゃいますか」と E3 の空の問いに繋がる。
        if (!_startBannerShown)
        {
            _startBannerShown = true;
            // FINAL だけは共通バナー（出て消える一行）ではなく専用タイトルカードで“格”を上げる。
            // 見せ方＝Hud.ShowEpicBanner を参照。
            Hud.ShowEpicBanner("FINAL", "まだ、いますか", UiKit.Kegare);
        }
        // タイトルカードの拍：タグが合わさる瞬間(1.25s)に低く一度だけ画面を突く。
        if (_startBannerShown && !_titleThump)
        {
            _titleThumpT += delta;
            if (_titleThumpT >= 1.25) { _titleThump = true; GameCamera.Instance?.Shake(2.6f, 0.34f); }
        }
        if (Hud.EpicBannerActive) return;
        // ルナティック：導入（F1）は流さない。Step_Lines が担っていた World の起動だけ肩代わりして残響の波へ。
        if (_lunatic && _step == 1) { World.ProcessMode = ProcessModeEnum.Inherit; Advance(); }
        switch (_step)
        {
            case 1: Step_Lines(delta, _intro); break;
            case 2: Step_Route(); break;
            case 3: Step_BossSpawn(); break;
            case 4: Step_BossWait(delta); break;
            case 5: if (_lunatic) Step_LunaticFinish(delta); else Step_Transition(); break;
        }
        // ボス戦中の ambient は、全ボス共通の投稿弾（Y投稿モチーフの言葉弾）に統一（難易度で数がスケール）。
        // FINAL は PostPool の Final テーマ（09 の F04〜F35 由来の 8 文字弾）を源にする＝暴走中に渦巻く声。
        // ボス本体(BossMina)のスペル/予測線/パネル弾はそのまま。
        if (_bossActive && !_boss.IsPurified && !Hud.BubblePaused && !_boss.AoeGateActive) PostBullets.Tick(this, _rng, delta, ref _rainT, ref _wordTick, source: _boss, theme: PostPool.Theme.Final, fallSpeed: 56f,
            accent: new Color(0.70f, 0.55f, 0.84f), murkAll: true); // FINAL テーマ＝ミナの菫。渦巻く悲鳴＝全語濁色チップ
    }

    private void Advance() { _step++; _stepStarted = false; }

    private void Step_Lines(double delta, (int who, string text, string face)[] lines)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            World.ProcessMode = ProcessModeEnum.Inherit;
            _introLine = 0; _lineHold = 0;
            if (lines.Length == 0) { Advance(); return; }
            Hud.HoldBubble = true;
            ShowLine(lines);
        }
        if (_zEdge && _lineHold >= 0.15 && !Hud.DialogRevealed)
        {
            Hud.RevealDialogNow(); _lineHold = 0;
        }
        else if (_lineHold >= 0.15 && Hud.DialogRevealed
                 && (_zEdge || Hud.FastForwarding || (Hud.AutoAdvance && _lineHold >= 1.4)))  // FastForwarding=既読スキップ（Ctrl/RB長押し・既読行のみ・#22）
        {
            _lineHold = 0; _introLine++;
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
        // 案C のこの面に出るのは あなた(0)／ミナ(1)／システム表示(3)／投稿(4)。
        //   0 と 4 は Hud 側が立ち絵を捨てる（0＝下書きの吹き出し印）。3 は Narration 扱いで中央テロップ。
        string portrait = kind switch
        {
            Hud.LineKind.Boy => "",                                                             // 「あなた」に顔は無い
            Hud.LineKind.Mina => string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face, // ミナも行ごと表情
            _ => "res://char/mina_face.png",
        };
        Hud.ShowDialog(kind, text, portrait, otherName: "ミナ");
    }

    // ── 道中（step 2）：残響の三波 ──────────────────────────────────────────
    //   2026-09-27 作者指示「MINA の道中戦を長くして」。それまでは残響3体（SpawnLimit=3）を浄化したら即ボスで、
    //   自動操縦の実測 6.7 秒（Normal）／5.0 秒（ルナティック）しか無かった＝本編3面の道中（あかり面は同じ計り方で
    //   120〜137 秒・ザコ戦だけで 65〜76 秒）に対して極端に短い。本編と同じ作り（StageAkari/StageRei の StartMidwaveSpawner）に
    //   合わせ、波ごとに強度（StartIntensity）と数を上げる三波にする。
    //   ・敵は残響3種のまま（EnemyTable.CharactersFor(StageTheme.Mina)）。Spawner.CharactersOnly で他テーマ流用の
    //     アンチくん／引用リプ／バズ壁は混ぜない＝ミナの内側に他の面の絵を持ち込まない（新しい敵種は作らない）。
    //     数は3種の繰り返しで満たす（同じ種が何度も来る＝残響なので理屈も合う）。
    //   ・波の切り替えで盤面を掃かない。本編は波の切れ目に会話が入るので掃くが、ここは会話を足さない方針なので
    //     前の波の残りが居るうちに次のスポナーを走らせる＝敵ゼロの空白を作らない（QA の最長空白は 2 秒以内）。
    //   ・強度は本編の 0/0.35/0.7 ではなく 0.35/0.6/0.85。FINAL は最終面で一番緩い立ち上がりは要らず、
    //     湧き間隔が 2 秒級（si=0）だと即殺されたときに敵ゼロが 2 秒に迫る（あかり面の si=0 波は実測 1.75 秒）。
    //   ・会話は足さない（新規台詞はシナリオ側の判断）。波の切れ目は密度だけで見せる＝戦闘は一度も止まらない。
    //   ・難易度で長さは変えない（数も強度も共通）。弾の数・速さ・湧き間隔・同時数は難易度側（GameManager）の担当。
    //   実測（tools/FinalRouteQa・自動操縦を4回）: 道中 64〜71 秒／うちザコ戦 55〜58 秒＝あかり面のザコ戦の 0.74〜0.89 倍。
    //   ルナティックは湧き間隔が 0.62 倍なので同じ 66 体でも 43〜48 秒（＝密度の差。長さを難易度で変えてはいない）。
    private static readonly (int count, float intensity)[] EchoWaves =
    {
        (18, 0.35f),   // A: 残響が滲み出す
        (22, 0.60f),   // B: 間隔が詰まる
        (26, 0.85f),   // C: ボス直前の山（最大密度）
    };
    private static int EchoTotal
    {
        get { int n = 0; foreach (var w in EchoWaves) n += w.count; return n; }
    }
    private int _wave;       // いま何波目か（0..EchoWaves.Length-1）。QA（FinalRouteQa）が読む。
    private int _waveBase;   // その波の頭での PurifiedCount（波の規定数はここからの差で数える）

    private void Step_Route()
    {
        var game = GetNode<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _wave = 0;
            _waveBase = game.PurifiedCount;
            // 残響の道中に曲を立てる（2026-09-29）。導入（F1）の沈黙はそのまま残し、**戦闘が始まる
            //   この瞬間から**鳴らす＝「無音に委ねる」のは台詞のほうで、66体と撃ち合う 64〜71 秒
            //   （ルナティック 43〜48 秒）ではない。フェードを長め（2.4秒）に取り、曲が立ち上がるのではなく
            //   残響が滲み出してくるように入れる（波Aのコメント「残響が滲み出す」と揃える）。
            //   曲の正体と選定根拠は Audio.BgmFinalRoute のコメント。BossMina が BgmBossMina へ
            //   クロスフェードして引き取る＝道中→ボスの段差はそこで一度だけ付く。
            Audio.Instance?.Music(Audio.Instance.BgmFinalRoute, 2.4f);
            StartEchoWave();
        }
        if (Hud.BubblePaused) return;
        // 浄化が目標(EchoTotal+1)を追い越すと Spawner が自動停止して湧かなくなる（ボムで規定数を跨いだとき）。
        //   その場合も波を送って進める保険＝本編（StageAkari/StageRei）と同じ作法。ルナティックは
        //   IgnoreStageCleared で止まらないので保険は使わず、規定数を必ず出し切る（波を飛ばさない）。
        bool overrun = game.StageCleared && !_lunatic;
        if (game.PurifiedCount - _waveBase < EchoWaves[_wave].count && !overrun) return;
        if (_wave + 1 < EchoWaves.Length)
        {
            _wave++;
            _waveBase = game.PurifiedCount;
            _echoSpawner.Stop();
            _echoSpawner.QueueFree();
            StartEchoWave();   // 盤面は掃かない＝前の波の残りに次の波が重なる
            return;
        }
        // 最後の波は「予定数が湧き切ってから」ミナ本体へ（数を削って早出しにしない）。
        if (_echoSpawner.SpawnedCount < _echoSpawner.SpawnLimit && !overrun) return;

        _echoSpawner.Stop();
        _echoSpawner.QueueFree();
        // Residual purification waves must not peel the boss's opening shield.
        foreach (Node node in World.GetChildren())
            if (node is MidEnemy or Ripple) node.QueueFree();
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        Advance();   // step 3＝Step_BossSpawn（「ボスから」再開もここへ直に着く）
    }

    // ── ボス出現（step 3）──────────────────────────────────────────────
    //   道中の続きとしても、チェックポイント入口「ボスから」の着地点としても、ミナを建てるのはここだけ
    //   ＝再開経路が道中経路と1行も違わない（本編3面の Step_BossSpawn と同じ立て方）。
    private void Step_BossSpawn()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            // 今ランでボス戦に到達した印（ゲームオーバーの「ボスから・スコア半分消費」はこれが立っている
            //   ときだけ出る）。残響戦（ボス出現前）で倒れたときは出さない＝到達していないボスからは再開できない。
            GetNodeOrNull<GameManager>("/root/Game")?.NotifyBossReached();
            if (!_lunatic) CameoIntroScene.PlayBoss(Hud, World, "mina", System.Array.Empty<(int, string, string)>(), () => {
                _zHeld = Pad.AdvanceHeld(); _zEdge = false;
            }, SpawnBoss);
            else SpawnBoss();
        }

        void SpawnBoss()
        {
            _boss = new BossMina { Name = "BossMina" };
            World.AddChild(_boss);
            _boss.GlobalPosition = new Vector2(SpawnX, 70f);
            _bossActive = true;
            (GetTree().GetFirstNodeInGroup("stagebg") as StageBackground)?.EnterBoss();
            Advance();
        }
    }

    // その波ぶんのスポナーを立てる（波ごとに新規＝ランプが StartIntensity から始まる）。
    private void StartEchoWave()
    {
        var (count, intensity) = EchoWaves[_wave];
        _echoSpawner = new Spawner
        {
            Name = $"EchoSpawner{_wave}", World = World, Theme = StageTheme.Mina,
            SpawnLimit = count, StartIntensity = intensity,
            CharactersOnly = true,          // 残響3種だけ（他テーマ流用のスキンを混ぜない）
            IgnoreStageCleared = _lunatic,  // ルナティックは目標到達でも止めない（空白を作らない）
        };
        AddChild(_echoSpawner);
        _echoSpawner.Begin();
    }

    // 撃破後に Finished が立たないまま固まる進行不能への保険。
    //   通常は改心の会話を送り切った時点で Finished が立ち、この計時は使われない（尺・演出は不変）。
    //   ボス側の保険（Enemy の cry ウォッチドッグ）が何らかの理由で効かなかった場合の最後の砦として、
    //   撃破（IsPurified）から BossFinishGrace 秒経っても立たなければ次へ進める。
    //   ※撃破前（戦闘中）は一切計らない＝長期戦を勝手に打ち切ることはない。
    private const double BossFinishGrace = 150.0;
    private double _postDefeatT;
    // ボス戦（step 4）。
    private void Step_BossWait(double delta)
    {
        if (!IsInstanceValid(_boss) || _boss.Finished)
        {
            _bossActive = false;
            Advance();
            return;
        }
        if (!_boss.IsPurified) return;
        _postDefeatT += delta;
        if (_postDefeatT < BossFinishGrace) return;
        GD.PushWarning("[StageMina] ボス撃破後に Finished が立たないため保険で進行");
        _bossActive = false;
        Advance();
    }

    private bool _clearing;
    private bool _returnShown;
    private void Step_Transition()
    {
        if (!_returnShown)
        {
            _returnShown = true;
            MinaStoryFilm.Play(Hud, World, aftermath: true, completed: () => _zHeld = Pad.AdvanceHeld());
            return;
        }
        if (_clearing) return;
        _clearing = true;
        // FINAL クリア確定＝この瞬間に経過秒を確定しベスト記録（記録画面/カードで参照）。
        var game = GetNodeOrNull<GameManager>("/root/Game");
        game?.RecordClearTime("final", game.Difficulty, (float)_stageElapsed);
        if (game != null) game.RecordScore("final", game.Difficulty, game.Score);
        game?.AutoSave(); // 記録を永続化（FINAL は CompleteStage を通らないためここで保存）。
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        // 撃破＝穢れを祓った。本決着（対話で帰還）は Final へ委ねる。
        // 暗転してから渡す（ボス背景のフラッシュ止め・2026-09-22。StageRei と同じ理由）。
        GameManager.FadeToScene(this, "res://Final.tscn");
    }

    // ルナティックの締め：アフター（MinaStoryFilm）も Final（F4）も Epilogue も流さない。
    //   記録は Step_Transition と同じ作法で確定し、他の面と同じ様式のリザルト（FINAL CLEAR＋TIME/SCORE）を
    //   LunaticClearHold 秒置いてから、通常ならエピローグの末尾が着地する先（タイトル）へ暗転して直行する。
    private void Step_LunaticFinish(double delta)
    {
        if (!_returnShown)
        {
            _returnShown = true;
            var game = GetNodeOrNull<GameManager>("/root/Game");
            float clearTime = (float)_stageElapsed;
            var rec = game?.RecordClearTime("final", game.Difficulty, clearTime) ?? (true, (float?)null);
            long score = game?.Score ?? 0;
            var recScore = game?.RecordScore("final", game.Difficulty, score) ?? (true, (long?)null);
            game?.AutoSave(); // 記録を永続化（FINAL は CompleteStage を通らないためここで保存）。
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Hud.ShowClearBanner("FINAL CLEAR", clearTime, rec.isBest, rec.prev, score, recScore.isBest, recScore.prev);
            return;
        }
        _lunaticClearT += delta;
        if (_lunaticClearT < LunaticClearHold || _clearing) return;
        _clearing = true;
        GameManager.FadeToScene(this, "res://TitleMenu.tscn");
    }

    // 投稿弾（暴走中に渦巻く悲鳴の言葉）の周期/tick 用アキュムレータ。湧き処理は PostBullets.Tick に集約。
    // FINAL 固有の“声”プールは PostPool.Theme.Final（wiki/08_仮台本/09_投稿文集_Y風.md の FINAL の行）へ移した。
    //   三人ぶんの穢れが満ちた、が設定＝層1（炎上のリプライ）／層2（悲鳴）／層3（三人の言葉とミナ語）を
    //   3:5:2 で混ぜる。ミナ語（わたくしの、せいです／ご主人様／……アホですね）は 09 のとおり X 化せず
    //   現行維持で層3 に置き、彼女自身の口癖が悲鳴として降ってくることで“これは彼女の内側だ”と示す
    //   （Intro:48「ぜんぶ、わたくし、の……」の先取り）。
    private int _wordTick;
}
