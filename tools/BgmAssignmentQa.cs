using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// BgmAssignmentQa : 「どの場面でどの曲が鳴るか」の自動検証（2026-09-29）。
//   耳で確かめられないもの（音色・良し悪し）は見ない。**配線として正しいか**だけを機械的に見る:
//     ① audio/bgm_*.ogg が実在し、Audio の各スロットが**合成フォールバックに落ちていない**
//        （インポート漏れ・ファイル名の打ち間違いは、実行すると無警告で合成音に化けるので必ず検出する）
//     ② ループ設定（エンディング映像からロールまでの専用曲だけ Loop false）
//     ③ StageBgm() / StoryBgm() のマップが意図どおり（FINAL の "mina" を含む）
//     ④ 同じ主題を予定した場面で共有し、それ以外の曲を誤って流用していないか
//     ⑤ FINAL の道中（残響の三波）で曲が鳴っている（2026-09-27 に 7 秒→70 秒へ延びた区間）
//     ⑥ ルナティックでもボステーマが**こもったままにならない**
//        （投稿の割り込みを畳む難易度なので、札の数で開くローパスが開かずに固定される事故）
//
//   実行: APPDATA=<repo>/build/qa_story/bgm_appdata
//         godot --headless --path <repo> res://tools/qa_bgm_assignment.tscn
//   ※ --qa は付けないこと（Audio.Muted=true になって Music() が何もしなくなり、判定が空振りする）。
public partial class BgmAssignmentQa : Node
{
    private const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo CurrentMusicField = typeof(Audio).GetField("_currentMusic", Priv)!;

    private int _fail;
    private Audio _audio = null!;

    // 合成のままで正しいスロット（設計意図。BGM/acquisition_list.md §1 の #12）。
    private static readonly string[] SynthSlots = { "BgmStage", "BgmBoss" };
    // ループしない曲（.import が loop=false）。
    private static readonly string[] OneShotSlots = { "BgmOpening", "BgmEndingFilm" };

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Engine.MaxFps = 60;
        await Frames(2);
        var self = GetTree().CurrentScene;
        if (self == this) GetTree().CurrentScene = null;

        _audio = GetNode<Audio>("/root/Audio");
        Check("Audio が常駐している", _audio != null, "オートロードが無い");
        Check("Audio.Muted=false（--qa を付けていない）", !_audio!.Muted, "Muted だと Music() が働かず判定できない");

        bool storyOnly = OS.GetCmdlineUserArgs().Contains("--story-themes-only");
        bool encountersOnly = OS.GetCmdlineUserArgs().Contains("--encounters-only");
        bool scenesOnly = OS.GetCmdlineUserArgs().Contains("--scenes-only");
        if (!storyOnly && !encountersOnly && !scenesOnly)
        {
            StaticChecks();
            await ThemePlaybackChecks();
            await BossArrangementChecks();
        }
        if (!scenesOnly)
        {
            if (!encountersOnly) await CharacterThemeChecks();
            await StageMusicChecks();
            await EncounterMusicChecks();
        }
        if (!storyOnly && !encountersOnly && !OS.GetCmdlineUserArgs().Contains("--themes-only"))
        {
            if (!scenesOnly) await OpeningMusicChecks();
            await SceneChecks();
        }

