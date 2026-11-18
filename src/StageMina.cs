using Godot;

public partial class StageMina : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;
    internal double Elapsed { get => _stageElapsed; init => _stageElapsed = value; }
    private int _step;
    private bool _stepStarted;
    private double _stageElapsed;   // ステージ全体の経過秒（クリア確定まで・ポーズ中は止まる）。
    private double _lineHold;
    private int _introLine;
    private BossMina _boss = null!;
    private Spawner _echoSpawner = null!;   // いま走っている波のスポナー（波ごとに作り直す）
    private bool _bossActive;
    private bool _zHeld, _zEdge, _startBannerShown;
    private bool _titleThump; private double _titleThumpT; // タイトルカードの拍（タグが合わさる瞬間の一突き）
    private const float SpawnX = 300f;
    private static readonly (int who, string text, string face)[] IntroHead =
    {
        (0, "レイさん。ミナと連絡が取れない。最後に届いたのは、この投稿だけだ。", ""),
        (6, "差出人のところ、文字が崩れてる。……中には入れるのね？", ""),
        (0, "うん。帰り道も見えてる。でも、僕の声は今のミナに届かない。", ""),
        (6, "だったら、わたしを送って。今度は、わたしが呼ぶ。", ""),
        (0, "ありがとう。……こんなときに、僕だけ外から。", ""),
        (6, "道を開けられるのは、あんたでしょう。そっちを頼むって言ってるの。", ""),
        (0, "……うん。必ず、二人を帰す。", ""),
    };
    private static readonly (int who, string text, string face)[] IntroTail =
    {
        (4, "既読、ついてる……返事、まだ。……もう、だれに送ったのか、わからない。", ""),
        (4, "なにしてんだろ、あたし……ぜんぶ、むだ。", ""),
        (4, "気づいて……見て……。みんな、ガワしか見てない。", ""),
        (6, "……最後の、わたしの声だわ。どうしてここに？", ""),
        (0, "その先にも、音がする。少しだけ、そっちを向いて。", ""),
        (6, "聞こえた。回線、つないでおいて。", ""),
    };

    private (int who, string text, string face)[] _intro = System.Array.Empty<(int, string, string)>();
    // ── ルナティック（2026-09-26 作者指示「回想・エンディング・選択肢はカット、常に敵が出続け、ボス戦は止まらない」）──
    //   GameManager.IsLunatic のとき true。F1 の導入会話を流さず、タイトルカードのあと即・残響の波→ミナ戦へ。
    //   段間のカットシーン（MinaPhaseScene）と回想・邂逅の会話は BossMina 側が畳む。
    //   撃破後は MinaStoryFilm（アフター）も Final（F4）も Epilogue も流さず、記録だけ確定して FINAL CLEAR のリザルトを置き、
    //   エピローグを省略してホームへ帰還する。
    private bool _lunatic;
    private const double LunaticClearHold = 3.0;   // リザルトのバナーを読む間
    private double _lunaticClearT;
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
    private static readonly (int count, float intensity)[] EchoWaves =
    {
        (18, 0.35f),   // A: 残響が滲み出す
        (22, 0.60f),   // B: 間隔が詰まる
        (26, 0.85f),   // C: ボス直前の山（最大密度）
    };

    private int _wave;       // いま何波目か（0..EchoWaves.Length-1）。QA（FinalRouteQa）が読む。
    private int _waveBase;   // その波の頭での PurifiedCount（波の規定数はここからの差で数える）
    // 撃破後に Finished が立たないまま固まる進行不能への保険。
    //   通常は改心の会話を送り切った時点で Finished が立ち、この計時は使われない（尺・演出は不変）。
    //   ボス側の保険（Enemy の cry ウォッチドッグ）が何らかの理由で効かなかった場合の最後の砦として、
    //   撃破（IsPurified）から BossFinishGrace 秒経っても立たなければ次へ進める。
    //   ※撃破前（戦闘中）は一切計らない＝長期戦を勝手に打ち切ることはない。
    private const double BossFinishGrace = 150.0;
    private double _postDefeatT;
    private bool _clearing;
    private bool _returnShown;
    private bool _clearRecorded;
    private (bool isBest, float? prev) _clearTimeRecord;
    private (bool isBest, long? prev) _clearScoreRecord;
    private long _clearScore;

    // S3-7 の分岐受け（送った言葉を一度だけ観測で引用。断定はしない）。
    private static (int who, string text, string face)[] S37Quote(GameManager? game)
    {
        return (game?.ChosenAt("s3_7") ?? "") switch
        {
            "一緒に行こう。つらくなったら教えて" => new (int, string, string)[] {
        (0, "つらくなったら教えてって、言ったんだ。今度は、僕からも呼びたい。あの子に声が届くところまで、一緒に行ってほしい。", ""),
        (6, "ええ。返事を探して黙ってしまっても、こっちから呼ぶわ。", ""),
        (0, "道を開いた。レイさん、ダイブを始める。", ""),
        (6, "ええ。今度はわたしが、あの子の声を聞きに行くわ。", ""),
    },
            "ミナまで傷つくのは嫌だ。少し休もう" => new (int, string, string)[] {
        (0, "戻ってから、今日は終わりにしようって話したんだ。まだ回線はつながってるのに、いくら呼んでも返事がなくて。", ""),
        (6, "なら、道はあんたに任せる。あの子が動けなければ、わたしが手を貸すわ。", ""),
        (0, "道を開いた。レイさん、ダイブを始める。", ""),
        (6, "ええ。今度はわたしが、あの子の声を聞きに行くわ。", ""),
    },
            _ => new (int, string, string)[] {
        (0, "手伝ってくれって、最初に頼んだのは僕なのに。ミナが助けてって言えなくなるまで……。", ""),
        (6, "今は、自分を責めるより、あの子のところへ連れていって。", ""),
        (0, "道を開いた。レイさん、ダイブを始める。", ""),
        (6, "ええ。今度はわたしが、あの子の声を聞きに行くわ。", ""),
    },
        };
    }
    private static (int who, string text, string face)[] AdaptIntro((int who, string text, string face)[] lines, Job job)
    {
        if (job == Job.Tank) return new (int, string, string)[]
        {
            (0, "ミナ。あのときの記録、一緒に開いてもいい？", ""),
            (1, "はい。今なら、自分の言葉を聞きに行けます。", "res://char/v3/mina_conversation_v1.png"),
            (0, "僕はスマホを持ってる。いつでも、こっちに声をかけて。", ""),
            (1, "では、行ってまいります。帰り道も覚えていますよ、ご主人様。", "res://char/v3/mina_conversation_v1.png"),
        };
        if (job == Job.Magic) return lines;
        string name = Jobs.Get(job).CharacterName;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            line.text = (job, line.text) switch
            {
                (Job.Melee, "差出人のところ、文字が崩れてる。……中には入れるのね？") => "名前、読めないや。この投稿から入れるの？",
                (Job.Melee, "だったら、わたしを送って。今度は、わたしが呼ぶ。") => "じゃあ、あたしを送って。今度は、あたしが呼ぶから。",
                (Job.Melee, "道を開けられるのは、あんたでしょう。そっちを頼むって言ってるの。") => "道を開けられるのは、あなたでしょ。そっちは任せたよ。",
                (Job.Melee, "ええ。返事を探して黙ってしまっても、こっちから呼ぶわ。") => "うん。返事が出なくても、こっちから呼ぶよ。名前なら、何度でも。",
                (Job.Melee, "なら、道はあんたに任せる。あの子が動けなければ、わたしが手を貸すわ。") => "じゃあ、道は任せた。ミナが歩けなければ、あたしにつかまってもらう。",
                (Job.Melee, "今は、自分を責めるより、あの子のところへ連れていって。") => "今は、ミナのところへ連れていって。迎えに行くの、手伝って。",
                (Job.Melee, "ええ。今度はわたしが、あの子の声を聞きに行くわ。") => "うん。今度は、ミナの声を聞きに行こう。",
                (Job.Melee, "……最後の、わたしの声だわ。どうしてここに？") => "最初の声、覚えてる。……あれ、あたしだ。",
                (Job.Melee, "聞こえた。回線、つないでおいて。") => "聞こえたよ。切らないでね。",
                (Job.Heal, "差出人のところ、文字が崩れてる。……中には入れるのね？") => "名前のところだけ読めないね。ここから潜れる？",
                (Job.Heal, "だったら、わたしを送って。今度は、わたしが呼ぶ。") => "じゃあ、あたしを送って。今度は、あたしが呼びたい。",
                (Job.Heal, "道を開けられるのは、あんたでしょう。そっちを頼むって言ってるの。") => "道を開いてくれるの、あなただもん。帰り道も、お願いしていい？",
                (Job.Heal, "ええ。返事を探して黙ってしまっても、こっちから呼ぶわ。") => "うん。お返事が出ないときも、あたしから呼ぶね。",
                (Job.Heal, "なら、道はあんたに任せる。あの子が動けなければ、わたしが手を貸すわ。") => "道、お願い。あたしはミナの手をつなぐ。途中で休んでも、離さないよ。",
                (Job.Heal, "今は、自分を責めるより、あの子のところへ連れていって。") => "今は、ミナのところへ連れていって。あたしも、力になりたい。",
                (Job.Heal, "ええ。今度はわたしが、あの子の声を聞きに行くわ。") => "うん。今度は、あたしが聞く番だね。",
                (Job.Heal, "……最後の、わたしの声だわ。どうしてここに？") => "ふたつ目の声……あたし、あんな声だったんだ。",
                (Job.Heal, "聞こえた。回線、つないでおいて。") => "うん。あなたの声も、まだ聞こえてるよ。",
                _ => line.text.Replace("レイさん", name + "さん").Replace("レイ、さん", name + "、さん"),
            };
            lines[i] = line;
        }
        return lines;
    }

    public override void _Ready()
    {
        Hud.SetElapsed((float)Elapsed);
        _step = 1;
        World.ProcessMode = ProcessModeEnum.Disabled;
        var game = GetNodeOrNull<GameManager>("/root/Game");
        _lunatic = game?.IsLunatic == true;
        game?.SetStageTarget(EchoTotal + 1, hasBoss: true);
        // 導入は S3-7 の分岐受け1行だけが可変。
        // ★FINAL はジョブに関わらず常にミナ本編（2026-09-15 ユーザー承認仕様）。
        //   旧 CompanionDialogue.Final（他ジョブ時に導入をその子の掛け合いへ置換）は廃止した。
        //   他ジョブの専用ストーリーは STAGE1〜3 だけ（CharacterStory 参照）＝この面は固定。
        var intro = new System.Collections.Generic.List<(int who, string text, string face)>(IntroHead);
        intro.AddRange(S37Quote(game));
        intro.AddRange(IntroTail);
        _intro = AdaptIntro(intro.ToArray(), game?.SelectedJob ?? Job.Magic);
        // ───── チェックポイント入口「ボスから」（2026-09-27）─────
        //   ゲームオーバーの「ボスから・スコア半分消費」／R 単体が SelectedEntry をセットしてくる。
        //   導入（F1）と残響の三波を飛ばし、step 3＝Step_BossSpawn からミナ戦で始める
        //   ＝本編3面（StageAkari/StageKoharu/StageRei の _Ready）と同じ作法。
        //   ・FINAL に中ボスは居ないので Start 以外は全部「ボスから」に落とす。
        //   ・導入（Step_Lines）が起こすはずの World を自分で起こす（さもないと誰も動かない）。
        //   ・FINAL の入りの儀式（5.2 秒のタイトルカードと一突き）は出さない。本編3面のバナーは
        //     進行を止めないが、こちらは出ているあいだ _Process ごと止める＝倒れるたび 5.2 秒
        //     待たされる。面の頭に戻ったのではなくボス戦の続きなので、儀式は省く。
        //   ・読んだら消す（本編3面と同じ。残さないと Shift+R の「最初から」が前回の入口で再開する）。
        //     ただし --boss デバッグ中は「毎回ボスから」を保つため貼り直す。
        //   ・道中で得るはずだったパワーアップは本編3面と同じく補填しない
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
        if (_step == 4 && IsInstanceValid(_boss) && _boss.IsPurified) RecordClear();
        if (Hud.CinematicMode) { _zHeld = Pad.AdvanceHeld(); return; }
        _lineHold += delta;
        if (!_clearRecorded && !Hud.BubblePaused && Player.Lives > 0) { _stageElapsed += delta; Hud.SetElapsed((float)_stageElapsed); }
        // 会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        _zEdge = z && !_zHeld && !Pad.UiBlocked(this);
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
            return;
        }
        if (_zEdge && !Hud.DialogRevealed)
        {
            Hud.RevealDialogNow(); _lineHold = 0;
        }
        else if (Hud.DialogRevealed
                 && (_zEdge || (Hud.FastForwarding && _lineHold >= 0.15) || (Hud.AutoAdvanceReady && _lineHold >= 1.4)))
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
            Hud.LineKind.Boy => "",
            Hud.LineKind.Companion => face,                                                             // 「あなた」に顔は無い
            Hud.LineKind.Mina => string.IsNullOrEmpty(face) ? "res://char/v3/mina_conversation_v1.png" : face, // ミナも行ごと表情
            _ => "res://char/v3/mina_conversation_v1.png",
        };
        Hud.ShowDialog(kind, text, portrait, otherName: "ミナ");
    }
    private static int EchoTotal
    {
        get { int n = 0; foreach (var w in EchoWaves) n += w.count; return n; }
    }

    private void Step_Route()
    {
        var game = GetNode<GameManager>("/root/Game");
        if (!_stepStarted)
        {
            _stepStarted = true;
            _wave = 0;
            _waveBase = game.PurifiedCount;
            // 残響の道中に曲を立てる（2026-09-29）。導入（F1）の沈黙はそのまま残し、**戦闘が始まる
            //   この瞬間から**鳴らす。フェードを長め（2.4秒）に取り、曲が立ち上がるのではなく
            //   残響が滲み出してくるように入れる（波Aのコメント「残響が滲み出す」と揃える）。
            //   曲の正体と選定根拠は Audio.BgmFinalRoute のコメント。BossMina が BgmBossMina へ
            //   クロスフェードして引き取る＝道中→ボスの段差はそこで一度だけ付く。
            Audio.Instance?.Music(Audio.Instance.BgmFinalRoute, 2.4f);
            StartEchoWave();
        }
        if (Hud.BubblePaused) return;
        if (!_echoSpawner.Finished) return;
        if (_wave + 1 < EchoWaves.Length)
        {
            if (game.PurifiedCount - _waveBase < EchoWaves[_wave].count && !game.SpawnTargetReached) return;
            _wave++;
            _waveBase = game.PurifiedCount;
            _echoSpawner.Stop();
            _echoSpawner.QueueFree();
            StartEchoWave();   // 盤面は掃かない＝前の波の残りに次の波が重なる
            return;
        }
        // 最後の波は「予定数が湧き切ってから」ミナ本体へ（数を削って早出しにしない）。
        if (!Spawner.BattlefieldClear(GetTree())) return;
        game.FinishStageRoad();

        _echoSpawner.Stop();
        _echoSpawner.QueueFree();
        // Residual purification waves must not peel the boss's opening shield.
        foreach (Node node in World.GetChildren())
            if (node is Ripple) node.QueueFree();
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
            var game = GetNode<GameManager>("/root/Game");
            game.NotifyBossReached();
            game.RevealFinalBoss();
            if (!_lunatic) CameoIntroScene.PlayBoss(Hud, World, "mina", System.Array.Empty<(int, string, string)>(), () => {
                _boss.GetNode<BossPostSequence>("PostSequence").ResumeMusic();
                _zHeld = Pad.AdvanceHeld(); _zEdge = false;
            }, SpawnBoss);
            else SpawnBoss();
        }

        void SpawnBoss()
        {
            _boss = new BossMina { Name = "BossMina" };
            World.AddChild(_boss);
            _boss.GlobalPosition = new Vector2(SpawnX, 70f);
            // ボス戦の開始＝ミナが湧き位置に立つ仕切り直し（直前に残響とその弾を掃き切っている）。
            //   自機も初期位置へ戻す（ルナティックでも戻す）。残響の波の継ぎ目では戻さない＝FINAL の道中は
            //   意図して盤面を掃かず前の波と重ねる設計（Step_Route のコメント）なので、生きた弾の中を
            //   運ぶことになり理不尽。段の仕切りがここ1点しか無いのも同じ理由。
            Player?.ReturnToStart();
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
            AttackDensity = GetNode<GameManager>("/root/Game").Difficulty == GameManager.Diff.Easy ? 1.8f : 1.35f,
            CharactersOnly = true,          // 残響3種だけ（他テーマ流用のスキンを混ぜない）
            IgnoreStageCleared = _lunatic,  // ルナティックは目標到達でも止めない（空白を作らない）
        };
        AddChild(_echoSpawner);
        _echoSpawner.Begin();
        var (from, to) = _wave switch { 0 => (0f, 1.95f), 1 => (2f, 2.95f), _ => (3f, 4f) };
        GetParent().GetNode<StageBackground>("StageBackground").TrackRoute(_echoSpawner, from, to);
    }
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
    private void RecordClear()
    {
        if (_clearRecorded) return;
        _clearRecorded = true;
        var game = GetNode<GameManager>("/root/Game");
        _clearTimeRecord = game.RecordClearTime(GameManager.FinalStageId, game.Difficulty, (float)_stageElapsed);
        _clearScore = game.Score;
        _clearScoreRecord = game.RecordScore(GameManager.FinalStageId, game.Difficulty, _clearScore);
        game.SettleStageScore();
        // エンディングの途中で終了しても撃破記録が残る、必須のクリアセーブ。
        game.SaveToSlot(0);
    }

    private void Step_Transition()
    {
        RecordClear();
        if (LunaticUnlock.PlayIfPending(GetParent())) return;
        if (!_returnShown)
        {
            _returnShown = true;
            MinaStoryFilm.Play(Hud, World, aftermath: true, completed: () => _zHeld = Pad.AdvanceHeld());
            return;
        }
        if (_clearing) return;
        _clearing = true;
        GetNode<GameManager>("/root/Game").SaveToSlot(0);
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        // 撃破＝穢れを祓った。本決着（対話で帰還）は Final へ委ねる。
        // 暗転してから渡す（ボス背景のフラッシュ止め・2026-09-22。StageRei と同じ理由）。
        GameManager.FadeToScene(this, "res://Final.tscn");
    }

    // ルナティックの締め：アフター（MinaStoryFilm）も Final（F4）も Epilogue も流さない。
    //   FINAL CLEAR のリザルトを表示してからホームへ戻る。
    private void Step_LunaticFinish(double delta)
    {
        if (_returnShown && LunaticUnlock.PlayIfPending(GetParent())) return;
        if (!_returnShown)
        {
            _returnShown = true;
            RecordClear();
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            Hud.ShowClearBanner("FINAL CLEAR", (float)_stageElapsed, _clearTimeRecord.isBest, _clearTimeRecord.prev,
                _clearScore, _clearScoreRecord.isBest, _clearScoreRecord.prev);
            return;
        }
        _lunaticClearT += delta;
        if (_lunaticClearT < LunaticClearHold || _clearing) return;
        _clearing = true;
        var game = GetNode<GameManager>("/root/Game");
        game.SetContamination(0);
        game.SaveToSlot(0);
        GameManager.FadeToScene(this, "res://Hub.tscn");
    }

}
