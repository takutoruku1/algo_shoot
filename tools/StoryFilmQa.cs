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
            await Frames(15);
            await AdvanceUntil(() => Read<int>(stage, "_step") == (rei ? 12 : 13));
            var boss = world.GetNode<Enemy>($"Boss{stageName}");
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
                Check(Read<bool>(boss, "_mealFired") && Read<int>(boss, "_mealPhase") > 0, "archive mechanic starts after memory");
                Check(Read<int>(boss, "_gotoPhase") == 0, "crossfire does not overlap archive mechanic");
                Check(!Read<bool>(boss, "_form2", typeof(Enemy)), "form change waits for the archive mechanic");
                await WaitUntil(() => Read<int>(boss, "_mealPhase") == 0, 1600);
                if (burst)
                {
                    Check(Read<int>(boss, "_gotoPhase") > 0, "large damage queues crossfire after archives");
                    await WaitUntil(() => Read<int>(boss, "_gotoPhase") == 0, 1600);
                }
                Check(Read<bool>(boss, "_form2", typeof(Enemy)), "second form follows completed archive mechanic");
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
