using Godot;

// StageMina : FINAL「穢れたわたし」進行（案C・仮台本 08 F1〜F3）。三人ぶんの穢れが限界に達したミナ自身が
// 襲ってくる。自機は通信路を通る「あなたの光」で、彼女が抱えた穢れを撃ち祓う。
//   1: 導入（F1。ミナの声が壊れ、三人の投稿が変質して戻る）
//   2: 残響3体の浄化 → ボス出現（BossMina）
//   3: ボス戦（撃破＝穢れを祓う／中で短い邂逅セリフ）
//   4: Final（対話で帰還）へ
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
    private Spawner _echoSpawner = null!;
    private bool _bossActive;
    private double _rainT;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private bool _zHeld, _zEdge, _startBannerShown;
    private bool _titleThump; private double _titleThumpT; // タイトルカードの拍（タグが合わさる瞬間の一突き）

    private const float SpawnX = 300f;

    // F1 導入（仮台本 wiki/08_仮台本/08。ユーザー承認済み・2026-09-05）。who: 1=ミナ / 3=システム表示 / 4=投稿。
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
            "つづけて"   => (1, "……“つづけて”と、いただきましたので。……まだ、つづけて、います。", MWorried),
            "むりしないで" => (1, "……“むりしないで”と、いただいたのに。……すみません。", MWorried),
            _            => (1, "……あのとき、無言、でしたね。……それを、続行と、読みました。", MWorried), // （送らない）＝空文字
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

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        World.ProcessMode = ProcessModeEnum.Disabled;
        var game = GetNodeOrNull<GameManager>("/root/Game");
        game?.SetStageTarget(EnemyTable.CharactersFor(StageTheme.Mina).Count + 1);
        // 導入は S3-7 の分岐受け1行だけが可変。
        // ★FINAL はジョブに関わらず常にミナ本編（2026-09-15 ユーザー承認仕様）。
        //   旧 CompanionDialogue.Final（他ジョブ時に導入をその子の掛け合いへ置換）は廃止した。
        //   他ジョブの専用ストーリーは STAGE1〜3 だけ（CharacterStory 参照）＝この面は固定。
        var intro = new System.Collections.Generic.List<(int who, string text, string face)>(IntroHead);
        intro.Add(S37Quote(game));
        intro.AddRange(IntroTail);
        _intro = intro.ToArray();
        // 導入は「バナー＋暴走ビジュアル＋無音に委ねる」（Intro コメント①）。
        //   Audio はシーンをまたいで常駐するため、ここで止めないとハブ等の BgmMenu が
        //   壊れたミナの声（導入1行目）の上で鳴り続けてしまう。BossMina 出現時に
        //   BgmBossMina が立ち上がるまでの区間を意図どおり沈黙にする（mitsuda style §7 無音）。
        Audio.Instance?.StopMusic(fade: 1.2f);
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
        switch (_step)
        {
            case 1: Step_Lines(delta, _intro); break;
            case 2: Step_BossSpawn(); break;
            case 3: Step_BossWait(delta); break;
            case 4: Step_Transition(); break;
        }
        // ボス戦中の ambient は、全ボス共通の投稿弾（X投稿モチーフの言葉弾）に統一（難易度で数がスケール）。
        // FINAL は PostPool の Final テーマ（09 の F04〜F35 由来の 8 文字弾）を源にする＝暴走中に渦巻く声。
        // ボス本体(BossMina)のスペル/予測線/パネル弾はそのまま。
        if (_bossActive && !_boss.IsPurified && !Hud.BubblePaused && !_boss.AoeGateActive) PostBullets.Tick(this, _rng, delta, ref _rainT, ref _wordTick, theme: PostPool.Theme.Final, fallSpeed: 56f,
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

    private void Step_BossSpawn()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _echoSpawner = new Spawner
            {
                Name = "EchoSpawner", World = World, Theme = StageTheme.Mina,
                SpawnLimit = EnemyTable.CharactersFor(StageTheme.Mina).Count,
            };
            AddChild(_echoSpawner);
            _echoSpawner.Begin();
        }
        if (Hud.BubblePaused || _echoSpawner.SpawnedCount < _echoSpawner.SpawnLimit) return;
        var game = GetNode<GameManager>("/root/Game");
        if (game.PurifiedCount < _echoSpawner.SpawnLimit) return;

        _echoSpawner.Stop();
        _echoSpawner.QueueFree();
        // Residual purification waves must not peel the boss's opening shield.
        foreach (Node node in World.GetChildren())
            if (node is MidEnemy or Ripple) node.QueueFree();
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        _boss = new BossMina { Name = "BossMina" };
        World.AddChild(_boss);
        _boss.GlobalPosition = new Vector2(SpawnX, 70f);
        _bossActive = true;
        (GetTree().GetFirstNodeInGroup("stagebg") as StageBackground)?.EnterBoss();
        Advance();
    }

    // 撃破後に Finished が立たないまま固まる進行不能への保険。
    //   通常は改心の会話を送り切った時点で Finished が立ち、この計時は使われない（尺・演出は不変）。
    //   ボス側の保険（Enemy の cry ウォッチドッグ）が何らかの理由で効かなかった場合の最後の砦として、
    //   撃破（IsPurified）から BossFinishGrace 秒経っても立たなければ次へ進める。
    //   ※撃破前（戦闘中）は一切計らない＝長期戦を勝手に打ち切ることはない。
    private const double BossFinishGrace = 150.0;
    private double _postDefeatT;
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

    // 投稿弾（暴走中に渦巻く悲鳴の言葉）の周期/tick 用アキュムレータ。湧き処理は PostBullets.Tick に集約。
    // FINAL 固有の“声”プールは PostPool.Theme.Final（wiki/08_仮台本/09 の FINAL の行）へ移した。
    //   三人ぶんの穢れが満ちた、が設定＝層1（炎上のリプライ）／層2（悲鳴）／層3（三人の言葉とミナ語）を
    //   3:5:2 で混ぜる。ミナ語（わたくしの、せいです／ご主人様／……アホですね）は 09 のとおり X 化せず
    //   現行維持で層3 に置き、彼女自身の口癖が悲鳴として降ってくることで“これは彼女の内側だ”と示す
    //   （Intro:48「ぜんぶ、わたくし、の……」の先取り）。
    private int _wordTick;
}