        // 終了前に畳み切る（QueueFree の実処理は次フレーム。待たずに Quit すると解放漏れの警告が出る）。
        //   Audio は常駐なので、クロスフェード用の2つのプレイヤーが最後に鳴らした ogg を掴んだままになる。
        //   合成BGMを2回続けて張って両方のプレイヤーを合成音に置き換えてから止める＝ogg の参照を手放す。
        Engine.TimeScale = 1;
        _audio.Music(_audio.BgmStage, 0);
        await Frames(4);
        _audio.Music(_audio.BgmBoss, 0);
        await Frames(4);
        _audio.StopMusic(0);
        await Frames(12);
        GD.Print(_fail == 0 ? "[bgmqa] ALL OK" : $"[bgmqa] FAILED: {_fail}");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // ───────── ① 〜 ④ シーンを立てずに見られるもの ─────────
    private void StaticChecks()
    {
        var slots = typeof(Audio).GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Where(f => typeof(AudioStream).IsAssignableFrom(f.FieldType) && f.Name.StartsWith("Bgm"))
            .OrderBy(f => f.Name).ToArray();
        Check("BGM スロットを列挙できた", slots.Length >= 25, $"見つかったのは {slots.Length} 枠");

        var byPath = new Dictionary<string, List<string>>();
        foreach (var f in slots)
        {
            var stream = f.GetValue(_audio) as AudioStream;
            if (stream == null)
            {
                Check($"{f.Name}: 曲がある", false, "null（曲が読めていない）");
                continue;
            }

            if (SynthSlots.Contains(f.Name))
            {
                Check($"{f.Name}: 合成のまま（設計意図）", stream is AudioStreamWav,
                      $"実音源になっている: {stream.ResourcePath}");
                continue;
            }

            // 実音源スロット。合成（AudioStreamWav）に落ちていたら「ローダーのフォールバックが
            //   働いた＝ファイルが無い/インポートされていない」＝無警告で音が別物になっている。
            bool real = stream is AudioStreamOggVorbis || stream is AudioStreamMP3;
            Check($"{f.Name}: 実音源を読めている", real,
                  $"合成フォールバックに落ちている（型={stream.GetType().Name}）");
            if (!real) continue;

            string path = stream.ResourcePath;
            Check($"{f.Name}: ファイルが実在（{path}）", !string.IsNullOrEmpty(path) && ResourceLoader.Exists(path),
                  "ResourcePath が空か存在しない");

            bool loop = stream is AudioStreamOggVorbis ogg ? ogg.Loop : ((AudioStreamMP3)stream).Loop;
            bool wantLoop = !OneShotSlots.Contains(f.Name);
            Check($"{f.Name}: ループ設定 {(wantLoop ? "true" : "false")}", loop == wantLoop,
                  $"Loop={loop}");

            if (!string.IsNullOrEmpty(path))
            {
                if (!byPath.TryGetValue(path, out var users)) byPath[path] = users = new List<string>();
                users.Add(f.Name);
            }
        }

        var sharedThemes = new Dictionary<string, string[]>
        {
            ["res://audio/bgm_menu_mina.ogg"] = new[] { "BgmMenu", "BgmShop" },
            ["res://audio/bgm_theme_mina.ogg"] = new[] { "BgmStoryMina", "BgmFinalCutscene" },
            ["res://audio/bgm_theme_akari.ogg"] = new[] { "BgmStoryAkari", "BgmStoryAkariAfter" },
            ["res://audio/bgm_theme_koharu.ogg"] = new[] { "BgmStoryKoharu", "BgmStoryKoharuAfter" },
            ["res://audio/bgm_theme_rei.ogg"] = new[] { "BgmStoryRei", "BgmStoryReiAfter" },
            ["res://audio/bgm_stage_akari.ogg"] = new[] { "BgmStageAkari", "BgmTraining" },
        };
        foreach (var (path, users) in byPath.Where(kv => kv.Value.Count > 1))
            Check($"主題を意図した場面だけで共有する: {path}",
                  sharedThemes.TryGetValue(path, out var expected) && new HashSet<string>(users).SetEquals(expected),
                  $"使用枠={string.Join(" / ", users)}");
        foreach (var (path, expected) in sharedThemes)
            Check($"主題の再登場先がそろっている: {path}",
                  byPath.TryGetValue(path, out var users) && new HashSet<string>(users).SetEquals(expected),
                  $"予定枠={string.Join(" / ", expected)}");

        // ③ ステージ→道中曲のマップ。
        CheckSame("StageBgm(rei)", _audio.StageBgm("rei"), _audio.BgmStageRei);
        CheckSame("StageBgm(akari)", _audio.StageBgm("akari"), _audio.BgmStageAkari);
        CheckSame("StageBgm(koharu)", _audio.StageBgm("koharu"), _audio.BgmStageKoharu);
        Check("FINAL 道中は専用の戦闘曲を使う", _audio.BgmFinalRoute.ResourcePath == "res://audio/bgm_final_route.ogg",
              $"再生対象={_audio.BgmFinalRoute.ResourcePath}");
        CheckSame("StageBgm(tutorial)", _audio.StageBgm("tutorial"), _audio.BgmStageW0);
        CheckSame("StageBgm(mina)＝FINAL 道中", _audio.StageBgm("mina"), _audio.BgmFinalRoute);
        CheckSame("StageBgm(未知)＝合成へ", _audio.StageBgm("???"), _audio.BgmStage);

        // ③ 回想→曲のマップ（ミナの aftermath だけ意図的に無音）。
        CheckSame("StoryBgm(rei,memory)", _audio.StoryBgm("rei", false), _audio.BgmStoryRei);
        CheckSame("StoryBgm(rei,after)", _audio.StoryBgm("rei", true), _audio.BgmStoryReiAfter);
        CheckSame("StoryBgm(akari,memory)", _audio.StoryBgm("akari", false), _audio.BgmStoryAkari);
        CheckSame("StoryBgm(akari,after)", _audio.StoryBgm("akari", true), _audio.BgmStoryAkariAfter);
        CheckSame("StoryBgm(koharu,memory)", _audio.StoryBgm("koharu", false), _audio.BgmStoryKoharu);
        CheckSame("StoryBgm(koharu,after)", _audio.StoryBgm("koharu", true), _audio.BgmStoryKoharuAfter);
        CheckSame("StoryBgm(mina,memory)", _audio.StoryBgm("mina", false), _audio.BgmStoryMina);
        Check("StoryBgm(mina,after)＝意図的な無音", _audio.StoryBgm("mina", true) == null, "曲が入っている");
    }

    private async Task ThemePlaybackChecks()
    {
        foreach (var (stream, path, length, once) in new[]
        {
            (_audio.BgmOpening, "res://audio/bgm_opening.mp3", 241.48, true),
            (_audio.BgmBossAkari, "res://audio/bgm_boss_akari_instrumental.ogg", 223.408, false),
            (_audio.BgmBossAkariRemix, "res://audio/bgm_boss_akari_remix.ogg", 202.768, false),
            (_audio.BgmBossKoharu, "res://audio/bgm_boss_koharu_instrumental.ogg", 222.48, false),
            (_audio.BgmBossKoharuRemix, "res://audio/bgm_boss_koharu_remix.ogg", 217.824, false),
            (_audio.BgmBossRei, "res://audio/bgm_boss_rei_instrumental.ogg", 233.664, false),
            (_audio.BgmBossReiRemix, "res://audio/bgm_boss_rei_remix.ogg", 197.968, false),
            (_audio.BgmBossMina, "res://audio/bgm_boss_mina_instrumental.ogg", 169.856, false),
            (_audio.BgmBossMinaRemix, "res://audio/bgm_boss_mina_remix.ogg", 170.576, false),
        })
        {
            Check($"指定音源を読み込む: {path}", stream.ResourcePath == path,
                  $"再生対象={stream.ResourcePath}");
            Check($"指定音源の尺: {path}", Math.Abs(stream.GetLength() - length) < 0.1,
                  $"期待={length}s 実際={stream.GetLength()}s");
            if (once) _audio.MusicOnce(stream, 0);
            else _audio.Music(stream, 0);
            await Frames(4);
            var player = ActiveMusicPlayer();
            double loopOffset = stream is AudioStreamOggVorbis ogg ? ogg.LoopOffset : 0;
            Check($"指定音源の再生が開始する: {path}", player.Playing && player.Stream == stream,
                  "再生されていない");
            player.Seek((float)stream.GetLength() - 0.15f);
            await Task.Delay(500);
            await Frames(2);
            Check($"曲末の{(once ? "停止" : "ループ")}: {path}", once
                  ? !player.Playing && Now() == null
                  : player.Playing && player.GetPlaybackPosition() >= loopOffset
                    && player.GetPlaybackPosition() < loopOffset + 1.0 && Now() == stream,
                  $"playing={player.Playing} position={player.GetPlaybackPosition():0.000}");
            _audio.StopMusic(0);
            await Frames(4);
        }
    }

