using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class ShopStyleQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Write(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception(text);
        GD.Print($"[ShopStyleQA] PASS {text}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(2);
            foreach (var job in Jobs.All)
            {
                game.TrainingSetAllUpgrades(false);
                game.TrainingSetImpression(2480);
                game.SelectedJob = job.Id;
                for (int i = 0; i < 6; i++) game.TrainingSetUpgrade(GameManager.Upgrades[i].Id, true);
                var shop = GD.Load<PackedScene>("res://Shop.tscn").Instantiate<Shop>();
                GetTree().Root.AddChild(shop);
                GetTree().CurrentScene = shop;
                Write(shop, "_t", 3.0);
                Write(shop, "_toastT", 0.0);
                await Frames(8);
                if (OS.GetCmdlineUserArgs().Contains("--before"))
                {
                    await Shot("before");
                    shop.QueueFree();
                    await Frames(3);
                    break;
                }
                Check(Read<Texture2D>(shop, "_playerShot").ResourcePath == job.PlayerTexturePath, $"{job.CharacterId} uses current player art");
                Check(Read<int>(shop, "_sel") == 6, "starts on next upgrade");
                await Shot(job.CharacterId);
                for (int i = 0; i < GameManager.Upgrades.Length; i++)
                {
                    Write(shop, "_sel", i);
                    await Frames(1);
                    var row = (Rect2)typeof(Shop).GetMethod("RowRect", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { i })!;
                    Check(row.Position.Y >= 120 && row.End.Y <= 640, $"row {i} stays above navigation");
                    var def = GameManager.Upgrades[i];
                    Check(UiKit.TextW(UiKit.ZenBold, def.Name, 17) < 270, "catalog label leaves price space");
                    Check(UiKit.TextW(UiKit.ZenBold, def.Name, 34) < 386, "detail title leaves character space");
                    Check(UiKit.WrapLines(UiKit.Zen, def.Desc, 18, 330).Count <= 2, "description fits");
                    var pair = ((string now, string after))Call(shop, "EffectPair", i)!;
                    Check(UiKit.WrapLines(UiKit.ZenBold, pair.now, 18, 284).Count <= 2 && UiKit.WrapLines(UiKit.ZenBold, pair.after, 18, 284).Count <= 2,
                        $"{job.CharacterId} upgrade {i} effect fits");
                }
                Write(shop, "_sel", 6);
                Call(shop, "ShowShopTalk", CompanionDialogue.Menu.ShopEnter, new[] { "急がなくて結構ですよ。ここは、時間が減りませんので。" });
                await Frames(24);
                Check(UiKit.WrapLines(UiKit.ZenBold, Read<string>(shop, "_toast"), 16, 638).Count <= 3, "dialogue fits separate bottom area");
                await Shot(job.CharacterId + "_dialogue");
                DisplayServer.WindowSetSize(new Vector2I(960, 540));
                await Frames(6);
                await Shot(job.CharacterId + "_small");
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                Write(shop, "_toastT", 0.0);
                game.TrainingSetImpression(0);
                await Frames(3);
                Check(!Read<bool>(shop, "_buyBtnActive"), "insufficient funds disables purchase button");
                Call(shop, "OnConfirm");
                Check(game.GetUpgradeLevel("n_charge") == 0 && game.Impression == 0, "insufficient funds does not purchase");
                await Shot(job.CharacterId + "_unaffordable");
                game.TrainingSetImpression(100000);
                Write(shop, "_sel", 13);
                Call(shop, "OnConfirm");
                Check(game.GetUpgradeLevel("n_option") == 0 && game.Impression == 100000, "locked upgrade cannot bypass order");
                await Frames(3);
                await Shot(job.CharacterId + "_locked");
                Write(shop, "_sel", 6);
                Call(shop, "OnConfirm");
                Check(game.Impression == 98800 && game.GetUpgradeLevel("n_charge") == 1 && Read<int>(shop, "_sel") == 7, "purchase debits exact cost and advances focus");
                await Frames(8);
                await Shot(job.CharacterId + "_purchase");
                Write(shop, "_sel", 6);
                Call(shop, "OnConfirm");
                Check(game.Impression == 98800, "owned upgrade cannot be repurchased");
                game.TrainingSetAllUpgrades(true);
                Write(shop, "_sel", 13);
                Write(shop, "_toastT", 0.0);
                await Frames(3);
                Check(!Read<bool>(shop, "_buyBtnActive"), "completed state cannot purchase");
                await Shot(job.CharacterId + "_complete");
                if (job.Id == Job.Tank)
                {
                    DisplayServer.WindowSetSize(new Vector2I(1920, 1080));
                    game.TrainingSetImpression(long.MaxValue);
                    await Frames(6);
                    await Shot("wide_large_wallet");
                    DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                }
                shop.QueueFree();
                await Frames(3);
            }
            if (!OS.GetCmdlineUserArgs().Contains("--before")) await CheckNavigation(game);
            Audio.Instance?.StopMusic(0);
            foreach (var player in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { player.Stop(); player.Stream = null; }
            await Task.Delay(250);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(5);
            GD.Print("[ShopStyleQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"[ShopStyleQA] FAIL {e}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void Click(Shop shop, Rect2 rect, int id)
    {
        void SetPad(string name, object value) => typeof(Pad).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
        UiKit.BeginHotspots(rect.GetCenter());
        UiKit.Hotspot(rect, id);
        SetPad("_mL", true);
        SetPad("_mLPrev", false);
        shop._Process(1.0 / 60);
        SetPad("_mL", false);
        SetPad("_mLPrev", false);
    }

    private async Task CheckNavigation(GameManager game)
    {
        game.TrainingSetAllUpgrades(false);
        game.TrainingSetImpression(3000);
        game.SelectedJob = Job.Tank;
        var shop = GD.Load<PackedScene>("res://Shop.tscn").Instantiate<Shop>();
        GetTree().Root.AddChild(shop);
        GetTree().CurrentScene = shop;
        Write(shop, "_t", 3.0);
        await Frames(5);
        Input.ActionPress("ui_down");
        shop._Process(1.0 / 60);
        Input.ActionRelease("ui_down");
        shop._Process(1.0 / 60);
        Check(Read<int>(shop, "_sel") == 1, "keyboard changes selection");
        var row = (Rect2)typeof(Shop).GetMethod("RowRect", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { 0 })!;
        Click(shop, row, 0);
        Check(Read<int>(shop, "_sel") == 0 && game.Impression == 3000, "first row click selects without purchasing");
        await Frames(2);
        Click(shop, Read<Rect2>(shop, "_buyBtnRect"), -101);
        Check(game.GetUpgradeLevel("n_life_1") == 1 && game.Impression == 2850, "purchase button works with mouse");
        await Frames(2);
        Input.ActionPress("ui_accept");
        shop._Process(1.0 / 60);
        Input.ActionRelease("ui_accept");
        shop._Process(1.0 / 60);
        Check(game.GetUpgradeLevel("n_dodge") == 1 && game.Impression == 2650, "keyboard purchase works");
        await Frames(2);
        Click(shop, Read<Rect2>(shop, "_trainBtnRect"), -102);
        await Frames(5);
        Check(GetTree().CurrentScene is TrainingRoot, "training button opens practice");
        GetTree().ChangeSceneToFile("res://Shop.tscn");
        await Frames(5);
        shop = (Shop)GetTree().CurrentScene;
        Check(game.Impression == 2650 && game.GetUpgradeLevel("n_dodge") == 1, "practice round trip preserves purchases");
        Write(shop, "_t", 3.0);
        Click(shop, Read<Rect2>(shop, "_backBtnRect"), -100);
        Check(Read<bool>(shop, "_exitPending"), "home button keeps farewell dialogue");
        Write(shop, "_exitDelayT", 0.0);
        await Frames(5);
        Check(GetTree().CurrentScene is Hub, "home button returns to smartphone home");
        GetTree().CurrentScene.QueueFree();
        await Frames(3);
    }

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string dir = ProjectSettings.GlobalizePath("res://build/qa_story/shop_style/shots");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{dir}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
