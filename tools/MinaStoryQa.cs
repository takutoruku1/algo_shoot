using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

public partial class MinaStoryQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _out = "";
    private static T Read<T>(object obj, string field, Type? type = null)
        => (T)(type ?? obj.GetType()).GetField(field, Private)!.GetValue(obj)!;
    private static void Write(object obj, string field, object value, Type? type = null)
        => (type ?? obj.GetType()).GetField(field, Private)!.SetValue(obj, value);
    private static void Call(object obj, string method, Type? type = null)
        => (type ?? obj.GetType()).GetMethod(method, Private)!.Invoke(obj, null);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[MinaQA] PASS {message}");
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        bool lethal = Array.IndexOf(OS.GetCmdlineUserArgs(), "--lethal") >= 0;
        bool burst = Array.IndexOf(OS.GetCmdlineUserArgs(), "--burst") >= 0;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            _out = ProjectSettings.GlobalizePath($"res://build/qa_story/mina{(lethal ? "_lethal" : burst ? "_burst" : "")}/shots");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            var game = GetNode<GameManager>("/root/Game");
            var quoteMethod = typeof(StageMina).GetMethod("S37Quote", BindingFlags.Static | BindingFlags.NonPublic)!;
            var missingQuote = ((int, string, string))quoteMethod.Invoke(null, new object?[] { null })!;
            Check(!missingQuote.Item2.Contains("つづけて") && !missingQuote.Item2.Contains("いただ"),
                "missing choice history does not invent a player instruction");
            game.MsgCharsPerSec = 300;
            game.AutoAdvanceDialog = false;
            var root = GD.Load<PackedScene>("res://MinaBattle.tscn").Instantiate<MinaRoot>();
            await Frames(1);
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            var hud = root.Hud;
            var world = root.World;
            var player = root.Player;
            var stage = root.Stage;
            await Frames(10);
            Check(hud.EpicBannerActive && !Read<bool>(stage, "_stepStarted") && world.ProcessMode == ProcessModeEnum.Disabled,
                "title finishes before dialogue and player controls begin");
            Check(player.CharacterId == game.JobDef.CharacterId && Read<bool>(player, "_hasTexture")
                  && player.GetNode<Sprite2D>("Sprite").Texture.ResourcePath == game.JobDef.PlayerTexturePath,
                  "final stage uses the selected playable character");
            var intro = Read<(int, string, string)[]>(stage, "_intro");
            Check(Array.Exists(intro, x => x.Item2.Contains("送信元：あなた"))
                  && Array.Exists(intro, x => x.Item2.Contains("回線")), "intro explains the remaining connection and sender");
            await AdvanceUntil(() => Read<int>(stage, "_introLine") >= 9);
            await Frames(80);
            await Shot("player_intro", false);
            await AdvanceUntil(() => Read<int>(stage, "_step") == 2);
            player.SetPhysicsProcess(false);
            Write(player, "_invincible", true);
            Write(player, "_invincibleTimer", 999f);
            await WaitUntil(() => Read<Spawner?>(stage, "_echoSpawner") is { SpawnedCount: 3 }, 800);
            var echoes = new System.Collections.Generic.List<MidEnemy>();
            var echoPaths = new System.Collections.Generic.HashSet<string>();
            foreach (var node in world.GetChildren())
                if (node is MidEnemy echo) { echoes.Add(echo); echoPaths.Add(echo.GetNode<Sprite2D>("Body").Texture.ResourcePath); }
            Check(echoes.Count == 3 && echoPaths.Count == 3 && !Read<Spawner>(stage, "_echoSpawner").Active,
                "all three Mina enemies spawn once before the boss");
            foreach (var spec in EnemyTable.CharactersFor(StageTheme.Mina))
                Check(echoPaths.Contains(spec.PreTexPath), "Mina echo uses the correct stage illustration");
            Check(!game.StageCleared && world.GetNodeOrNull<BossMina>("BossMina") == null,
                "echo spawning does not clear the stage or overlap the boss");
            await Frames(140);
            await Shot("echo_wave", false);
            echoes[0].Purify();
            await Frames(5);
            Check(!game.StageCleared && Read<int>(stage, "_step") == 2, "one echo cannot complete the final stage");
            foreach (var echo in echoes) if (IsInstanceValid(echo)) echo.Purify();
            await WaitUntil(() => Read<int>(stage, "_step") == 3, 120);
            await Frames(3);
            Check(!game.StageCleared && game.PurifiedCount == 3 && game.StageTarget == 4,
                "only the boss remains in final-stage progress");
            foreach (var node in world.GetChildren())
                Check(node is not MidEnemy && node is not Ripple, "echoes and purification waves do not leak into the boss fight");
            player.SetPhysicsProcess(true);
            var boss = world.GetNode<BossMina>("BossMina");
            Write(player, "_invincible", true);
            Write(player, "_invincibleTimer", 999f);
            await Frames(80);
            Check(boss.GetNode<Sprite2D>("Body").Texture.ResourcePath.Contains("boss_mina"), "new boss illustration loaded");
            foreach (string pose in new[] { "idle", "attack", "cry", "post" })
            {
                var sprite = GD.Load<Texture2D>($"res://char/v3/boss_mina_body_{pose}.png").GetImage();
                Check(sprite.GetPixel(0, 0).A == 0 && sprite.DetectAlpha() != Image.AlphaMode.None,
                    $"{pose} sprite has genuine transparency");
            }
            await Shot("battle_black_hair", false);
            int maxHp = Read<int>(boss, "_maxHp", typeof(Enemy));
            var caster = Read<MinaPhaseAttacks>(boss, "_caster");
            caster.SetProcess(false);
            caster.CancelPendingAttacks();
            if (lethal)
            {
                Write(boss, "_hp", 0, typeof(Enemy));
                Call(boss, "Redeem", typeof(Enemy));
            }
            else
            {
                // Costume transitions are exercised separately by MinaPhaseQa.
                Write(boss, "_pattern", 2);
                Write(boss, "_hp", (int)(maxHp * (burst ? 0.18f : 0.49f)), typeof(Enemy));
                Call(boss, "OnHpChanged");
            }
            Write(game, "_comboTimer", 5.0);
            await Frames(10);
            var film = GetTree().GetFirstNodeInGroup("storyfilm") as StoryFilm;
            Check(film is MinaStoryFilm && hud.CinematicMode, "memory starts even after burst or lethal damage");
            Vector2 position = player.GlobalPosition;
            int lives = player.Lives, bombs = game.Bombs;
            float hp = boss.HpRatio;
            double elapsed = Read<double>(stage, "_stageElapsed");
            double phaseTime = Read<double>(boss, "_phaseT", typeof(Enemy));
            double comboTime = Read<double>(game, "_comboTimer");
            KeyEvent(Key.Right, true);
            KeyEvent(Key.X, true);
            await Frames(90);
            KeyEvent(Key.Right, false);
            KeyEvent(Key.X, false);
            Check(world.ProcessMode == ProcessModeEnum.Disabled && Hud.BubblePaused, "world and combat are suspended");
            Check(player.GlobalPosition == position && player.Lives == lives && game.Bombs == bombs && boss.HpRatio == hp,
                "movement, damage and bombs freeze");
            Check(Read<double>(stage, "_stageElapsed") == elapsed && Read<double>(boss, "_phaseT", typeof(Enemy)) == phaseTime
                  && Read<double>(game, "_comboTimer") == comboTime, "stage, boss and combo clocks freeze");
            var first = await Shot("memory_start", true);
            Check(first.GetWidth() == 1280 && first.GetHeight() == 720, "desktop viewport is 1280x720");
            await Frames(90);
            var moving = await Shot("memory_motion", true);
            int changed = 0;
            for (int y = 100; y < 400; y += 5)
                for (int x = 70; x < 1000; x += 5)
                    if (first.GetPixel(x, y) != moving.GetPixel(x, y)) changed++;
            Check(changed > 100, "flashback camera moves");
            DisplayServer.WindowSetSize(new Vector2I(960, 540));
            await Frames(15);
            var small = await Shot("memory_small", true);
            Check(small.GetWidth() == 960 && small.GetHeight() == 540, "small viewport is 960x540");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var backlog = GetNode<Backlog>("/root/Backlog");
            backlog.Open();
            double motion = Read<double>(film!, "_shotT", typeof(StoryFilm));
            await Frames(25);
            Check(Read<double>(film!, "_shotT", typeof(StoryFilm)) == motion, "backlog pauses flashback");
            Call(backlog, "Close");
            await AdvanceUntil(() => Read<int>(film!, "_line", typeof(StoryFilm)) == 14);
            await Frames(100);
            await Shot("unsent_request_to_rest", true);
            if (!lethal) boss.SetProcess(false);
            await AdvanceUntil(() => !IsInstanceValid(film));
            Check(!hud.CinematicMode && world.ProcessMode == ProcessModeEnum.Inherit && game.ProcessMode != ProcessModeEnum.Disabled,
                "memory restores processing");
            Check(boss.MemoryPlayed, "memory completion recorded");
            if (!lethal)
            {
                Check(boss.EncounterPhase == 2 && boss.MemoryPlayed, "memory preserves the active costume phase");
                Call(boss, "OnHpChanged");
                await Frames(10);
                Check(GetTree().GetNodesInGroup("storyfilm").Count == 0, "memory is one-shot");
                Write(boss, "_hp", 0, typeof(Enemy));
                Call(boss, "Redeem", typeof(Enemy));
                boss.SetProcess(true);
            }
            await AdvanceUntil(() => hud.CinematicMode);
            film = GetTree().GetFirstNodeInGroup("storyfilm") as StoryFilm;
            Check(film is MinaStoryFilm && Read<bool>(film!, "_aftermath", typeof(StoryFilm)), "defeat starts everyone's report");
            await Frames(100);
            await Shot("return_reports", false);
            await AdvanceUntil(() => Read<int>(film!, "_line", typeof(StoryFilm)) == 12);
            await Frames(100);
            await Shot("come_home_together", false);
            Check(Read<int>(film!, "_shot", typeof(StoryFilm)) == 4, "Mina accepts the invitation before taking the hand");
            await AdvanceUntil(() => Read<int>(film!, "_line", typeof(StoryFilm)) == 13);
            var grade = Read<ShaderMaterial>(film!, "_grade", typeof(StoryFilm));
            Check((grade.GetShaderParameter("previous_texture").AsGodotObject() as Texture2D)?.ResourcePath
                  == "res://char/bg2/story/cg_mina_reunion_v1.png", "hand scene dissolves from the reunion CG");
            await Shot("take_hand_blend", false);
            await Frames(65);
            await Shot("take_hand", false);
            DisplayServer.WindowSetSize(new Vector2I(960, 540));
            await Frames(15);
            await Shot("take_hand_small", false);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(15);
            if (!lethal && !burst)
            {
                stage.SetProcess(false);
                await AdvanceUntil(() => !IsInstanceValid(film));
                bool ended = false;
                MinaStoryFilm.Play(hud, world, false, () => ended = true);
                KeyEvent(Key.Ctrl, true);
                await WaitUntil(() => ended, 2000);
                KeyEvent(Key.Ctrl, false);
                await Frames(2);
                Check(ended && !hud.CinematicMode, "read-only fast-forward completes memory");
                game.AutoAdvanceDialog = true;
                ended = false;
                MinaStoryFilm.Play(hud, world, true, () => ended = true);
                await WaitUntil(() => ended, 3000);
                game.AutoAdvanceDialog = false;
                await Frames(2);
                Check(ended && !Hud.BubblePaused, "auto mode completes everyone's report");
                MinaStoryFilm.Play(hud, world, false, () => throw new Exception("aborted callback fired"));
                await Frames(5);
                hud.GetNode("MinaStoryFilm").QueueFree();
                await Frames(5);
                Check(!hud.CinematicMode && !Hud.BubblePaused && world.ProcessMode == ProcessModeEnum.Inherit
                      && game.ProcessMode != ProcessModeEnum.Disabled, "aborted movie restores processing");
                stage.SetProcess(true);
            }
            await AdvanceUntil(() => GetTree().CurrentScene.SceneFilePath == "res://Final.tscn");
            Check(!Hud.BubblePaused, "return film releases pause before final choice");
            var final = GetTree().CurrentScene;
            await AdvanceUntil(() => Read<ChoiceOverlay?>(final, "_choice") != null);
            await Frames(60);
            await Shot("final_word_choice", false);
            await AdvanceUntil(() => GetTree().CurrentScene.SceneFilePath == "res://Epilogue.tscn");
            var epilogue = GetTree().CurrentScene;
            await AdvanceUntil(() => Read<int>(epilogue, "_line") == 12);
            await Frames(120);
            await Shot("rest_under_the_sky", false);
            Check(Read<int>(epilogue, "_phase") == 0, "new sky wish appears before walking or credits");
            GD.Print($"[MinaQA] {(lethal ? "LETHAL" : burst ? "BURST" : "NORMAL")} ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[MinaQA] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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

    private async Task WaitUntil(Func<bool> condition, int frames)
    {
        for (int i = 0; i < frames && !condition(); i++) await Frames(1);
        if (!condition()) throw new Exception("Timed out waiting for playback");
    }

    private static void KeyEvent(Key key, bool pressed)
        => Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = pressed });

    private async Task<Image> Shot(string name, bool grayscale)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng($"{_out}/{name}.png");
        float min = 1, max = 0;
        int colored = 0, green = 0;
        for (int y = image.GetHeight() / 5; y < image.GetHeight() / 2; y += 5)
            for (int x = image.GetWidth() / 8; x < image.GetWidth() * 7 / 8; x += 5)
            {
                var c = image.GetPixel(x, y);
                min = Math.Min(min, c.R);
                max = Math.Max(max, c.R);
                if (Math.Abs(c.R - c.G) + Math.Abs(c.G - c.B) > 0.025f) colored++;
                if (c.G > 0.8f && c.R < 0.15f && c.B < 0.15f) green++;
            }
        Check(max - min > 0.15f, $"{name}: nonblank");
        Check(grayscale ? colored == 0 : colored > 100, $"{name}: correct color mode");
        Check(green == 0, $"{name}: no chroma background");
        if (GetTree().GetFirstNodeInGroup("storyfilm") is StoryFilm film)
            StoryFilmQa.CheckFrame(film, image);
        return image;
    }
}
