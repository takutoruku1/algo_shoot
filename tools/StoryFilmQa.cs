using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

public partial class StoryFilmQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _out = "";
    private static T Read<T>(object obj, string field, Type? type = null)
        => (T)(type ?? obj.GetType()).GetField(field, Private)!.GetValue(obj)!;
    private static void Write(object obj, string field, object value, Type? type = null)
        => (type ?? obj.GetType()).GetField(field, Private)!.SetValue(obj, value);
    private static void Call(object obj, string method, Type? type = null)
        => (type ?? obj.GetType()).GetMethod(method, Private)!.Invoke(obj, null);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print($"[StoryQA] PASS {message}");
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        bool koharu = Array.IndexOf(OS.GetCmdlineUserArgs(), "--koharu") >= 0;
        bool rei = Array.IndexOf(OS.GetCmdlineUserArgs(), "--rei") >= 0;
        bool burst = (koharu || rei) && Array.IndexOf(OS.GetCmdlineUserArgs(), "--burst") >= 0;
        string stageName = rei ? "Rei" : koharu ? "Koharu" : "Akari";
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated user data");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            _out = ProjectSettings.GlobalizePath($"res://build/qa_story/{stageName.ToLowerInvariant()}/shots");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            var game = GetNode<GameManager>("/root/Game");
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--matrix") >= 0)
            {
                await CheckMatrix(stageName, game);
                return;
            }
            var gameMode = game.ProcessMode;
            game.SelectedEntry = GameManager.StageEntry.Boss;
            game.MsgCharsPerSec = 300;
            game.AutoAdvanceDialog = false;
            var root = GD.Load<PackedScene>($"res://{stageName}.tscn").Instantiate<Node2D>();
            await Frames(1);
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            var hud = root.GetNode<Hud>("Hud");
            var world = root.GetNode<Node2D>("World");
            var stage = root.GetNode<Node>($"Stage{stageName}");
            var player = world.GetNode<Player>("Player");
            void PlayFilm(bool aftermath, Action completed)
            {
                if (rei) ReiStoryFilm.Play(hud, world, aftermath, completed);
                else if (koharu) KoharuStoryFilm.Play(hud, world, aftermath, completed);
                else AkariStoryFilm.Play(hud, world, aftermath, completed);
            }
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--devices") >= 0)
            {
                Check(koharu || rei, "device artwork test selects an updated atlas");
                game.AutoSaveEnabled = false;
                root.SetProcess(false);
                stage.SetProcess(false);
                player.SetPhysicsProcess(false);
                world.ProcessMode = ProcessModeEnum.Inherit;
                _out = ProjectSettings.GlobalizePath($"res://build/qa_story/devices/{stageName}");
                DirAccess.MakeDirRecursiveAbsolute(_out);
                foreach (bool aftermath in new[] { false, true })
                {
                    bool complete = false;
                    PlayFilm(aftermath, () => complete = true);
                    await Frames(90);
                    var artFilm = (StoryFilm)GetTree().GetFirstNodeInGroup("storyfilm");
                    Check(Read<string>(artFilm, "_atlasPath").EndsWith("_story_atlas_v2.png"), "runtime uses the repaired atlas");
                    Check(Read<int>(artFilm, "_atlasRows") == 4, "original eight-shot layout retained");
                    for (int shot = aftermath ? 5 : 0; shot <= (aftermath ? 7 : 4); shot++)
                    {
                        await AdvanceUntil(() => Read<int>(artFilm, "_shot") == shot);
                        await Frames(65);
                        foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540) })
                        {
                            DisplayServer.WindowSetSize(size);
                            await Frames(5);
                            using var frame = await Shot($"shot_{shot}_{size.X}", grayscale: !aftermath);
                        }
                        Check(world.ProcessMode == ProcessModeEnum.Disabled && Hud.BubblePaused,
                            "combat stays paused while the corrected illustration is displayed");
                    }
                    await AdvanceUntil(() => complete);
                    Check(!hud.CinematicMode && world.ProcessMode == ProcessModeEnum.Inherit, "film completes and restores combat");
                    await Frames(30);
                }
                root.QueueFree();
                Audio.Instance?.StopMusic(0);
                foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                    if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
                await Task.Delay(250);
                await Frames(5);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Frames(5);
                GD.Print($"[StoryQA] {stageName} DEVICES ALL PASS");
                GetTree().Quit();
                return;
            }
            await Frames(15);
            await AdvanceUntil(() => Read<int>(stage, "_step") == (rei ? 12 : 13));
            var boss = world.GetNode<Enemy>($"Boss{stageName}");
            // 撃破後演出（BossPostSequence＝下書きの札を自機が撃って割る）は、自機を動かさないこの走行では
            //   札が割れず、ボスの会話送りが _posts.Active で止まる（こはる／レイ）。フィルムの検証には
            //   関係しないので、しきい値を空にして発火させない（あかりは別実装＝AkariPost で、止まらない）。
            if (boss.GetNodeOrNull<BossPostSequence>("PostSequence") is { } postSeq)
                Write(postSeq, "_thresholds", Array.Empty<float>());
            int maxHp = Read<int>(boss, "_maxHp", typeof(Enemy));
            if (!(rei && burst))
            {
                Write(boss, "_hp", (int)(maxHp * 0.77f), typeof(Enemy));
                Call(boss, "OnHpChanged");
            }
            Write(boss, "_hp", (int)(maxHp * (burst ? rei ? 0.18f : 0.24f : koharu || rei ? 0.49f : 0.51f)), typeof(Enemy));
            Call(boss, "OnHpChanged");
            Write(game, "_comboTimer", 5.0);
            await Frames(10);
            var film = GetTree().GetFirstNodeInGroup("storyfilm") as StoryFilm;
            Check(film != null && hud.CinematicMode, "HP threshold starts flashback");
            Check(world.ProcessMode == ProcessModeEnum.Disabled && Hud.BubblePaused, "combat is suspended");
            if (rei)
            {
                Check(!Read<bool>(stage, "_midStoryShown"), "flashback takes priority over Mina choice");
                Check(!Read<bool>(boss, "_form2", typeof(Enemy)) && !Read<bool>(boss, "_relayFired")
                      && !Read<bool>(boss, "_accelerated"), "form, relay and music changes wait for memory");
            }
            var position = player.GlobalPosition;
            int lives = player.Lives;
            int bombs = game.Bombs;
            int bombCount = player.BombCount;
            float hp = boss.HpRatio;
            double phaseT = Read<double>(boss, "_phaseT", typeof(Enemy));
            double elapsed = Read<double>(stage, "_stageElapsed");
            double comboTime = Read<double>(game, "_comboTimer");
            KeyEvent(Key.Right, true);
            KeyEvent(Key.X, true);
            await Frames(90);
            KeyEvent(Key.Right, false);
            KeyEvent(Key.X, false);
            Check(player.GlobalPosition == position && player.Lives == lives && boss.HpRatio == hp
                  && game.Bombs == bombs && player.BombCount == bombCount, "movement, damage and bombs stay frozen");
            Check(Read<double>(boss, "_phaseT", typeof(Enemy)) == phaseT && Read<double>(stage, "_stageElapsed") == elapsed, "boss phase and stage clocks stay frozen");
            Check(game.ProcessMode == ProcessModeEnum.Disabled && Read<double>(game, "_comboTimer") == comboTime, "combo timeout stays frozen during memory");
            var first = await Shot("memory_start", grayscale: true);
            await Frames(90);
            var moving = await Shot("memory_motion", grayscale: true);
            int changed = 0;
            for (int y = 100; y < 440; y += 4)
                for (int x = 60; x < Math.Min(900, first.GetWidth()); x += 4)
                    if (first.GetPixel(x, y) != moving.GetPixel(x, y)) changed++;
            Check(changed > 100, "background camera motion renders");
            DisplayServer.WindowSetSize(new Vector2I(960, 540));
            await Frames(15);
            await Shot("memory_small", grayscale: true);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(15);

            var backlog = GetNode<Backlog>("/root/Backlog");
            backlog.Open();
            double motion = Read<double>(film!, "_shotT");
            await Frames(30);
            Check(GetTree().Paused && Read<double>(film!, "_shotT") == motion, "backlog pauses the film");
            Call(backlog, "Close");
            await Frames(30);
            Check(!GetTree().Paused, "backlog restores normal playback");
            await AdvanceUntil(() => Read<int>(film!, "_shot") == 1);
            await Frames(60);
            await Shot("memory_pressure", grayscale: true);
            if (koharu || rei)
                for (int shot = 2; shot <= 4; shot++)
                {
                    await AdvanceUntil(() => Read<int>(film!, "_shot") == shot);
                    await Frames(60);
                    await Shot($"memory_scene_{shot}", grayscale: true);
                }
            await AdvanceUntil(() => !IsInstanceValid(film));
            Check(!hud.CinematicMode && (rei || !Hud.BubblePaused) && world.ProcessMode == ProcessModeEnum.Inherit
                  && game.ProcessMode == gameMode, "flashback restores world and HUD");
            if (koharu)
            {
                // 2026-09-17 ユーザー指示：配膳（うちわ）は削除（BossKoharu.MealEnabled=false）。
                //   閾値を跨いだ一度だけ _mealFired が立ち、_mealPhase には入らず素通りする＝
                //   保留（holds）が残らず、第二形態・スペル切替がその場で通ること自体が回帰テストになる。
                Check(Read<bool>(boss, "_mealFired") && Read<int>(boss, "_mealPhase") == 0, "archive mechanic stays disabled after memory");
                if (burst)
                {
                    Check(Read<int>(boss, "_gotoPhase") > 0, "large damage still reaches crossfire without archives");
                    await WaitUntil(() => Read<int>(boss, "_gotoPhase") == 0, 1600);
                }
                Check(Read<bool>(boss, "_form2", typeof(Enemy)), "second form is no longer held by the archive mechanic");
            }
            else if (rei)
            {
                Check(Read<bool>(boss, "_form2", typeof(Enemy)), "avatar cracks only after memory");
                if (burst)
                {
                    Check(Read<int>(boss, "_beatsFired") == 3 && Read<bool>(boss, "_relayFired")
                          && Read<bool>(boss, "_accelerated"), "large damage resumes all crossed thresholds");
                    Check(((BossRei)boss).AoeGateActive, "relay telegraph starts after memory");
                    await WaitUntil(() => !((BossRei)boss).AoeGateActive, 1600);
                }
                else
                {
                    await AdvanceUntil(() => Read<bool>(stage, "_midStoryShown") && Read<int>(stage, "_step") == 12);
                    Check(!Hud.BubblePaused && !hud.SuppressCallouts, "Mina choice completes after memory and restores battle");
                }
            }
            else Check(Read<bool>(boss, "_form2", typeof(Enemy)) && Read<bool>(boss, "_corridorFired"), "second form and corridor begin after the memory");
            Call(boss, "OnHpChanged");
            await Frames(15);
            Check(GetTree().GetNodesInGroup("storyfilm").Count == 0, "memory is one-shot");
            await Shot("battle_resumed", grayscale: false);

            Write(boss, "_hp", 0, typeof(Enemy));
            Call(boss, "Redeem", typeof(Enemy));
            if (rei)
            {
                await AdvanceUntil(() => boss.ShellPeelBusy);
                int peelLine = Read<int>(boss, "_line");
                KeyEvent(Key.Z, true);
                await Frames(30);
                KeyEvent(Key.Z, false);
                Check(boss.ShellPeelBusy && Read<int>(boss, "_line") == peelLine, "advance cannot skip the avatar peel");
                await Shot("avatar_peel", grayscale: false);
            }
            await AdvanceUntil(() => hud.CinematicMode);
            film = GetTree().GetFirstNodeInGroup("storyfilm") as StoryFilm;
            Check(film != null && Read<bool>(film, "_aftermath"), "clear dialogue starts next-day aftermath");
            await Frames(90);
            await Shot("aftermath_start", grayscale: false);
            await AdvanceUntil(() => Read<int>(film!, "_line") == 7);
            await Frames(60);
            await Shot("aftermath_action", grayscale: false);
            await AdvanceUntil(() => Read<int>(film!, "_shot") == (koharu || rei ? 7 : 5));
            await Frames(60);
            await Shot("aftermath_changed", grayscale: false);
            await AdvanceUntil(() => !IsInstanceValid(film));
            stage.SetProcess(false);
            Check(!hud.CinematicMode && Read<int>(stage, "_clearPhase") == 2, "aftermath returns to clear dialogue");

            bool ended = false;
            PlayFilm(false, () => ended = true);
            KeyEvent(Key.Ctrl, true);
            await WaitUntil(() => ended, 1600);
            KeyEvent(Key.Ctrl, false);
            Check(ended, "read-only fast-forward completes memory");
            game.AutoAdvanceDialog = true;
            ended = false;
            PlayFilm(true, () => ended = true);
            await WaitUntil(() => ended, 2600);
            game.AutoAdvanceDialog = false;
            Check(ended, "auto mode completes aftermath");

            PlayFilm(false, () => throw new Exception("aborted callback fired"));
            await Frames(5);
            hud.GetNode($"{stageName}StoryFilm").QueueFree();
            await Frames(5);
            Check(!hud.CinematicMode && !Hud.BubblePaused && world.ProcessMode == ProcessModeEnum.Inherit
                  && game.ProcessMode == gameMode, "aborted film releases pause state");
            Write(stage, "_stepStarted", false);
            stage.SetProcess(true);
            await AdvanceUntil(() => GetTree().CurrentScene != root);
            Check(GetTree().CurrentScene.SceneFilePath is "res://ShopTutorial.tscn" or "res://Hub.tscn", "normal clear transition completes");
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print($"[StoryQA] {stageName} ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[StoryQA] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    // ステージ × 操作キャラ（ミナ／あかり／こはる／レイ）で、戦闘中の回想（memory）と撃破後のアフター
    //   の中身を確かめる。2026-09-23 後半の仕様：
    //   ・ミナ潜行（Job.Tank）＝従来どおり**その面のボスのフィルム**（一枚絵）。
    //   ・他ジョブ潜行＝フィルムを**一本も起こさず**、CharacterStory.Memory / Aftermath（潜行キャラ×この面のボスの
    //     9通り）を会話枠で流す（ユーザー指示「吹き出しのやり取りだけにして」）。本文は Hud.Backlog で照合する。
    //   旧仕様（この下のコメント）の回帰も兼ねる：
    //   （aftermath）に流れるフィルムが**その面のボスのもの**であることを確かめ、表にして出す
    //   （2026-09-23 ユーザー報告「こはるの話であかりの回想／アフターが入ってる」「レイのときも同様」の回帰。
    //   以前は他ジョブ潜行で CharacterStoryFilm＝操作キャラ×章のフィルムを流していた）。
    //   起動: qa_story_film.tscn -- --matrix [--koharu|--rei]（1プロセス＝1面×4キャラ。既定は STAGE1）。
    //   結果は [StoryQA] MATRIX 行（面／操作キャラ／memory／aftermath の FilmId）。こはる面×あかりだけ
    //   ユーザー報告の再現条件なので memory／aftermath の画面を PNG に残す（build/qa_story/matrix/koharu/akari/）。
    //   Shot() の CheckFrame が「描かれている絵＝その面のアトラスの該当コマ」まで画素で照合する。
    private async Task CheckMatrix(string stageName, GameManager game)
    {
        game.AutoSaveEnabled = false;
        game.MsgCharsPerSec = 300;
        game.AutoAdvanceDialog = false;
        string stageId = stageName.ToLowerInvariant();
        bool rei = stageName == "Rei";
        int stageNo = stageName == "Akari" ? 1 : stageName == "Koharu" ? 2 : 3;
        var dives = Read<System.Collections.Generic.Dictionary<string, int>>(game, "_charDives");
        var filmId = typeof(StoryFilm).GetProperty("FilmId", Private)!;
        var rows = new System.Collections.Generic.List<string>();
        foreach (var job in new[] { Job.Tank, Job.Melee, Job.Heal, Job.Magic })
        {
            string id = Jobs.Get(job).CharacterId;
            bool report = stageId == "koharu" && job == Job.Melee;   // ユーザー報告の再現条件＝画面も残す
            game.SelectedJob = job;
            dives[id] = 1;
            game.SelectedEntry = GameManager.StageEntry.Boss;
            _out = ProjectSettings.GlobalizePath($"res://build/qa_story/matrix/{stageId}/{id}");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            var root = GD.Load<PackedScene>($"res://{stageName}.tscn").Instantiate<Node2D>();
            await Frames(1);
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            var hud = root.GetNode<Hud>("Hud");
            var world = root.GetNode<Node2D>("World");
            var stage = root.GetNode<Node>($"Stage{stageName}");
            var player = world.GetNode<Player>("Player");
            player.SetPhysicsProcess(false);
            Hud.ClearBacklog();
            await AdvanceUntil(() => Read<int>(stage, "_step") == (rei ? 12 : 13));
            var boss = world.GetNode<Enemy>($"Boss{stageName}");
            // 撃破後演出（BossPostSequence＝下書きの札を自機が撃って割る）は、自機を動かさないこの走行では
            //   札が割れず、ボスの会話送りが _posts.Active で止まる（こはる／レイ）。フィルムの検証には
            //   関係しないので、しきい値を空にして発火させない（あかりは別実装＝AkariPost で、止まらない）。
            if (boss.GetNodeOrNull<BossPostSequence>("PostSequence") is { } posts)
                Write(posts, "_thresholds", Array.Empty<float>());
            int maxHp = Read<int>(boss, "_maxHp", typeof(Enemy));
            Write(boss, "_hp", (int)(maxHp * 0.77f), typeof(Enemy));
            Call(boss, "OnHpChanged");
            Write(boss, "_hp", (int)(maxHp * (stageName == "Akari" ? 0.51f : 0.49f)), typeof(Enemy));
            Call(boss, "OnHpChanged");
            string memory;
            if (job == Job.Tank)
            {
                await WaitUntil(() => hud.CinematicMode, 120);
                var film = (StoryFilm)GetTree().GetFirstNodeInGroup("storyfilm");
                memory = (string)filmId.GetValue(film)!;
                Check(memory == $"{stageId}_memory" && film.GetType().Name == $"{stageName}StoryFilm",
                    $"STAGE{stageNo} as {id}: memory film is the stage boss's ({memory})");
                Check(world.ProcessMode == ProcessModeEnum.Disabled && game.ProcessMode == ProcessModeEnum.Disabled,
                    "world and game timers disabled during memory");
                if (report) { await Frames(90); using var shot = await Shot("memory", grayscale: true); }
                await AdvanceUntil(() => !IsInstanceValid(film));
                Check(!hud.CinematicMode && world.ProcessMode == ProcessModeEnum.Inherit
                      && game.ProcessMode != ProcessModeEnum.Disabled, "memory restores combat");
            }
            else
            {
                // 他ジョブ潜行：一枚絵は起こさず、吹き出し（Hud.HoldBubble）だけで流す。
                var lines = CharacterStory.Memory(job, stageId);
                memory = $"talk:{id}×{stageId}({lines.Length})";
                await WaitUntil(() => Hud.BubblePaused, 120);
                Check(GetTree().GetNodesInGroup("storyfilm").Count == 0,
                    $"STAGE{stageNo} as {id}: memory raises no film (bubble talk only)");
                // 最初の1行目が出ていることだけ確かめてから送る（本文全体の照合は送り切ったあと）。
                if (report)
                {
                    await Frames(30);
                    using (var shot = await Shot("memory", grayscale: false)) { }
                    // M2 の決めの二行（こはる「終わるまで。ぜんぶ終わるまで。」→ あかり「……あー。……その返事、
                    //   あたし、八年してた。上司に。」）を画で残す（ユーザー要求のスクショ）。
                    for (int want = 2; want <= 3; want++)
                    {
                        await AdvanceUntil(() => System.Linq.Enumerable.Any(Hud.Backlog, e => e.Text == lines[want].text));
                        await Frames(30);
                        using var beat = await Shot($"memory_line{want}", grayscale: false);
                    }
                }
                await AdvanceUntil(() => !Hud.BubblePaused);
                foreach (var line in lines)
                    Check(System.Linq.Enumerable.Any(Hud.Backlog, e => e.Text == line.text),
                        $"{id}×{stageId}: memory line in backlog ({line.text[..Math.Min(12, line.text.Length)]})");
                Check(world.ProcessMode == ProcessModeEnum.Inherit && game.ProcessMode != ProcessModeEnum.Disabled,
                    "memory talk restores combat");
            }
            // ミナ本編のレイ面だけ、回想明けに S3-7 の下書き選択（ミナ）が割り込む＝送り切ってから撃破へ。
            if (rei && job == Job.Tank)
                await AdvanceUntil(() => Read<bool>(stage, "_midStoryShown") && Read<int>(stage, "_step") == 12);
            Call(boss, "OnHpChanged");
            await Frames(12);
            Check(GetTree().GetNodesInGroup("storyfilm").Count == 0, "memory only starts once");

            Write(boss, "_hp", 0, typeof(Enemy));
            Call(boss, "Redeem", typeof(Enemy));
            Hud.ClearBacklog();
            string aftermath;
            if (job == Job.Tank)
            {
                await AdvanceUntil(() => hud.CinematicMode);
                var film = (StoryFilm)GetTree().GetFirstNodeInGroup("storyfilm");
                aftermath = (string)filmId.GetValue(film)!;
                Check(Read<bool>(film, "_aftermath") && aftermath == $"{stageId}_aftermath"
                      && film.GetType().Name == $"{stageName}StoryFilm",
                    $"STAGE{stageNo} as {id}: aftermath film is the stage boss's ({aftermath})");
                if (report) { await Frames(90); using var shot = await Shot("aftermath", grayscale: false); }
                await AdvanceUntil(() => !IsInstanceValid(film));
                Check(!hud.CinematicMode, "aftermath restores HUD");
            }
            else
            {
                var lines = CharacterStory.Aftermath(job, stageId);
                aftermath = $"talk:{id}×{stageId}({lines.Length})";
                Check(GetTree().GetNodesInGroup("storyfilm").Count == 0,
                    $"STAGE{stageNo} as {id}: aftermath raises no film (bubble talk only)");
            }
            // 明けの会話 → 遷移。この間に別のフィルム（旧・操作キャラの playable）が立たないことも見る。
            int extraFilms = 0;
            for (int i = 0; i < 500 && GetTree().CurrentScene == root; i++)
            {
                if (GetTree().GetNodesInGroup("storyfilm").Count > 0) extraFilms++;
                KeyEvent(Key.Z, true);
                await Frames(16);
                KeyEvent(Key.Z, false);
                await Frames(2);
            }
            Check(GetTree().CurrentScene != root, "clear transition leaves the stage");
            Check(extraFilms == 0, "no second (playable) film between the aftermath and the transition");
            Check(GetTree().CurrentScene.SceneFilePath is "res://Hub.tscn" or "res://ShopTutorial.tscn"
                  && game.IsStageCleared(stageId), "clear transition completes after the aftermath");
            if (job != Job.Tank)
            {
                // アフター（吹き出し）→ 帰還ビートの順で両方流れていること。
                foreach (var line in CharacterStory.Aftermath(job, stageId))
                    Check(System.Linq.Enumerable.Any(Hud.Backlog, entry => entry.Text == line.text),
                        $"{id}×{stageId}: aftermath line in backlog ({line.text[..Math.Min(12, line.text.Length)]})");
                foreach (var line in CharacterStory.Lines(job, 1, CharacterStory.Beat.Return))
                    Check(System.Linq.Enumerable.Any(Hud.Backlog, entry => entry.Text == line.text),
                        $"{id}: return beat follows the aftermath talk");
                // 写真アプリの6枚（「もう一度」／「帰還」）の解禁キーがこの走行で立っていること。
                Check(FilmSkip.Seen(game, CharacterStory.SeenKey(job, aftermath: false))
                      && FilmSkip.Seen(game, CharacterStory.SeenKey(job, aftermath: true)),
                    $"{id}: photo app keys unlocked by the memory/aftermath talk");
            }
            rows.Add($"[StoryQA] MATRIX | STAGE{stageNo} {stageId} | {Jobs.Get(job).CharacterName}({id}) | memory={memory} | aftermath={aftermath} |");
            GetTree().CurrentScene.QueueFree();
            await Frames(8);
        }
        foreach (var row in rows) GD.Print(row);
        Audio.Instance?.StopMusic(0);
        foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
            if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
        await Task.Delay(250);
        await Frames(5);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GD.Print($"[StoryQA] {stageName} MATRIX ALL PASS");
        GetTree().Quit();
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task WaitUntil(Func<bool> condition, int limit)
    {
        for (int i = 0; i < limit && !condition(); i++) await Frames(1);
        if (!condition()) throw new Exception("Timed out waiting for playback");
    }

    private async Task AdvanceUntil(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            KeyEvent(Key.Z, true);
            await Frames(16);
            KeyEvent(Key.Z, false);
            await Frames(2);
        }
        if (!condition()) throw new Exception("Timed out advancing dialogue");
    }

    private static void KeyEvent(Key key, bool pressed)
        => Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = pressed });

    private async Task<Image> Shot(string name, bool grayscale)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng($"{_out}/{name}.png");
        float min = 1, max = 0;
        int colored = 0;
        for (int y = image.GetHeight() / 5; y < image.GetHeight() / 2; y += 9)
            for (int x = image.GetWidth() / 8; x < image.GetWidth() * 7 / 8; x += 9)
            {
                var c = image.GetPixel(x, y);
                min = Math.Min(min, c.R);
                max = Math.Max(max, c.R);
                if (Math.Abs(c.R - c.G) + Math.Abs(c.G - c.B) > 0.025f) colored++;
            }
        Check(max - min > 0.2f, $"{name}: nonblank image");
        Check(grayscale ? colored == 0 : colored > 100, $"{name}: correct color mode");
        if (GetTree().GetFirstNodeInGroup("storyfilm") is StoryFilm film)
            CheckFrame(film, image);
        return image;
    }

    public static void CheckFrame(StoryFilm film, Image rendered)
    {
        var grade = Read<ShaderMaterial>(film, "_grade", typeof(StoryFilm));
        if (grade.GetShaderParameter("blend_amount").AsSingle() < 1f || film.Modulate.A < 1f) return;
        var texture = grade.GetShaderParameter("scene_texture").AsGodotObject() as Texture2D;
        var region = grade.GetShaderParameter("scene_region").AsVector4();
        int shot = Read<int>(film, "_shot", typeof(StoryFilm));
        var images = Read<System.Collections.Generic.Dictionary<int, string>>(film, "_shotImages", typeof(StoryFilm));
        bool fullFrame = images.TryGetValue(shot, out string? path);
        Check(texture != null && texture.ResourcePath == (fullFrame ? path : Read<string>(film, "_atlasPath", typeof(StoryFilm))),
            $"shot {shot}: intended illustration loaded");
        int rows = Read<int>(film, "_atlasRows", typeof(StoryFilm));
        var expectedRegion = fullFrame ? new Vector4(0, 0, 1, 1)
            : new Vector4((shot % 2) / 2f, (shot / 2) / (float)rows, 0.5f, 1f / rows);
        Check(region.IsEqualApprox(expectedRegion), $"shot {shot}: correct full-frame or atlas region");
        using var source = texture!.GetImage();
        float t = grade.GetShaderParameter("motion_time").AsSingle();
        bool grayscale = grade.GetShaderParameter("grayscale").AsBool();
        float error = 0;
        int samples = 0;
        for (int y = rendered.GetHeight() / 6; y < rendered.GetHeight() * 2 / 3; y += 37)
            for (int x = rendered.GetWidth() / 10; x < rendered.GetWidth() * 9 / 10; x += 41)
            {
                var uv = (new Vector2((x + 0.5f) / rendered.GetWidth(), (y + 0.5f) / rendered.GetHeight()) - Vector2.One * 0.5f)
                    * 0.94f + Vector2.One * 0.5f + new Vector2(Mathf.Sin(t * 0.045f) * 0.016f, 0.006f);
                uv = new Vector2(region.X, region.Y) + uv * new Vector2(region.Z, region.W);
                var p = uv * source.GetSize() - Vector2.One * 0.5f;
                int sx = Mathf.FloorToInt(p.X), sy = Mathf.FloorToInt(p.Y);
                var expected = source.GetPixel(sx, sy).Lerp(source.GetPixel(sx + 1, sy), p.X - sx)
                    .Lerp(source.GetPixel(sx, sy + 1).Lerp(source.GetPixel(sx + 1, sy + 1), p.X - sx), p.Y - sy);
                if (grayscale)
                {
                    float luma = expected.R * 0.2126f + expected.G * 0.7152f + expected.B * 0.0722f;
                    expected = new Color(luma, luma, luma);
                }
                var actual = rendered.GetPixel(x, y);
                error += Math.Abs(actual.R - expected.R) + Math.Abs(actual.G - expected.G) + Math.Abs(actual.B - expected.B);
                samples++;
            }
        Check(error / samples < 0.045f, $"shot {shot}: rendered pixels match illustration ({error / samples:F4})");
    }
}