    private async Task BossArrangementChecks()
    {
        var game = GetNode<GameManager>("/root/Game");
        var difficultyBefore = game.Difficulty;
        game.Difficulty = GameManager.Diff.Lunatic;
        game.AutoAdvanceDialog = true;
        Engine.TimeScale = 8;
        var root = GD.Load<PackedScene>("res://MinaBattle.tscn").Instantiate<MinaRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        root.Stage.SetProcess(false);
        root.World.ProcessMode = ProcessModeEnum.Inherit;
        root.Player.ProcessMode = ProcessModeEnum.Disabled;
        root.Hud.HideBubble();
        foreach (bool cinematic in new[] { true, false })
        {
            foreach (var id in new[] { "akari", "koharu", "rei", "mina" })
            {
                game.Difficulty = GameManager.Diff.Lunatic;
                Enemy boss = id switch
                {
                    "akari" => new BossAkari(), "koharu" => new BossKoharu(),
                    "rei" => new BossRei(), _ => new BossMina(),
                };
                boss.Position = new Vector2(300, 70);
                root.World.AddChild(boss);
                boss.ProcessMode = ProcessModeEnum.Disabled;
                var posts = boss.GetNode<BossPostSequence>("PostSequence");
                var normal = Now();
                Check($"{id}: 初回はインスト", normal?.ResourcePath == $"res://audio/bgm_boss_{id}_instrumental.ogg", Describe(normal));
                if (boss is BossMina)
                {
                    typeof(BossMina).GetMethod("BeginPhaseTransition", Priv)!.Invoke(boss, null);
                    typeof(BossMina).GetMethod("CompletePhaseTransition", Priv)!.Invoke(boss, null);
                    posts.ResumeMusic(0);
                    CheckSame("mina: 人型の衣装変更では曲を変えない", Now(), normal);
                    typeof(BossMina).GetMethod("BeginPhaseTransition", Priv)!.Invoke(boss, null);
                }
                game.Difficulty = cinematic ? GameManager.Diff.Normal : GameManager.Diff.Lunatic;
                if (boss is BossMina)
                    typeof(BossMina).GetMethod("CompletePhaseTransition", Priv)!.Invoke(boss, null);
                else typeof(Enemy).GetMethod("AdvanceForm2", Priv)!.Invoke(boss, null);
                if (cinematic) CheckSame($"{id}: 変身の予備動作中は通常曲", Now(), normal);
                bool switched = await Until(() => Now()?.ResourcePath == $"res://audio/bgm_boss_{id}_remix.ogg", 1800);
                Check($"{id}/{cinematic}: 変身の着地でREMIXへ切替", switched, Describe(Now()));
                var remix = Now();
                if (cinematic)
                {
                    bool ended = await Until(() => GetTree().GetNodesInGroup("boss_transform").Count == 0, 1800);
                    Check($"{id}: 変身演出が終了する", ended, "終了しない");
                }
                else Check($"{id}: LUNATICは戦闘を止めない", !boss.Transforming, "演出が残っている");
                await MusicFade();
                Check($"{id}: 調整済み音源の再生音量", Math.Abs(ActiveMusicPlayer().VolumeDb) < 0.05,
                      $"volume={ActiveMusicPlayer().VolumeDb}");
                _audio.Music(_audio.StoryBgm(id, false), 0);
                posts.ResumeMusic(0);
                CheckSame($"{id}: 回想後もREMIXへ復帰", Now(), remix);
                boss.QueueFree();
                await Frames(6);
            }
        }
        await CloseStage(root);
        Engine.TimeScale = 1;
        game.AutoAdvanceDialog = false;
        game.Difficulty = difficultyBefore;

        for (int index = 0; index < 4; index++)
        {
            var practice = GD.Load<PackedScene>(BossPracticeRoot.ScenePath).Instantiate<BossPracticeRoot>();
            typeof(BossPracticeRoot).GetField("_bossIndex", Priv)!.SetValue(practice, index);
            typeof(BossPracticeRoot).GetField("_phase", Priv)!.SetValue(practice, index == 3 ? 2 : 1);
            GetTree().Root.AddChild(practice);
            GetTree().CurrentScene = practice;
            practice.StartPractice();
            Check($"練習{index}: 変身後の技はREMIX", Now()?.ResourcePath.EndsWith("_remix.ogg") == true, Describe(Now()));
            practice.RestartPractice();
            Check($"練習{index}: 再試行もREMIX", Now()?.ResourcePath.EndsWith("_remix.ogg") == true, Describe(Now()));
            await CloseStage(practice);
        }
    }

