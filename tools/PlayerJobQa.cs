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
            if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--life"))
            {
                await CheckLifeHud(game);
                await Finish();
                return;
            }

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
            await Finish();
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

    private async Task Finish()
    {
        Audio.Instance?.StopMusic(0);
        foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
            if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
        await Task.Delay(250);
        await Frames(5);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GD.Print("[PlayerJobQA] ALL PASS");
        GetTree().Quit();
    }

    private async Task CheckLifeHud(GameManager game)
    {
        string output = ProjectSettings.GlobalizePath("res://build/qa_story/sidebar");
        DirAccess.MakeDirRecursiveAbsolute(output);
        using var overview = Image.CreateEmpty(373 * Jobs.All.Length, 720, false, Image.Format.Rgba8);
        game.SetProcess(false);
        int row = 0;
        foreach (var job in Jobs.All)
        {
            game.SelectedJob = job.Id;
            var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            root.Stage.SetProcess(false);
            root.World.ProcessMode = ProcessModeEnum.Disabled;
            root.Hud.HoldBubble = false;
            root.Hud.HideBubble();
            root.Hud.SetShotMode(job.Mode, false);
            root.Hud.SetElapsed(83.45f);
            root.Hud.SetFocusMode(true, true, false, 1f);
            typeof(GameManager).GetProperty("Score")!.SetValue(game, 127840L);
            typeof(GameManager).GetProperty("Combo")!.SetValue(game, 12);
            typeof(GameManager).GetProperty("PurifiedCount")!.SetValue(game, game.StageTarget / 2);
            Write(game, "_comboTimer", 1.6);
            var faces = Read<Dictionary<Job, Texture2D>>(root.Hud, "_accountFaces");
            Check(faces[job.Id].ResourcePath == CompanionDialogue.Portrait(job.Id), "account portrait matches the playable character");
            var marks = Read<Dictionary<Job, Texture2D>>(root.Hud, "_lifeMarks");
            Check(marks[job.Id].ResourcePath == $"res://char/player/{job.CharacterId}/{job.CharacterId}_core_v1.png",
                $"{job.CharacterId}: LIFE uses the same emblem as the player");
            var bombMark = Read<Texture2D>(root.Hud, "_bombMark");
            Check(bombMark.ResourcePath == "res://char/ui/bomb_v2.png", "BOMB uses the generated bomb illustration");
            using (var bombImage = bombMark.GetImage())
                Check(bombImage.DetectAlpha() != Image.AlphaMode.None && bombImage.GetPixel(0, 0).A == 0,
                    "bomb illustration has a genuinely transparent background");
            int cap = root.Player.Lives;
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(960, 540) })
            {
                DisplayServer.WindowSetSize(size);
                Write(root.Hud, "_bannerTimer", 0.0);
                Write(root.Hud, "_hurtEdge", 0f);
                Call(root.Player, "SetSpriteVisible", true);
                root.Hud.SetLives(cap);
                using var full = await Capture("full");
                var background = full.GetPixel(Mathf.RoundToInt(12f * size.X / 1280f), Mathf.RoundToInt(400f * size.Y / 720f));
                Check(background.R > 0.10f && background.R < 0.25f && Mathf.Abs(background.R - background.B) < 0.04f,
                    "sidebar uses a charcoal surface instead of white or pure black");
                if (size.X == 1280) overview.BlitRect(full, new Rect2I(0, 0, 373, 720), new Vector2I(row * 373, 0));
                Check(Light(full, cap - 1, cap) > 0.025f, $"{job.CharacterId} {size}: LIFE marks contrast with the sidebar background");
                Write(root.Player, "_invincible", false);
                root.Player.TakeHit();
                Check(root.Player.Lives == cap - 1 && Read<int>(root.Hud, "_lives") == cap - 1,
                    "damage updates the existing life count");
                using var hurt = await Capture("hurt");
                Check(Light(hurt, cap - 1, cap) < Light(full, cap - 1, cap) * 0.55f
                    && Light(hurt, 0, cap) > Light(full, 0, cap) * 0.85f,
                    "only the lost life mark becomes dim");
                Check(root.Player.AddLife() && root.Player.Lives == cap, "healing restores the missing life");
                using var healed = await Capture("healed");
                Check(Mathf.Abs(Light(healed, cap - 1, cap) - Light(full, cap - 1, cap)) < 0.01f,
                    "healing restores the same emblem at the same position and size");
                root.Hud.SetLives(0);
                using var empty = await Capture("empty");
                Check(Light(empty, 0, cap) < Light(full, 0, cap) * 0.55f, "zero lives leaves dim marks instead of hearts");
                root.Hud.SetLives(12);
                using var packed = await Capture("packed");
                Check(Light(packed, 11, 12) > 0.015f, "high life counts still fit inside the panel");
                root.Hud.SetLives(cap);
                int bombs = game.Bombs;
                Check(Light(full, 0, bombs, bomb: true) > 0.025f, "bomb silhouette remains visible at HUD size");
                Check(game.UseBomb() && game.Bombs == bombs - 1, "using a bomb consumes the existing inventory");
                using var used = await Capture("bomb_used");
                Check(Light(used, bombs - 1, bombs, bomb: true) < Light(full, bombs - 1, bombs, bomb: true) * 0.65f,
                    "the consumed bomb illustration dims");
                game.RewardCameoDefeat();
                using var refilled = await Capture("bomb_refilled");
                Check(game.Bombs == bombs
                    && Mathf.Abs(Light(refilled, 0, bombs, bomb: true) - Light(full, 0, bombs, bomb: true)) < 0.01f,
                    "bomb replenishment restores the same illustration");
                typeof(GameManager).GetProperty("Bombs")!.SetValue(game, 12);
                using var packedBombs = await Capture("bomb_packed");
                Check(Light(packedBombs, 11, 12, bomb: true) > 0.015f, "large bomb inventories fit inside the panel");
                typeof(GameManager).GetProperty("Bombs")!.SetValue(game, bombs);
                root.Hud.SetFocusMode(true, false, true, 0.6f);
                using var focusActive = await Capture("focus_active");
                root.Hud.SetFocusMode(true, false, false, 0.3f);
                using var focusCharging = await Capture("focus_charging");
                root.Hud.SetFocusMode(false, false, false, 0f);
                typeof(GameManager).GetProperty("Combo")!.SetValue(game, 0);
                using var noExtras = await Capture("no_extras");
                Check(RegionDifference(full, noExtras, new Rect2I(26, 574, 321, 44)) > 0.01f
                    && RegionDifference(full, noExtras, new Rect2I(26, 647, 321, 42)) > 0.01f,
                    "combo and focus rows only appear while applicable");
                Check(RegionDifference(full, noExtras, new Rect2I(26, 156, 321, 90)) < 0.001f,
                    "conditional rows do not shift the resource layout");
                typeof(GameManager).GetProperty("Score")!.SetValue(game, long.MaxValue);
                using var wideScore = await Capture("wide_score");
                Check(RegionDifference(noExtras, wideScore, new Rect2I(348, 452, 24, 55)) < 0.001f,
                    "maximum score fits before the sidebar edge");
                typeof(GameManager).GetProperty("Score")!.SetValue(game, 127840L);
                typeof(GameManager).GetProperty("Combo")!.SetValue(game, 12);
                root.Hud.SetFocusMode(true, true, false, 1f);

                async Task<Image> Capture(string state)
                {
                    await Frames(4);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var image = GetViewport().GetTexture().GetImage();
                    Check(image.SavePng($"{output}/{job.CharacterId}_{size.X}_{state}.png") == Error.Ok, $"LIFE screenshot {state}");
                    return image;
                }
            }
            root.QueueFree();
            await Task.Delay(150);
            await Frames(5);
            row++;
        }
        game.SetProcess(true);
        Check(overview.SavePng($"{output}/all_characters.png") == Error.Ok, "four-character sidebar overview");

        static float Light(Image image, int slot, int count = 5, bool bomb = false)
        {
            float scale = image.GetWidth() / 1280f;
            float step = Mathf.Min(bomb ? 44f : 42f, 321f / count);
            float cx = 26f + slot * step + step / 2f;
            float cy = bomb ? 298f : 220f;
            var paper = image.GetPixel(Mathf.RoundToInt(355f * scale), Mathf.RoundToInt(cy * scale));
            float sum = 0;
            int samples = 0;
            for (int y = Mathf.RoundToInt((cy - 12f) * scale); y < (cy + 12f) * scale; y++)
                for (int x = Mathf.RoundToInt((cx - 10f) * scale); x < (cx + 10f) * scale; x++)
                {
                    var c = image.GetPixel(x, y);
                    sum += (Mathf.Abs(c.R - paper.R) + Mathf.Abs(c.G - paper.G) + Mathf.Abs(c.B - paper.B)) / 3f;
                    samples++;
                }
            return sum / samples;
        }

        static float RegionDifference(Image a, Image b, Rect2I rect)
        {
            float scale = a.GetWidth() / 1280f;
            float difference = 0f;
            int samples = 0;
            for (int y = Mathf.RoundToInt(rect.Position.Y * scale); y < rect.End.Y * scale; y++)
                for (int x = Mathf.RoundToInt(rect.Position.X * scale); x < rect.End.X * scale; x++)
                {
                    var ca = a.GetPixel(x, y);
                    var cb = b.GetPixel(x, y);
                    difference += (Mathf.Abs(ca.R - cb.R) + Mathf.Abs(ca.G - cb.G) + Mathf.Abs(ca.B - cb.B)) / 3f;
                    samples++;
                }
            return difference / samples;
        }
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
            // 2026-09-17：ジョブ名（結び手…）の表記は全廃したので、行の副題は撃ち方に差し替える。
            LabelAt(ModeLabel(job.Mode), new Vector2(22, y + 144), 19);
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

    // 撃ち方の表記（GameManager.ShotModeName と同じ語）。QA走行では /root/Game が居ないことがあるので
    // インスタンス経由ではなくここで引く（2026-09-17・ジョブ名表記の全廃に伴う差し替え）。
    private static string ModeLabel(GameManager.ShotMode m) => m switch
    {
        GameManager.ShotMode.Spread => "拡散",
        GameManager.ShotMode.Homing => "ホーミング",
        GameManager.ShotMode.Accel => "加速球",
        _ => "連射",
    };

    private async Task Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/player_job/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
