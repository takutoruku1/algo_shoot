using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class CustomizeQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object o, string field) => (T)o.GetType().GetField(field, Private)!.GetValue(o)!;
    private static void Write(object o, string field, object value) => o.GetType().GetField(field, Private)!.SetValue(o, value);
    private static object? Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Private)!.Invoke(o, args);
    private static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception(text);
        GD.Print($"[CustomizeQA] PASS {text}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/customize/"), "isolated save data");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.ResetPersistent();
            await Frames(4);
            Check(Cosmetics.All.Select(i => i.Id).Distinct().Count() == Cosmetics.All.Length, "catalog IDs are unique");
            Check(game.OwnsCosmetic(Cosmetics.DefaultCursor) && game.CostumeFor(Job.Tank).Price == 0, "original appearances remain free");
            Check(!game.TryPurchaseCosmetic("cursor_ember") && game.Impression == 0, "insufficient funds cannot purchase");
            game.TrainingSetImpression(5000);
            Check(!game.TryPurchaseCosmetic("akari_dayoff") && !game.EquipCosmetic("akari_default"), "unrescued characters are locked");
            Check(!game.TryPurchaseCosmetic("invalid") && !game.EquipCosmetic("cursor_star"), "unknown and unowned items cannot be used");
            Check(game.TryPurchaseCosmetic("cursor_ember") && game.Impression == 4700, "cursor charges exactly 300 Imp");
            Check(!game.TryPurchaseCosmetic("cursor_ember") && game.Impression == 4700, "repeat purchase does not debit twice");
            Check(game.EquipCosmetic("cursor_ember") && game.SelectedCursor == "cursor_ember", "cursor equips immediately");
            Check(game.EquipCosmetic(Cosmetics.DefaultCursor) && game.Impression == 4700, "returning to default is free");
            var menu = OpenMenu();
            await Frames(6);
            Check(Read<JobTuning[]>(menu, "_characters").Length == 1, "new save shows only Mina");
            Write(menu, "_time", 2.0);
            Write(menu, "_focus", 0);
            Nav(menu, "ui_right");
            Check(Read<int>(menu, "_tab") == 1, "keyboard/pad navigation switches category");
            Nav(menu, "ui_down"); Nav(menu, "ui_right");
            Check(Read<int>(menu, "_character") == 0, "keyboard navigation cannot reveal locked characters");
            Nav(menu, "ui_down"); Nav(menu, "ui_right");
            Check(Read<int>(menu, "_pose") == 1, "keyboard navigation switches preview pose");
            Nav(menu, "ui_down"); Nav(menu, "ui_right");
            Nav(menu, "ui_accept");
            Check(Read<string?>(menu, "_pendingPurchase") == "mina_starway", "keyboard purchase opens confirmation");
            Nav(menu, "ui_accept");
            Check(Read<string?>(menu, "_pendingPurchase") == null && game.Impression == 4700, "confirmation defaults to cancel");
            Write(menu, "_pose", 0);
            Click(menu, new Vector2(372, 116));
            ClickItem(menu, 1);
            Check(!game.OwnsCosmetic("mina_starway"), "previewing does not buy or equip");
            await Shot("mina_preview");
            Click(menu, new Vector2(945, 630));
            Check(Read<string?>(menu, "_pendingPurchase") == "mina_starway" && game.Impression == 4700, "purchase requires confirmation");
            await Shot("purchase_confirm");
            Click(menu, new Vector2(510, 439));
            Check(Read<string?>(menu, "_pendingPurchase") == null && game.Impression == 4700, "cancel leaves wallet untouched");
            Click(menu, new Vector2(945, 630));
            Click(menu, new Vector2(734, 439));
            Check(game.OwnsCosmetic("mina_starway") && game.CostumeFor(Job.Tank).Id == "mina_starway" && game.Impression == 3900,
                "confirmed purchase equips once");
            Click(menu, new Vector2(945, 630));
            Check(game.Impression == 3900, "equipped button cannot repurchase");
            Check(game.SelectedJob == Job.Tank, "browsing never switches the player account");
            menu.QueueFree();
            await Frames(4);

            foreach (var stage in GameManager.Stages) Read<HashSet<string>>(game, "_cleared").Add(stage.Id);
            foreach (var item in Cosmetics.All.Where(i => i.Price > 0))
                if (!game.OwnsCosmetic(item.Id)) Check(game.TryPurchaseCosmetic(item.Id), $"purchase {item.Id}");
            foreach (var item in Cosmetics.All.Where(i => i.Price > 0)) Check(game.EquipCosmetic(item.Id), $"equip {item.Id}");
            Check(game.Impression == 1200, "all purchases debit the catalog total only");
            game.SaveToSlot(1);
            game.AutoSaveEnabled = true;
            game.EquipCosmetic("cursor_ember");
            Check(game.SlotExists(0), "equipment changes follow autosave settings");
            game.AutoSaveEnabled = false;
            game.ResetPersistent();
            Check(game.SelectedCursor == Cosmetics.DefaultCursor && game.CostumeFor(Job.Magic).Price == 0, "new game clears appearances");
            Check(game.LoadFromSlot(1) && game.Impression == 1200 && game.SelectedCursor == "cursor_star", "slot load restores wallet and cursor");
            foreach (var job in Jobs.All) Check(game.CostumeFor(job.Id).Price == 800, $"{job.CharacterId} keeps independent equipped costume");
            Check(game.LoadFromSlot(0) && game.SelectedCursor == "cursor_ember", "autosave restores selected cursor");
            game.LoadFromSlot(1);

            menu = OpenMenu();
            await Frames(4);
            Write(menu, "_time", 3.0);
            Check(Read<JobTuning[]>(menu, "_characters").Length == 4, "rescued accounts appear in wardrobe");
            Check(GetNode<PauseMenu>("/root/PauseMenu").HintClickable, "customization is a non-combat menu");
            Click(menu, new Vector2(156, 116));
            ClickItem(menu, 0);
            Click(menu, new Vector2(945, 630));
            await Frames(3);
            Check(game.SelectedCursor == Cosmetics.DefaultCursor, "equipping while preview is loaded keeps shared texture alive");
            ClickItem(menu, 2);
            Click(menu, new Vector2(945, 630));
            await Frames(3);
            await Shot("cursors");
            foreach (var job in Jobs.All)
            {
                Click(menu, new Vector2(372, 116));
                Click(menu, new Vector2(729 + (int)job.Id * 142, 171));
                ClickItem(menu, 1);
                Check(Read<CosmeticItem[]>(menu, "_items")[1].Character == job.Id, "character tab selects its own catalog");
                await Shot(job.CharacterId + "_wardrobe");
                Click(menu, new Vector2(420, 631));
                await Frames(12);
                await Shot(job.CharacterId + "_dodge_preview");
                Click(menu, new Vector2(168, 631));
            }
            foreach (var size in new[] { new Vector2I(960, 540), new Vector2I(540, 960), new Vector2I(1920, 1080) })
            {
                DisplayServer.WindowSetSize(size);
                await Frames(4);
                await Shot($"viewport_{size.X}x{size.Y}");
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            game.TrainingSetImpression(0);
            foreach (var item in Cosmetics.All)
            {
                Check(UiKit.TextW(UiKit.ZenBold, item.Name, 20) <= 300, $"title fits: {item.Id}");
                string[] poses = item.Kind == CosmeticKind.Cursor ? new[] { "idle" }
                    : new[] { "idle", "aim_u", "aim_ur", "aim_r", "aim_dr", "aim_d", "spin_00", "spin_01", "spin_02", "spin_03", "spin_04" };
                foreach (string pose in poses)
                {
                    var tex = GD.Load<Texture2D>(item.PosePath(pose));
                    using var image = tex.GetImage();
                    Check(image.DetectAlpha() != Image.AlphaMode.None && image.GetUsedRect().Size.X > 20, $"{item.Id}/{pose} has nonempty transparent art");
                    if (item.Kind == CosmeticKind.Cursor)
                        Check(tex.GetWidth() == 34 && tex.GetHeight() == 40 && image.GetPixel(0, 0).A > 0.1f, "cursor uses compact dimensions and aligned tip");
                }
            }
            menu.QueueFree();
            await Frames(4);
            await TestPlayers(game);
            TestOldAndInvalidSave(game);
            game.ShopTutorialSeen = true;
            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub); GetTree().CurrentScene = hub;
            await Frames(5);
            for (int i = 0; i < 5; i++)
            {
                var rect = (Rect2)Call(hub, "HomeAppRect", i)!;
                Check(rect.Position.X >= 400 && rect.End.X <= 880 && rect.End.Y < 610, $"home app {i} fits phone grid");
            }
            Write(hub, "_homeSel", 2);
            Write(hub, "_navHeld", false);
            Input.ActionPress("ui_up"); Call(hub, "ProcessHome"); Input.ActionRelease("ui_up"); Call(hub, "ProcessHome");
            Check(Read<int>(hub, "_homeSel") == 2, "up on the first app row does not jump sideways");
            Input.ActionPress("ui_down"); Call(hub, "ProcessHome"); Input.ActionRelease("ui_down"); Call(hub, "ProcessHome");
            Check(Read<int>(hub, "_homeSel") == 4, "down reaches the customization app on the second row");
            await Shot("home");
            Call(hub, "OpenHomeApp", 4);
            await Frames(80);
            Check(GetTree().CurrentScene is Customize, "home icon opens customization app");
            var opened = (Customize)GetTree().CurrentScene;
            Call(opened, "Leave");
            await Frames(5);
            Check(GetTree().CurrentScene is Hub, "back returns to phone home");
            GetTree().CurrentScene.QueueFree();
            await Frames(4);
            Audio.Instance?.StopMusic(0);
            foreach (var p in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>()) { p.Stop(); p.Stream = null; }
            GC.Collect(); GC.WaitForPendingFinalizers();
            await Frames(4);
            GD.Print("[CustomizeQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError($"[CustomizeQA] FAIL {e}"); GetTree().Quit(1); }
    }

    private Customize OpenMenu()
    {
        var menu = GD.Load<PackedScene>("res://Customize.tscn").Instantiate<Customize>();
        GetTree().Root.AddChild(menu); GetTree().CurrentScene = menu;
        return menu;
    }

    private static void Click(Customize menu, Vector2 point)
    {
        void PadField(string name, object value) => typeof(Pad).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
        PadField("_mousePos", point); PadField("_usingMouse", true); PadField("_mL", true); PadField("_mLPrev", false);
        menu._Process(1.0 / 60);
        PadField("_mL", false); PadField("_mLPrev", false);
    }

    private static void ClickItem(Customize menu, int index)
        => Click(menu, ((Rect2)Call(menu, "ItemRect", index)!).GetCenter());

    private static void Nav(Customize menu, string action)
    {
        Input.ActionPress(action);
        menu._Process(1.0 / 60);
        Input.ActionRelease(action);
        menu._Process(1.0 / 60);
    }

    private async Task TestPlayers(GameManager game)
    {
        foreach (var job in Jobs.All)
        {
            game.SelectedJob = job.Id;
            var outfit = game.CostumeFor(job.Id);
            var training = new TrainingRoot();
            GetTree().Root.AddChild(training); GetTree().CurrentScene = training;
            await Frames(5);
            var player = Read<Player>(training, "_player");
            var sprite = player.GetNode<Sprite2D>("Sprite");
            Check(sprite.Texture.ResourcePath == outfit.PosePath("idle"), $"{job.CharacterId} spawns in equipped outfit");
            Check(player.Lives == game.StartLives && player.CharacterId == job.CharacterId && game.SelectedShotMode == job.Mode, "costume does not alter character stats");
            var dummy = Read<TrainingDummy>(training, "_dummy");
            Write(player, "_locked", true); Write(player, "_lockTarget", dummy);
            foreach (var pair in new[] { ("u", Vector2.Up), ("ur", new Vector2(1,-1)), ("r", Vector2.Right), ("dr", new Vector2(1,1)), ("d", Vector2.Down) })
            {
                dummy.GlobalPosition = player.GlobalPosition + pair.Item2.Normalized() * 50;
                await Frames(3);
                Check(sprite.Texture.ResourcePath == outfit.PosePath("aim_" + pair.Item1), "aim uses equipped outfit");
            }
            Call(player, "TryDodge", Vector2.Right);
            await Frames(4);
            Check(Read<float>(player, "_dodgeTimer") > 0, "dodge starts");
            for (int i = 0; i < 5; i++)
            {
                Call(player, "ApplySpinFrame", Mathf.Tau * (i + 0.1f) / 8);
                Check(sprite.Texture.ResourcePath == outfit.PosePath($"spin_{i:00}"), "dodge retains equipped costume");
                Check(Mathf.IsEqualApprox(sprite.Scale.Y * sprite.Texture.GetHeight(), 36), "dodge scale remains stable");
            }
            Call(player, "SpawnTrail");
            Check(Read<List<Sprite2D>>(player, "_trail").Last().Texture.ResourcePath.Contains("costume_v1"), "afterimage retains outfit");
            await Shot(job.CharacterId + "_battle");
            Write(player, "_locked", false);
            await Frames(50);
            Check(sprite.Texture.ResourcePath == outfit.PosePath("idle"), "dodge returns to equipped idle");
            training.QueueFree(); await Frames(5);
            GetNode<BulletPool>("/root/Pool").DespawnAll();
        }
    }

    private static void TestOldAndInvalidSave(GameManager game)
    {
        game.SaveToSlot(2);
        Godot.Collections.Dictionary data;
        using (var file = FileAccess.Open("user://save_2.json", FileAccess.ModeFlags.Read))
            data = Json.ParseString(file.GetAsText()).AsGodotDictionary();
        data.Remove("cosmetics");
        using (var file = FileAccess.Open("user://save_2.json", FileAccess.ModeFlags.Write)) file.StoreString(Json.Stringify(data));
        Check(game.LoadFromSlot(2) && game.SelectedCursor == Cosmetics.DefaultCursor && game.CostumeFor(Job.Tank).Price == 0,
            "older saves load original appearances without carrying another slot's purchases");
        data["cosmetics"] = new Godot.Collections.Dictionary
        {
            ["owned"] = new Godot.Collections.Array { "mina_starway", "missing", 44 },
            ["cursor"] = "cursor_star",
            ["costumes"] = new Godot.Collections.Dictionary { ["akari"] = "mina_starway", ["mina"] = "missing" },
        };
        using (var file = FileAccess.Open("user://save_2.json", FileAccess.ModeFlags.Write)) file.StoreString(Json.Stringify(data));
        Check(game.LoadFromSlot(2) && game.SelectedCursor == Cosmetics.DefaultCursor && game.CostumeFor(Job.Melee).Price == 0,
            "invalid IDs, unowned cursors and wrong-character outfits are rejected on load");
    }

    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Shot(string name)
    {
        await Frames(3); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string folder = "res://build/qa_story/customize/shots";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(folder));
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{folder}/{name}.png") == Error.Ok, "screenshot " + name);
    }
}