    private async Task CharacterThemeChecks()
    {
        var game = GetNode<GameManager>("/root/Game");
        var root = new Node2D();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        var world = new Node2D();
        root.AddChild(world);
        var hud = new Hud();
        root.AddChild(hud);
        root.AddChild(new BubbleLayer { Hud = hud });
        var themes = new (string id, Action<Hud, Node, bool, Action> play)[]
        {
            ("mina", MinaStoryFilm.Play), ("akari", AkariStoryFilm.Play),
            ("koharu", KoharuStoryFilm.Play), ("rei", ReiStoryFilm.Play),
        };
        foreach (var (id, play) in themes)
        {
            var stream = _audio.StoryBgm(id, false) as AudioStreamOggVorbis;
            Check($"{id}: 専用テーマ", stream?.ResourcePath == $"res://audio/bgm_theme_{id}.ogg",
                  Describe(stream));
            if (stream == null) continue;
            Check($"{id}: イントロの後へ戻る長いループ", stream.Loop && stream.LoopOffset > 5
                  && stream.GetLength() - stream.LoopOffset >= 90, $"offset={stream.LoopOffset}");
            int completed = 0;
            play(hud, world, false, () => completed++);
            await Frames(90);
            await MusicFade();
            CheckSame($"{id}: 実際の回想でテーマに切り替わる", Now(), stream);
            var player = ActiveMusicPlayer();
            Check($"{id}: 調整済み音源を余分に減衰させない", Math.Abs(player.VolumeDb) < 0.05,
                  $"volume={player.VolumeDb}");
            for (int repeat = 0; repeat < 3; repeat++)
            {
                player.Seek((float)stream.GetLength() - 0.15f);
                await Task.Delay(500);
                await Frames(2);
                Check($"{id}: 反復 {repeat + 1} でイントロを飛ばす", player.Playing
                      && player.GetPlaybackPosition() >= stream.LoopOffset
                      && player.GetPlaybackPosition() < stream.LoopOffset + 1, $"position={player.GetPlaybackPosition()}");
            }
            GetTree().Paused = true;
            await Frames(4);
            double pausedPosition = player.GetPlaybackPosition();
            await Task.Delay(200);
            await Frames(2);
            Check($"{id}: ポーズ中もループBGMを継続する", !player.StreamPaused && player.Playing
                  && player.GetPlaybackPosition() > pausedPosition, "既存のBGM継続動作が変わった");
            GetTree().Paused = false;
            await Frames(4);
            Check($"{id}: ポーズ解除で曲が巻き戻らない", !player.StreamPaused && player.Playing
                  && player.GetPlaybackPosition() > pausedPosition, "再生位置が戻った");
            string? capture = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--theme-captures="));
            if (capture != null && DisplayServer.GetName() != "headless")
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var path = capture.Substring("--theme-captures=".Length) + $"/{id}.png";
                Check($"{id}: 回想の描画を保存", GetViewport().GetTexture().GetImage().SavePng(path) == Error.Ok, path);
            }
            game.AutoAdvanceDialog = true;
            Engine.TimeScale = 8;
            bool ended = await Until(() => completed == 1, 5000);
            Check($"{id}: 回想を最後まで読んで自然終了する", ended, $"completed={completed}");
            Engine.TimeScale = 1;
            game.AutoAdvanceDialog = false;
            await Frames(12);
            _audio.Music(_audio.BgmBoss, 0.7f);
            await MusicFade();
            Check($"{id}: 回想後に古いテーマが残らない", !_audio.GetChildren().OfType<AudioStreamPlayer>()
                  .Any(p => p.Stream == stream && p.Playing),
                  $"current={Describe(Now())} scale={Engine.TimeScale} paused={GetTree().Paused} "
                  + string.Join("; ", _audio.GetChildren().OfType<AudioStreamPlayer>()
                      .Where(p => p.Playing && p.Stream is AudioStreamOggVorbis)
                      .Select(p => $"{p.Stream.ResourcePath} volume={p.VolumeDb} paused={p.StreamPaused}")));
        }
        await CloseStage(root);
    }

    private async Task OpeningMusicChecks()
    {
        var prologue = GD.Load<PackedScene>("res://Prologue.tscn").Instantiate<Prologue>();
        GetTree().Root.AddChild(prologue);
        prologue.SetProcess(false);
        GetTree().CurrentScene = prologue;
        CheckSame("プロローグの会話は元の曲を使う", Now(), _audio.BgmPrologue);

        int completed = 0;
        var film = new OpeningFilm { Completed = () => completed++ };
        prologue.AddChild(film);
        await Frames(4);
        CheckSame("OP開始で声の残響へ切り替わる", Now(), _audio.BgmOpening);
        var player = ActiveMusicPlayer();
        double elapsed = film.Elapsed;
        GetTree().Paused = true;
        await Frames(4);
        Check("OPのポーズ中は映像と曲を一緒に止める", player.StreamPaused && film.Elapsed == elapsed,
              "映像と曲が同期して止まらない");
        GetTree().Paused = false;
        await Frames(4);
        Check("OPのポーズ解除で同じ曲を再開する", !player.StreamPaused && player.Playing
              && ActiveMusicPlayer() == player, "再生が再開しない、または再生先が変わった");

        Engine.TimeScale = 8;
        bool continuous = true;
        for (int i = 0; i < 1800 && completed == 0; i++)
        {
            if (film.Elapsed < OpeningFilm.Duration - 1.3)
                continuous &= Now() == _audio.BgmOpening && ActiveMusicPlayer() == player && player.Playing;
            await Frames(1);
        }
        Check("OP途中でボス曲へ変えず同じ演奏を継続する", continuous, $"再生中={Describe(Now())}");
        Check("OP自然終了で曲をフェードアウトする", completed == 1 && Now() == null,
              $"completed={completed} 再生中={Describe(Now())}");
        Engine.TimeScale = 1;
        await Frames(4);

        film = new OpeningFilm { Completed = () => completed++ };
        prologue.AddChild(film);
        CheckSame("OPの再視聴でも専用曲を再生する", Now(), _audio.BgmOpening);
        player = ActiveMusicPlayer();
        await Until(() => film.Elapsed >= 0.7, 120);
        film.RequestSkip();
        await Until(() => completed == 2, 120);
        Check("OPスキップで曲と終了通知を止める", completed == 2 && Now() == null
              && Field(_audio, "_oncePlayer") == null, "曲または終了通知が残っている");
        _audio.Music(_audio.BgmPrologue, 0);
        await Frames(4);
        player.EmitSignal(AudioStreamPlayer.SignalName.Finished);
        CheckSame("OPの終了通知で次の場面を無音にしない", Now(), _audio.BgmPrologue);
        await CloseStage(prologue);
    }

    // ───────── ⑤ ⑥ 実際にシーンを立てて確かめるもの ─────────
    private async Task StageMusicChecks()
    {
        Engine.TimeScale = 1;
        var stageTracks = MusicLibrary.Load().Where(t => t.Category == "stage").ToArray();
        Check("新しい道中5曲がSuno曲だけのライブラリにある", stageTracks.Length == 5, $"count={stageTracks.Length}");
        foreach (var track in stageTracks)
        {
            var stream = GD.Load<AudioStreamMP3>(track.Audio);
            Check($"{track.Title}: アプリ用はフル尺原曲", !stream.Loop && stream.GetLength() >= 175
                && Math.Abs(stream.GetLength() - track.Duration) < .15, $"length={stream.GetLength()}");
            bool ended = false;
            var player = new AudioStreamPlayer { Stream = stream, Bus = "Music", VolumeDb = -60 };
            AddChild(player);
            player.Finished += () => ended = true;
            player.Play((float)stream.GetLength() - .15f);
            await Task.Delay(500);
            await Frames(2);
            Check($"{track.Title}: フル尺の末尾で自然終了", ended && !player.Playing, "最後まで再生できない、またはループする");
            player.QueueFree();
        }
        foreach (string id in new[] { "tutorial", "akari", "koharu", "rei", "mina" })
        {
            _audio.SetStageMusic(id);
            await MusicFade();
            var stream = Now() as AudioStreamOggVorbis;
            Check($"{id}: 道中に長いSunoループを読み込む", stream != null && stream.Loop
                && stream.LoopOffset > 5 && stream.GetLength() - stream.LoopOffset >= 90, Describe(stream));
            if (stream == null) continue;
            var player = ActiveMusicPlayer();
            Check($"{id}: 道中の音量補正が二重に掛からない", Math.Abs(player.VolumeDb) < .05,
                $"volume={player.VolumeDb}");
            for (int repeat = 0; repeat < 2; repeat++)
            {
                player.Seek((float)stream.GetLength() - .15f);
                await Task.Delay(500);
                await Frames(2);
                Check($"{id}: 道中ループ {repeat + 1}", player.Playing
                    && player.GetPlaybackPosition() >= stream.LoopOffset
                    && player.GetPlaybackPosition() < stream.LoopOffset + 1, $"position={player.GetPlaybackPosition()}");
            }
        }
    }

    private async Task EncounterMusicChecks()
    {
        var game = GetNode<GameManager>("/root/Game");
        var previousJob = game.SelectedJob;
        game.AutoSaveEnabled = false;
        game.AutoAdvanceDialog = true;
        Engine.TimeScale = 8;
        typeof(QaPilot).GetProperty("GodActive")!.SetValue(null, true);
        foreach (var (id, scene) in new[] { ("akari", "Akari"), ("koharu", "Koharu"), ("rei", "Rei"), ("mina", "MinaBattle") })
        {
            foreach (var entry in id == "mina" ? new[] { GameManager.StageEntry.Boss }
                : new[] { GameManager.StageEntry.MidBoss, GameManager.StageEntry.Boss })
            {
                game.SelectedJob = entry == GameManager.StageEntry.MidBoss ? Job.Magic : Job.Tank;
                var (root, stage) = await OpenStage($"res://{scene}.tscn", game, GameManager.Diff.Normal, entry, true);
                string group = entry == GameManager.StageEntry.Boss ? "boss_intro" : "cameo_intro";
                bool arrived = await Until(() => GetTree().GetFirstNodeInGroup(group) is Node intro
                    && Field(intro, "_talking") is true, 4000);
                Check($"{id}/{entry}: 実際の登場会話まで進む", arrived, $"step={ReadInt(stage, "_step")}");
                CheckSame($"{id}/{entry}: 相手のテーマ曲で会話する", Now(), _audio.StoryBgm(id, false));
                await Frames(12);
                CheckSame($"{id}/{entry}: ボス生成で会話曲を上書きしない", Now(), _audio.StoryBgm(id, false));
                if (DisplayServer.GetName() != "headless" && entry == GameManager.StageEntry.Boss)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng($"res://build/qa_story/stage_music_20261104/{id}_conversation.png");
                }
                bool ended = await Until(() => GetTree().GetFirstNodeInGroup(group) == null, 6000);
                Check($"{id}/{entry}: 会話が自然終了する", ended, "会話が終わらない");
                var expected = id switch { "akari" => _audio.BgmBossAkari, "koharu" => _audio.BgmBossKoharu,
                    "rei" => _audio.BgmBossRei, _ => _audio.BgmBossMina };
                bool resumed = await Until(() => Now() == expected, 500);
                Check($"{id}/{entry}: 戦闘曲に復帰する", resumed, Describe(Now()));
                if (entry == GameManager.StageEntry.MidBoss && Field(stage, "_cameo") is CameoBoss cameo)
                {
                    typeof(CameoBoss).GetMethod("OnCryStart", Priv)!.Invoke(cameo, null);
                    CheckSame($"{id}: 中ボス退場会話も専用テーマ", Now(), _audio.StoryBgm(id, false));
                    typeof(CameoBoss).GetMethod("OnCryEnd", Priv)!.Invoke(cameo, null);
                    CheckSame($"{id}: 中ボス退場後は道中の新曲へ復帰", Now(), _audio.StageBgm(id));
                }
                await CloseStage(root);
            }
        }
        game.SelectedJob = previousJob;
        game.AutoAdvanceDialog = false;
        Engine.TimeScale = 1;
    }

    private async Task SceneChecks()
    {
        var game = GetNode<GameManager>("/root/Game");
        // ゲーム内時間を速める（タイトルカードやボス登場の待ちを実時間で縮める。挙動は変えない）。
        Engine.TimeScale = 8;
        typeof(QaPilot).GetProperty("GodActive")!.SetValue(null, true);   // 判定中に自機が落ちない

        // ⑤ FINAL の道中（残響の三波）で曲が鳴るか。
        //    ルナティックで入れば F1 導入の会話を踏まないので、タイトルカードの後すぐ道中（_step=2）に入る。
        {
            var (root, stage) = await OpenStage("res://MinaBattle.tscn", game,
                GameManager.Diff.Lunatic, GameManager.StageEntry.Start, skipMinaBanner: true);
            bool reached = await Until(() => ReadInt(stage, "_step") >= 2, 4000);
            Check("FINAL: 道中（_step=2）まで進む", reached, $"_step={ReadInt(stage, "_step")}");
            await Frames(20);
            var now = Now();
            Check("FINAL 道中に曲が鳴っている", now != null, "無音のまま（2026-09-27 に 70 秒へ延びた区間）");
            CheckSame("FINAL 道中の曲＝BgmFinalRoute", now, _audio.BgmFinalRoute);
            // 導入（_step=1）の沈黙は残っているか＝道中に入る前は無音だったことの裏取りはできないので、
            //   代わりに「道中で鳴り始めた曲が数十フレームで消えない（誰かに止められない）」を見る。
            await Frames(40);
            CheckSame("FINAL 道中の曲が途切れない", Now(), _audio.BgmFinalRoute);
            await CloseStage(root);
        }

        // ⑥ ルナティックのボステーマ：投稿の割り込みを畳んでもローパスが開き切っているか。
        //    こはる／レイ／ミナ＝BossPostSequence（PostMusicDepth）、あかり＝専用実装（AkariMusicDepth）。
        {
            var (root, stage) = await OpenStage("res://MinaBattle.tscn", game,
                GameManager.Diff.Lunatic, GameManager.StageEntry.Boss, skipMinaBanner: true);
            bool spawned = await Until(() => Now() == _audio.BgmBossMina, 4000);
            Check("FINAL: ミナ戦の曲に切り替わる", spawned, $"再生中={Describe(Now())}");
            Check("ルナティック: ミナ戦の曲が開き切っている（depth=5）", _audio.PostMusicDepth == 5,
                  $"depth={_audio.PostMusicDepth}（0 のままだと 2400Hz のローパスでこもり続ける）");
            await CloseStage(root);
        }
        {
            var (root, stage) = await OpenStage("res://MinaBattle.tscn", game,
                GameManager.Diff.Normal, GameManager.StageEntry.Boss, skipMinaBanner: true);
            await Until(() => Now() == _audio.BgmBossMina, 4000);
            Check("通常難易度: ミナ戦は札 0 枚＝こもりから始まる（depth=0）", _audio.PostMusicDepth == 0,
                  $"depth={_audio.PostMusicDepth}");
            await CloseStage(root);
        }
        {
            var (root, stage) = await OpenStage("res://Akari.tscn", game,
                GameManager.Diff.Lunatic, GameManager.StageEntry.Boss, skipMinaBanner: false);
            bool spawned = await Until(() => Now() == _audio.BgmBossAkari, 4000);
            Check("あかり: ボス曲に切り替わる", spawned, $"再生中={Describe(Now())}");
            Check("ルナティック: あかり戦の曲が開き切っている（depth=5）", _audio.AkariMusicDepth == 5,
                  $"depth={_audio.AkariMusicDepth}（0 のままだと 1800Hz のローパスでこもり続ける）");
            await CloseStage(root);
        }
        {
            var (root, stage) = await OpenStage("res://Akari.tscn", game,
                GameManager.Diff.Normal, GameManager.StageEntry.Boss, skipMinaBanner: false);
            await Until(() => Now() == _audio.BgmBossAkari, 4000);
            Check("通常難易度: あかり戦は札 0 枚＝こもりから始まる（depth=0）", _audio.AkariMusicDepth == 0,
                  $"depth={_audio.AkariMusicDepth}");
            await CloseStage(root);
        }

        foreach (var id in new[] { "koharu", "rei" })
        {
            foreach (var difficulty in new[] { GameManager.Diff.Normal, GameManager.Diff.Lunatic })
            {
                var expected = id == "koharu" ? _audio.BgmBossKoharu : _audio.BgmBossRei;
                string scene = id == "koharu" ? "res://Koharu.tscn" : "res://Rei.tscn";
                var (root, stage) = await OpenStage(scene, game, difficulty, GameManager.StageEntry.Boss, false);
                bool spawned = await Until(() => Now() == expected, 4000);
                var intro = GetTree().GetFirstNodeInGroup("boss_intro");
                var hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
                Check($"{id}/{difficulty}: ボス曲に切り替わる", spawned,
                    $"再生中={Describe(Now())} job={game.SelectedJob} step={ReadInt(stage, "_step")}"
                    + (intro == null ? " intro=none" : $" line={Field(intro, "_line")} talking={Field(intro, "_talking")}"
                        + $" leaving={Field(intro, "_leaving")} lineTime={Field(intro, "_lineTime")} postTime={Field(intro, "_postTime")}"
                        + $" blocked={Pad.UiBlocked(intro)} page={hud?.DialogPageIndex} revealed={hud?.DialogRevealed}"));
                var boss = Field(stage, "_boss") as Node;
                var posts = boss == null ? null : Field(boss, "_posts") as BossPostSequence;
                Check($"{id}/{difficulty}: 投稿の音楽制御がある", posts != null, "BossPostSequence が無い");
                if (posts != null)
                {
                    _audio.Music(_audio.StoryBgm(id, false), 0);
                    posts.ResumeMusic();
                    CheckSame($"{id}/{difficulty}: 回想後の曲の復帰", Now(), expected);
                }
                await CloseStage(root);
            }
        }

        await MeasureFinalCutscene();
        await EndingMusicChecks();

        // ⑧ 設定画面は**タイトルの曲を継続する**（薄いサブ画面は親の曲を引き継ぐ設計）。
        //    タイトル → 設定 で曲が入れ替わらないこと。
        {
            _audio.StopMusic(0);
            await Frames(2);
            var title = GD.Load<PackedScene>("res://TitleMenu.tscn").Instantiate();
            GetTree().Root.AddChild(title);
            GetTree().CurrentScene = title;
            await Frames(10);
            CheckSame("タイトルの曲＝BgmTitle", Now(), _audio.BgmTitle);
            GetTree().CurrentScene = null;
            title.QueueFree();
            await Frames(6);
            var settings = GD.Load<PackedScene>("res://Settings.tscn").Instantiate();
            GetTree().Root.AddChild(settings);
            GetTree().CurrentScene = settings;
            await Frames(10);
            CheckSame("設定画面はタイトルの曲を継続する", Now(), _audio.BgmTitle);
            GetTree().CurrentScene = null;
            settings.QueueFree();
            await Frames(24);
        }

        Engine.TimeScale = 1;
    }

    private async Task MeasureFinalCutscene()
    {
        var game = GetNode<GameManager>("/root/Game");
        _audio.StopMusic(0);
        await Frames(2);
        double savedScale = Engine.TimeScale;
        Engine.TimeScale = 12;
        game.AutoAdvanceDialog = true;
        game.MsgCharsPerSec = 48;

        var final = GD.Load<PackedScene>("res://Final.tscn").Instantiate();
        GetTree().Root.AddChild(final);
        GetTree().CurrentScene = final;
        await Frames(4);
        CheckSame("F4 の頭の曲＝BgmFinalCutscene", Now(), _audio.BgmFinalCutscene);
        Check("F4 の曲が実音源", Now() is AudioStreamOggVorbis or AudioStreamMP3,
              $"再生中={Describe(Now())}");

        double toSilence = await RunUntil(() => ReadBool(final, "_cueSilenceDone"), 4000);
        Check("F4: 選択前の無音キューに到達する", toSilence >= 0, "到達しない");
        Check("F4: 選択前に無音になる", Now() == null, $"再生中={Describe(Now())}");
        double toChoice = await RunUntil(() => Field(final, "_choice") != null, 4000);
        Check("F4: 下書き選択まで進む", toChoice >= 0, "選択が出ない");
        await Frames(30);
        Check("F4: 選択待ちの間は無音を保つ", Now() == null, $"再生中={Describe(Now())}");

        // AUTO は選択肢を確定しないため、通常の決定入力で解決曲への遷移を検証する。
        Input.ActionPress("ui_accept");
        await Frames(2);
        Input.ActionRelease("ui_accept");
        double choiceOpen = await RunUntil(() => Field(final, "_choice") == null, 600);
        Check("F4: 決定入力で選択を確定する", choiceOpen >= 0, "確定しない");
        double toResolve = await RunUntil(() => ReadBool(final, "_cueResolveDone"), 4000);
        Check("F4: 解決曲のキューに到達する", toResolve >= 0, "到達しない");
        await Frames(4);
        CheckSame("F4: 無音のあと挿入曲（BgmFinalResolve）が立つ", Now(), _audio.BgmFinalResolve);

        GetTree().CurrentScene = null;
        final.QueueFree();
        Engine.TimeScale = savedScale;
        await Frames(24);
        game.AutoAdvanceDialog = false;
        _audio.StopMusic(0);
        await Frames(4);
    }

    private async Task EndingMusicChecks()
    {
        var game = GetNode<GameManager>("/root/Game");
        game.AutoAdvanceDialog = false;
        _audio.Music(_audio.BgmFinalResolve, 0);
        await Frames(12);
        var before = ActiveMusicPlayer();
        double position = before.GetPlaybackPosition();
        var epilogue = GD.Load<PackedScene>("res://Epilogue.tscn").Instantiate<Epilogue>();
        GetTree().Root.AddChild(epilogue);
        GetTree().CurrentScene = epilogue;
        await Frames(4);
        CheckSame("帰還後も回復の主題を継続する", Now(), _audio.BgmFinalResolve);
        Check("帰還時に曲を頭へ巻き戻さない", ActiveMusicPlayer() == before
              && before.GetPlaybackPosition() + 0.02 >= position, "再生プレイヤーまたは再生位置が戻った");
        Check("帰還後の会話は読む長さによらずループできる",
              _audio.BgmFinalResolve is AudioStreamOggVorbis { Loop: true }, "ループしない");

        typeof(Epilogue).GetMethod("StartFilm", Priv)!.Invoke(epilogue, null);
        await Frames(4);
        CheckSame("エンディング映像で専用曲を立ち上げる", Now(), _audio.BgmEndingFilm);
        var filmPlayer = ActiveMusicPlayer();
        var film = (EndingFilm)Field(epilogue, "_film")!;
        var statics = BindingFlags.Static | BindingFlags.NonPublic;
        float lineHeight = (float)typeof(Epilogue).GetField("RollLineH", statics)!.GetValue(null)!;
        float rollSpeed = (float)typeof(Epilogue).GetField("RollSpeed", statics)!.GetValue(null)!;
        double rollDuration = (216 + ((string[])Field(epilogue, "_roll")!).Length * lineHeight + 24) / rollSpeed;
        double sequenceDuration = EndingFilm.Duration + rollDuration;
        GD.Print($"[bgmqa] 映像＋ロール={sequenceDuration:0.00}s / 曲={_audio.BgmEndingFilm.GetLength():0.00}s");
        Check("映像とスタッフロールを曲の長さが覆う", _audio.BgmEndingFilm.GetLength() >= sequenceDuration,
              "曲がロール途中で終わる");
        GetTree().Paused = true;
        await Frames(6);
        Check("ポーズや会話ログでは映像と一緒に曲を止める", filmPlayer.StreamPaused,
              "映像を止めても曲だけ進んでしまう");
        GetTree().Paused = false;
        await Frames(6);
        Check("ポーズを閉じると同じ演奏を再開する", !filmPlayer.StreamPaused && filmPlayer.Playing
              && ActiveMusicPlayer() == filmPlayer, "再生が再開しない、または再生先が変わった");
        typeof(EndingFilm).GetMethod("Complete", Priv)!.Invoke(film, null);
        await Frames(4);
        Check("映像終了後にスタッフロールへ進む", ReadInt(epilogue, "_phase") == 2, "PhRoll ではない");
        CheckSame("スタッフロールで同じ演奏を継続する", Now(), _audio.BgmEndingFilm);
        Check("スタッフロールで曲を再スタートしない", ActiveMusicPlayer() == filmPlayer, "再生先が変わった");

        typeof(Epilogue).GetField("_t", Priv)!.SetValue(epilogue, rollDuration + 0.1);
        await Frames(4);
        Check("ロール終了後に最後の会話へ進む", ReadInt(epilogue, "_phase") == 3, "PhEnd ではない");
        CheckSame("最後の会話で回復の主題が戻る", Now(), _audio.BgmFinalResolve);
        Check("一度再生の終了通知を解除する", Field(_audio, "_oncePlayer") == null, "接続が残った");
        filmPlayer.EmitSignal(AudioStreamPlayer.SignalName.Finished);
        CheckSame("前の曲の終了通知で最後の会話を無音にしない", Now(), _audio.BgmFinalResolve);
        await CloseStage(epilogue);
    }

    private AudioStreamPlayer ActiveMusicPlayer()
        => (AudioStreamPlayer)Field(_audio, (bool)Field(_audio, "_useA")! ? "_musicA" : "_musicB")!;

    private static object? Field(Node node, string name)
        => node.GetType().GetField(name, Priv)?.GetValue(node);

    private static bool ReadBool(Node node, string name) => Field(node, name) is true;

    // 条件が立つまで回し、**ゲーム内秒**を返す（立たなければ -1）。
    private async Task<double> RunUntil(Func<bool> cond, int maxFrames)
    {
        double t = 0;
        for (int i = 0; i < maxFrames; i++)
        {
            if (cond()) return t;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += GetProcessDeltaTime();
        }
        return cond() ? t : -1;
    }

    // ───────── 足回り ─────────
    private async Task<(Node root, Node stage)> OpenStage(string path, GameManager game,
        GameManager.Diff diff, GameManager.StageEntry entry, bool skipMinaBanner)
    {
        _audio.StopMusic(0);
        await Frames(2);
        game.Difficulty = diff;
        game.SelectedEntry = entry;
        var root = GD.Load<PackedScene>(path).Instantiate();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        var stage = FindStage(root);
        Check($"{path}: ステージノードが取れた", stage != null, "見つからない");
        // FINAL の 5.2 秒タイトルカードは _Process ごと進行を止める。演出の確認はここの目的ではないので、
        //   最初の _Process が走る前に「もう出した」ことにして飛ばす（曲の配線は一切変えない）。
        if (skipMinaBanner && stage is StageMina)
        {
            stage.GetType().GetField("_startBannerShown", Priv)?.SetValue(stage, true);
            stage.GetType().GetField("_titleThump", Priv)?.SetValue(stage, true);
        }
        return (root, stage!);
    }

    private async Task CloseStage(Node root)
    {
        // ステージが撒いた弾・敵を先に掃いてから畳む（残したまま抜けると終了時に解放漏れが出る）。
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        if (IsInstanceValid(root)) { GetTree().CurrentScene = null; root.QueueFree(); }
        await Frames(12);
        _audio.StopMusic(0);
        await Frames(4);
    }

    private static Node? FindStage(Node root)
    {
        foreach (var child in root.GetChildren())
            if (child is StageMina or StageAkari or StageKoharu or StageRei) return child;
        return null;
    }

    private AudioStream? Now() => CurrentMusicField.GetValue(_audio) as AudioStream;

    private async Task MusicFade()
    {
        if (Field(_audio, "_musicFadeTween") is Tween tween && tween.IsRunning())
            await ToSignal(tween, Tween.SignalName.Finished);
        await Frames(2);
    }

    private static string Describe(AudioStream? s)
        => s == null ? "(無音)" : string.IsNullOrEmpty(s.ResourcePath) ? $"合成:{s.GetType().Name}" : s.ResourcePath;

    private static int ReadInt(Node node, string field)
        => node.GetType().GetField(field, Priv)?.GetValue(node) is int i ? i : -1;

    private void CheckSame(string label, AudioStream? actual, AudioStream? expected)
        => Check(label, ReferenceEquals(actual, expected),
                 $"期待={Describe(expected)} 実際={Describe(actual)}");

    private void Check(string label, bool ok, string detail)
    {
        if (!ok) _fail++;
        GD.Print($"[bgmqa] {(ok ? "OK  " : "NG  ")}{label}{(ok ? "" : $" — {detail}")}");
    }

    private async Task<bool> Until(Func<bool> cond, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (cond()) return true;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        return cond();
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
