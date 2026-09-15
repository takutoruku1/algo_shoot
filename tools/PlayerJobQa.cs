using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

public partial class PlayerJobQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static void Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[PlayerJobQA] PASS {message}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            var pool = GetNode<BulletPool>("/root/Pool");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.GrantDodge();
            // ジョブは 2026-09-14 から解禁制（その子の面をクリアすると開く）。ここは4キャラの
            //   自機まわりを見るQAなので、クリア記録だけを直接立てて全ジョブを開けた状態から始める
            //   （CompleteStage だと報酬・オートセーブまで動く。解禁ゲート自体の検証は HubJobQa の担当）。
            var clearedStages = Read<HashSet<string>>(game, "_cleared");
            foreach (var stage in GameManager.Stages) clearedStages.Add(stage.Id);
            game.Difficulty = GameManager.Diff.Normal;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);

            var expected = new[] { (Job.Tank, "mina", "ミナ"), (Job.Melee, "akari", "あかり"),
                (Job.Heal, "koharu", "こはる"), (Job.Magic, "rei", "レイ") };
            CheckAssets();
            await PoseSheets();
            foreach (var (id, character, name) in expected)
            {
                var job = Jobs.Get(id);
                Check(job.CharacterId == character && job.CharacterName == name, $"{id} maps to {character}");
                game.SelectedJob = id;
                game.SaveToSlot(0);
                game.SelectedJob = id == Job.Tank ? Job.Melee : Job.Tank;
                Check(game.LoadFromSlot(0) && game.SelectedJob == id && game.JobDef.CharacterId == character,
                    $"{character} survives save/load using the existing job value");

                var training = new TrainingRoot { Name = "TrainingRoot" };
                GetTree().Root.AddChild(training);
                GetTree().CurrentScene = training;
                game.TrainingSetAllUpgrades(false);
                Call(training, "RebuildPlayer");
                await Frames(3);
                var player = Read<Player>(training, "_player");
                var sprite = player.GetNode<Sprite2D>("Sprite");
                var dummy = Read<TrainingDummy>(training, "_dummy");
                Check(game.SelectedJob == id && player.CharacterId == character
                    && sprite.Texture.ResourcePath == job.PlayerTexturePath, $"training spawns {character}");
                Check(Mathf.IsEqualApprox(sprite.Scale.Y * sprite.Texture.GetHeight(), 36f), $"{character} height is 36 world pixels");
                Check(player.Lives == game.StartLives && game.SelectedShotMode == job.Mode,
                    $"{character} keeps job lives and shot mode");
                Check(Mathf.IsEqualApprox(((CircleShape2D)player.GetNode<CollisionShape2D>("HitShape").Shape).Radius,
                    2f * game.HitRadiusMul), $"{character} keeps the shared hitbox");
                CheckMarker(player, character);
                Write(player, "_invincible", false);
                Call(player, "SetSpriteVisible", true);
                player.GlobalPosition = new Vector2(Field.Left + 50, 120);
                var position = player.GlobalPosition;
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Right, PhysicalKeycode = Key.Right, Pressed = true });
                await Frames(8);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Right, PhysicalKeycode = Key.Right, Pressed = false });
                Check(player.GlobalPosition.X > position.X + 1f, $"{character} responds to movement");
                await Frames(18);
                bool fired = false;
                foreach (var node in pool.GetChildren()) if (node is Bullet bullet && bullet.Active) fired = true;
                Check(fired || dummy.Hp < TrainingDummy.DummyMaxHp, $"{character} fires shots");
                await Shot($"{character}_play");

                player.GlobalPosition = new Vector2(Field.CenterX, 108);
                Write(player, "_locked", true);
                Write(player, "_lockTarget", dummy);
                foreach (var (direction, pose) in new[] { (Vector2.Up, "u"), (new Vector2(1, -1), "ur"),
                    (Vector2.Right, "r"), (new Vector2(1, 1), "dr"), (Vector2.Down, "d"), (Vector2.Left, "r"),
                    (new Vector2(-1, -1), "ur"), (new Vector2(-1, 1), "dr") })
                {
                    dummy.GlobalPosition = player.GlobalPosition + direction.Normalized() * 65f;
                    await Frames(3);
                    Check(player.LockedOn && sprite.Texture.ResourcePath == $"res://char/player/{character}/{character}_aim_v2_{pose}.png",
                        $"{character} uses the correct pose aiming {direction}");
                    Check(Mathf.IsEqualApprox(sprite.Scale.Y * sprite.Texture.GetHeight(), 36f), $"{character} aim height is stable");
                    Check(sprite.FlipH == (direction.X < 0), $"{character} faces the target");
                    CheckMarker(player, character);
                }

                dummy.GlobalPosition = player.GlobalPosition + new Vector2(30, -60);
                await Frames(3);
                string aimPath = sprite.Texture.ResourcePath;
                Call(player, "TryDodge", Vector2.Zero);
                await Frames(4);
                Check(Read<float>(player, "_dodgeTimer") > 0f && sprite.Texture.ResourcePath.Contains(character),
                    $"{character} retains identity during dodge");
                Check(sprite.Texture.ResourcePath.Contains("_spin_v2_") && Mathf.IsZeroApprox(sprite.Rotation),
                    $"{character} uses upright dodge frames");
                int[] frames = { 0, 1, 2, 3, 4, 3, 2, 1 };
                foreach (float sign in new[] { 1f, -1f })
                foreach (int facing in new[] { 1, -1 })
                {
                    Write(player, "_dodgeSpinSign", sign);
                    Write(player, "_facing", facing);
                    for (int i = 0; i < 8; i++)
                    {
                        Call(player, "ApplySpinFrame", Mathf.Tau * (i + 0.1f) / 8f);
                        int frame = sign > 0 ? i : (8 - i) % 8;
                        Check(sprite.Texture.ResourcePath.EndsWith($"{character}_spin_v2_{frames[frame]:00}.png")
                            && sprite.FlipH == ((frame >= 5) ^ (facing < 0))
                            && Mathf.IsEqualApprox(sprite.Scale.Y * sprite.Texture.GetHeight(), 36f),
                            $"{character} spin {i} direction {sign} facing {facing}");
                    }
                }
                Write(player, "_dodgeSpinSign", 1f);
                Write(player, "_facing", 1);
                await Shot($"{character}_dodge");
                CheckMarker(player, character);
                await Frames(40);
                Check(sprite.Texture.ResourcePath == aimPath, $"{character} returns to the same aim pose after dodge");
                Write(player, "_locked", false);
                await Frames(3);
                Check(sprite.Texture.ResourcePath == job.PlayerTexturePath, $"{character} returns to its own idle pose");
                Call(training, "RebuildPlayer");
                await Frames(3);
                Check(Read<Player>(training, "_player").CharacterId == character, $"{character} survives a training rebuild");
                CheckMarker(Read<Player>(training, "_player"), character);
                DisplayServer.WindowSetSize(new Vector2I(960, 540));
                await Frames(3);
                await Shot($"{character}_play_small");
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                training.QueueFree();
                await Frames(5);
                pool.DespawnAll();
                Check(game.SelectedJob == id && !game.TrainingMode, $"{character} survives leaving training");

                foreach (string scene in new[] { "Stage0.tscn", "Akari.tscn", "Koharu.tscn", "Rei.tscn", "MinaBattle.tscn" })
                {
                    var root = GD.Load<PackedScene>($"res://{scene}").Instantiate<Node2D>();
                    GetTree().Root.AddChild(root);
                    GetTree().CurrentScene = root;
                    var stagePlayer = root.GetNode<Player>("World/Player");
                    Check(stagePlayer.CharacterId == character
                        && stagePlayer.GetNode<Sprite2D>("Sprite").Texture.ResourcePath == job.PlayerTexturePath,
                        $"{scene} spawns {character}");
                    CheckMarker(stagePlayer, character);
                    root.QueueFree();
                    await Frames(5);
                    pool.DespawnAll();
                    Hud.BubblePaused = false;
                }
            }
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) audio.Stop();
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[PlayerJobQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[PlayerJobQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void CheckMarker(Player player, string character)
    {
        var marker = player.GetNode<PlayerHitDot>("HitDot");
        var body = player.GetNode<Sprite2D>("Sprite");
        var collision = (CircleShape2D)player.GetNode<CollisionShape2D>("HitShape").Shape;
        Check(marker.CharacterId == character && marker.Texture.ResourcePath == $"res://char/player/{character}/{character}_core_v1.png",
            $"{character} uses its own generated emblem");
        Check(Mathf.IsEqualApprox(marker.Radius, collision.Radius) && marker.GlobalPosition.IsEqualApprox(player.GlobalPosition)
            && marker.Rotation == 0f && marker.Scale == Vector2.One && marker.ZIndex > body.ZIndex,
            $"{character} emblem stays above every pose at the unchanged collision center");
    }

    private static readonly string[] Poses = { "idle_v2", "aim_v2_u", "aim_v2_ur", "aim_v2_r", "aim_v2_dr", "aim_v2_d",
        "spin_v2_00", "spin_v2_01", "spin_v2_02", "spin_v2_03", "spin_v2_04" };
    private static string PosePath(string id, string pose) => $"res://char/player/{id}/{id}_{pose}.png";

    private static void CheckAssets()
    {
        var hashes = new HashSet<string>();
        foreach (var job in Jobs.All)
        {
            using var image = GD.Load<Texture2D>($"res://char/player/{job.CharacterId}/{job.CharacterId}_core_v1.png").GetImage();
            Check(image.GetHeight() == 128 && image.DetectAlpha() != Godot.Image.AlphaMode.None
                && image.GetPixel(0, 0).A < 0.05f, $"{job.CharacterId} emblem is a transparent raster asset");
            Check(hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.GetData()))),
                $"{job.CharacterId} emblem has distinct artwork");
        }
        foreach (var job in Jobs.All)
        foreach (string pose in Poses)
        {
            string path = PosePath(job.CharacterId, pose);
            using var image = Godot.Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
            Check(image.GetHeight() == 720 && image.DetectAlpha() != Godot.Image.AlphaMode.None, $"{path} normalized RGBA");
            image.Convert(Godot.Image.Format.Rgba8);
            var bytes = image.GetData();
            int solid = 0, green = 0;
            for (int p = 0; p < bytes.Length; p += 4)
            {
                if (bytes[p + 3] < 64) continue;
                solid++;
                if (bytes[p + 1] > bytes[p] + 40 && bytes[p + 1] > bytes[p + 2] + 40) green++;
            }
            Check(solid > image.GetWidth() * image.GetHeight() / 4 && green == 0, $"{path} nonblank without green background");
            Check(image.GetPixel(0, 0).A == 0 && image.GetPixel(image.GetWidth() - 1, 719).A == 0, $"{path} transparent corners");
            Check(hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))), $"{path} distinct artwork");
        }
    }

    private async Task PoseSheets()
    {
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/player_job/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        var viewport = new SubViewport { Size = new Vector2I(2040, 1136),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        viewport.AddChild(new ColorRect { Size = viewport.Size, Color = new Color("25282e") });
        string[] labels = { "通常", "照準 上", "照準 右上", "照準 右", "照準 右下", "照準 下",
            "回避 正面", "回避 45°", "回避 横", "回避 135°", "回避 後ろ" };
        for (int column = 0; column < Poses.Length; column++)
            LabelAt(labels[column], new Vector2(170 + column * 168, 22), 19);
        for (int row = 0; row < Jobs.All.Length; row++)
        {
            var job = Jobs.All[row];
            int y = 66 + row * 266;
            if (row % 2 == 0) viewport.AddChild(new ColorRect { Position = new Vector2(0, y), Size = new Vector2(2040, 266),
                Color = new Color("30343b") });
            LabelAt(job.CharacterName, new Vector2(22, y + 104), 26);
            LabelAt(job.Name, new Vector2(22, y + 144), 19);
            for (int column = 0; column < Poses.Length; column++)
                viewport.AddChild(new TextureRect { Position = new Vector2(162 + column * 168, y + 14), Size = new Vector2(156, 238),
                    Texture = GD.Load<Texture2D>(PosePath(job.CharacterId, Poses[column])),
                    TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        }
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = viewport.GetTexture().GetImage();
        Check(image.SavePng($"{path}/all_poses.png") == Error.Ok, "44-pose contact sheet");
        viewport.QueueFree();
        await Frames(2);

        void LabelAt(string text, Vector2 position, int fontSize)
        {
            var label = new Label { Text = text, Position = position };
            label.AddThemeFontOverride("font", UiKit.Zen);
            label.AddThemeFontSizeOverride("font_size", fontSize);
            viewport.AddChild(label);
        }
    }

    private async Task Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/player_job/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
