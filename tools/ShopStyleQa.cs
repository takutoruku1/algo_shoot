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
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            CheckRemovedUpgrades(game);
            CheckCharacterPurchases(game);
            CheckSpecialPresentation();
            await CheckSupportUpgrades(game);
            if (DisplayServer.GetName() == "headless")
            {
                Audio.Instance?.StopMusic(0);
                foreach (var player in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
                { player.Stop(); player.Stream = null; }
                await Task.Delay(250);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Frames(5);
                GD.Print("[ShopStyleQA] DATA PASS");
                GetTree().Quit();
                return;
            }
            int chargeIndex = Array.FindIndex(GameManager.Upgrades, def => def.Id == "n_charge");
            int lastIndex = GameManager.Upgrades.Length - 1;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(2);
            foreach (var job in Jobs.All)
                if (job.UnlockStageId.Length > 0) game.CompleteStage(job.UnlockStageId);
            foreach (var job in Jobs.All)
            {
                game.SelectedJob = job.Id;
                game.TrainingSetAllUpgrades(false);
                game.TrainingSetImpression(2480);
                for (int i = 0; i < chargeIndex; i++) game.TrainingSetUpgrade(GameManager.Upgrades[i].Id, true);
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
                Check(Read<int>(shop, "_sel") == chargeIndex, "starts on next upgrade");
                await Shot(job.CharacterId);
                for (int i = 0; i < GameManager.Upgrades.Length; i++)
                {
                    Write(shop, "_sel", i);
                    await Frames(1);
                    var row = (Rect2)typeof(Shop).GetMethod("RowRect", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { i })!;
                    Check(row.Position.Y >= 120 && row.End.Y <= 640, $"row {i} stays above navigation");
                    var def = GameManager.Upgrades[i];
                    Check(UiKit.TextW(UiKit.ZenBold, def.Name, 17) < 270, "catalog label leaves price space");
                    string title = def.Id == "n_slow" ? PlayerSpecial.NameFor(job.Id) : def.Name;
                    string description = def.Id == "n_slow" ? PlayerSpecial.DescriptionFor(job.Id) : def.Desc;
                    Check(UiKit.TextW(UiKit.ZenBold, title, 32) < 386, "detail title leaves character space");
                    Check(UiKit.WrapLines(UiKit.Zen, description, 18, 330).Count <= 2, "description fits");
                    var pair = ((string now, string after))Call(shop, "EffectPair", i)!;
                    Check(UiKit.WrapLines(UiKit.ZenBold, pair.now, 18, 284).Count <= 2 && UiKit.WrapLines(UiKit.ZenBold, pair.after, 18, 284).Count <= 2,
                        $"{job.CharacterId} upgrade {i} effect fits");
                    Check(def.Rating is >= 1 and <= 5, "upgrade rating stays within five levels");
                    string stars = new string('★', def.Rating) + new string('☆', 5 - def.Rating);
                    Check(UiKit.TextW(UiKit.ZenBold, stars, 19) <= 100, "five rating stars fit before the numeric rating");
                    if (job.Id == Job.Tank || def.Id is "n_heart_magnet" or "n_hit_invul")
                        await Shot(job.CharacterId + "_" + def.Id);
                }
                Write(shop, "_sel", chargeIndex);
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
                long balance = 100000 - game.GetUpgradeCost("n_slow");
                Write(shop, "_sel", lastIndex);
                Call(shop, "OnConfirm");
                Check(game.HasSpecial && game.Impression == balance, "last upgrade can be purchased before earlier items");
                await Frames(3);
                await Shot(job.CharacterId + "_special_first");
                Write(shop, "_sel", chargeIndex);
                balance -= game.GetUpgradeCost("n_charge");
                Call(shop, "OnConfirm");
                Check(game.Impression == balance && game.GetUpgradeLevel("n_charge") == 1 && Read<int>(shop, "_sel") == chargeIndex, "purchase debits exact cost and keeps the selected item");
                await Frames(8);
                await Shot(job.CharacterId + "_purchase");
                Write(shop, "_sel", chargeIndex);
                Call(shop, "OnConfirm");
                Check(game.Impression == balance, "owned upgrade cannot be repurchased");
                game.TrainingSetAllUpgrades(true);
                Write(shop, "_sel", lastIndex);
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
            if (!OS.GetCmdlineUserArgs().Contains("--before"))
            {
                await CheckPrerequisiteUi(game);
                await CheckNavigation(game);
            }
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

    private async Task WaitForScene<T>() where T : Node
    {
        ulong deadline = Time.GetTicksMsec() + 15000;
        while ((GetTree().CurrentScene is not T || LoadingScreen.IsActive) && Time.GetTicksMsec() < deadline)
            await Frames(1);
    }

    private static void CheckRemovedUpgrades(GameManager game)
    {
        string[] removed = { "n_power_2x", "n_rate_2x", "n_lines", "n_pierce", "n_option" };
        Check(removed.All(id => GameManager.GetUpgradeDef(id) == null), "retired upgrades cannot be purchased");
        game.TrainingSetImpression(100000);
        Check(removed.All(id => !game.TryPurchase(id)) && game.Impression == 100000,
            "retired upgrade IDs do not debit currency");
        var legacy = new Godot.Collections.Dictionary();
        foreach (var def in GameManager.Upgrades.Where(def => def.Id != "n_charge_shot")) legacy[def.Id] = 1;
        foreach (string id in removed) legacy[id] = 1;
        var data = new Godot.Collections.Dictionary { ["impression"] = 100, ["upgrades"] = legacy, ["hasDodge"] = true };
        using (var file = FileAccess.Open(Startup.DataPath("save_0.json"), FileAccess.ModeFlags.Write))
            file.StoreString(Json.Stringify(data));
        Check(game.LoadFromSlot(0), "shared legacy purchases load");
        Check(game.Impression == 13100 && removed.All(id => game.GetUpgradeLevel(id) == 0),
            "removed purchases refund their original 13000 cost");
        Check(GameManager.Upgrades.All(def => game.Has(def.Id)), "remaining purchased upgrades survive migration");
        foreach (var job in Jobs.All)
        {
            game.SelectedJob = job.Id;
            Check(game.HasChargeShot && game.HasChargeTier2 && game.HasSpecial && game.HasDodge,
                $"{job.CharacterId}: shared purchases retain their effects");
        }
        Check(game.SaveToSlot(0) && game.LoadFromSlot(0) && game.Impression == 13100,
            "saving and reloading does not refund twice");
        game.ResetPersistent();
        game.TrainingSetImpression(500);
        Check(game.SaveToSlot(0) && game.LoadFromSlot(0) && game.Impression == 500,
            "saves without retired purchases receive no refund");
        game.ResetPersistent();
        foreach (var def in GameManager.Upgrades.Where(def => def.Id is not ("n_heart_magnet" or "n_hit_invul")))
            game.TrainingSetUpgrade(def.Id, true);
        game.TrainingSetImpression(3000);
        Check(game.SaveToSlot(0) && game.LoadFromSlot(0)
            && game.HasChargeTier2 && game.HasSpecial && !game.Has("n_heart_magnet") && !game.Has("n_hit_invul"),
            "existing saves preserve purchases and leave the new upgrades available");
        long balance = 3000 - game.GetUpgradeCost("n_heart_magnet") - game.GetUpgradeCost("n_hit_invul");
        Check(game.TryPurchase("n_heart_magnet") && game.TryPurchase("n_hit_invul") && game.Impression == balance,
            "existing saves can buy both new upgrades at their exact prices");
        Check(game.SaveToSlot(0) && game.LoadFromSlot(0) && game.Has("n_heart_magnet") && game.Has("n_hit_invul"),
            "both new purchases survive saving and loading");
        game.ResetPersistent();
        var oldNodes = new Godot.Collections.Dictionary { ["fire_rate_1"] = 1, ["fire_rate_2"] = 1 };
        var oldData = new Godot.Collections.Dictionary { ["impression"] = 123, ["upgrades"] = oldNodes };
        using (var file = FileAccess.Open(Startup.DataPath("save_0.json"), FileAccess.ModeFlags.Write))
            file.StoreString(Json.Stringify(oldData));
        Check(game.LoadFromSlot(0) && game.Impression == 123 && game.MigratedNodeCount == 2
            && game.Has("n_life_1") && game.HasDodge && !game.Has("n_dodge_cd"),
            "legacy investment retains the same upgrades after repricing");
        game.ResetPersistent();
    }

    private static void CheckCharacterPurchases(GameManager game)
    {
        string[] picks = { "n_slow", "n_life_2", "n_move_15x", "n_hitbox" };
        game.TrainingSetImpression(10000);
        for (int i = 0; i < Jobs.All.Length; i++)
        {
            game.SelectedJob = Jobs.All[i].Id;
            Check(game.OwnedUpgradeCount == 0 && !game.HasChargeShot, "new characters start without upgrades");
            long balance = game.Impression - GameManager.GetUpgradeDef(picks[i])!.BaseCost;
            Check(game.TryPurchase(picks[i]) && game.OwnedUpgradeCount == 1, "buy any item first");
            Check(!game.TryPurchase(picks[i]) && game.Impression == balance, "repeat purchase does not charge again");
        }
        Check(game.SaveToSlot(2) && game.LoadFromSlot(2), "character purchases round trip");
        for (int i = 0; i < Jobs.All.Length; i++)
        {
            game.SelectedJob = Jobs.All[i].Id;
            Check(game.Has(picks[i]) && game.OwnedUpgradeCount == 1
                && !game.HasChargeShot && !game.HasChargeTier2, "loading preserves each independent loadout");
            game.TrainingSetImpression(10000);
            foreach (var (upgrade, prerequisite) in new[] { ("n_dodge_cd", "n_dodge"), ("n_charge", "n_charge_shot") })
            {
                long balance = game.Impression;
                Check(!game.CanPurchase(upgrade) && !game.TryPurchase(upgrade)
                    && !game.Has(upgrade) && game.Impression == balance,
                    $"{Jobs.All[i].CharacterId}: {upgrade} requires its own character's prerequisite and never charges while locked");
                if (upgrade == "n_charge" && game.SelectedJob == Job.Tank)
                    Check(StageTutorial.TakeCharge(game).Length == 0 && !game.IsIdleDialogSeen(StageTutorial.SkillChargeSeenKey),
                        "locked charge does not consume its tutorial");
                Check(game.TryPurchase(prerequisite) && game.CanPurchase(upgrade),
                    $"{Jobs.All[i].CharacterId}: buying {prerequisite} unlocks {upgrade} immediately");
                balance -= GameManager.GetUpgradeDef(prerequisite)!.BaseCost + GameManager.GetUpgradeDef(upgrade)!.BaseCost;
                Check(game.TryPurchase(upgrade) && game.Impression == balance && game.Has(upgrade),
                    $"{Jobs.All[i].CharacterId}: unlocked {upgrade} costs the listed amount");
                Check(!game.TryPurchase(upgrade) && game.Impression == balance, "unlocked upgrades cannot be charged twice");
            }
            Check(game.HasDodge && game.HasChargeTier2, $"{Jobs.All[i].CharacterId}: purchased skills activate");
            if (game.SelectedJob == Job.Tank)
                Check(StageTutorial.TakeCharge(game).Length > 0, "buying base charge enables its tutorial");
        }
        Check(game.SaveToSlot(2) && game.LoadFromSlot(2), "prerequisites and advanced purchases round trip");
        foreach (var job in Jobs.All)
        {
            game.SelectedJob = job.Id;
            Check(game.HasDodge && game.Has("n_dodge_cd") && game.HasChargeTier2 && game.OwnedUpgradeCount == 5,
                $"{job.CharacterId}: loading retains prerequisites and advanced purchases");
        }
        game.ResetPersistent();
        game.SelectedJob = Job.Tank;
        game.TrainingSetUpgrade("n_charge", true);
        game.TrainingSetImpression(10000);
        Check(game.SaveToSlot(2) && game.LoadFromSlot(2) && game.Has("n_charge") && !game.HasChargeShot
            && game.Impression == 10000, "existing advanced purchases without prerequisites are preserved");
        Check(game.TryPurchase("n_charge_shot") && game.HasChargeTier2,
            "buying the prerequisite activates an existing advanced purchase");
        game.TrainingSetImpression(0);
        Check(!game.TryPurchase("n_life_1") && game.Impression == 0, "insufficient funds do not grant an upgrade");
        game.ResetPersistent();
    }

    private async Task CheckSupportUpgrades(GameManager game)
    {
        await Frames(2);
        foreach (var job in Jobs.All)
        foreach (bool upgraded in new[] { false, true })
        {
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.SelectedJob = job.Id;
            game.TrainingSetUpgrade("n_heart_magnet", upgraded);
            game.TrainingSetUpgrade("n_hit_invul", upgraded);
            var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<AkariRoot>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.Stage.SetProcess(false);
            root.Stage.SetPhysicsProcess(false);
            root.Hud.HoldBubble = false;
            root.Hud.HideBubble();
            var player = root.Player;
            player.SetPhysicsProcess(false);
            player.GlobalPosition = new Vector2(Field.Left + 32, 120);
            var drops = root.World.GetNode<FxLayer>("FxLayer").ScoreDrops;
            drops.SetPhysicsProcess(false);
            Check(Mathf.IsEqualApprox(player.InvincibleRemaining, 1.5f),
                $"{job.CharacterId}: spawn protection stays 1.5s (upgraded={upgraded})");

            if (job.Id == Job.Tank)
            {
                var shards = Read<System.Collections.IList>(drops, "_shards");
                foreach (PowerKind? power in new PowerKind?[] { null, PowerKind.Line, PowerKind.Speed, PowerKind.Life })
                foreach (float distance in new[] { 47f, 49f, 71f, 73f })
                {
                    shards.Clear();
                    var at = player.GlobalPosition + Vector2.Right * distance;
                    drops.Add(new FxLayer.P { X = at.X, Y = at.Y, Ttl = 1f }, 1, power: power);
                    var shard = shards[0]!;
                    shard.GetType().GetField("Age")!.SetValue(shard, 1f);
                    drops._PhysicsProcess(1d / 60);
                    bool attracted = (bool)shard.GetType().GetField("Attracted")!.GetValue(shard)!;
                    Check(attracted == (distance <= (upgraded && power == null ? 72f : 48f)),
                        $"pickup {power?.ToString() ?? "heart"} at {distance}px uses its own radius (upgraded={upgraded})");
                }
                shards.Clear();
                if (upgraded)
                {
                    long score = game.Score;
                    long money = game.Impression;
                    var at = player.GlobalPosition + Vector2.Right * 60f;
                    drops.Add(new FxLayer.P { X = at.X, Y = at.Y, Ttl = 1f }, 1);
                    for (int frame = 0; frame < 120; frame++) drops._PhysicsProcess(1d / 60);
                    Check(shards.Count == 0 && game.Score == score + 1 && game.Impression == money,
                        "a heart in the added range reaches the player and pays its reward");
                }
            }

            int lives = player.Lives;
            Write(player, "_invincible", false);
            player.TakeHit();
            float expected = job.HitInvulSec + (upgraded ? .4f : 0f);
            Check(player.Lives == lives - 1 && Mathf.IsEqualApprox(player.InvincibleRemaining, expected)
                && Mathf.IsEqualApprox(player.InvincibleRatio, 1f),
                $"{job.CharacterId}: damage starts {expected:0.0}s protection (upgraded={upgraded})");
            int frames = Mathf.RoundToInt(expected * 60);
            for (int frame = 0; frame < frames - 1; frame++) player._PhysicsProcess(1d / 60);
            player.TakeHit();
            Check(player.Lives == lives - 1 && player.InvincibleRemaining > 0f,
                $"{job.CharacterId}: protection prevents repeated hits for the full duration");
            player._PhysicsProcess(1d / 60);
            player._PhysicsProcess(1d / 60);
            Check(player.InvincibleRemaining == 0f, $"{job.CharacterId}: protection expires on time");
            player.TakeHit();
            Check(player.Lives == lives - 2, $"{job.CharacterId}: hits resume after protection expires");
            Call(player, "StartInvincible", false);
            Check(Mathf.IsEqualApprox(player.InvincibleRemaining, 1.2f), $"{job.CharacterId}: bomb protection stays 1.2s");
            game.TrainingSetUpgrade("n_dodge", true);
            Call(player, "TryDodge", Vector2.Zero);
            Check(Mathf.IsEqualApprox(Read<float>(player, "_dodgeInv"), .45f), $"{job.CharacterId}: dodge protection stays 0.45s");
            root.QueueFree();
            await Frames(5);
            GetNode<BulletPool>("/root/Pool").DespawnAll();
        }
        game.ResetPersistent();
        game.AutoSaveEnabled = false;
    }

    private static void CheckSpecialPresentation()
    {
        Check(GameManager.GetUpgradeDef("n_slow")!.Name == "必殺技", "existing purchase ID unlocks character specials");
        Check(Jobs.All.Select(job => PlayerSpecial.NameFor(job.Id)).Distinct().Count() == 4, "each character has a distinct special name");
        foreach (var job in Jobs.All)
        {
            string name = PlayerSpecial.NameFor(job.Id);
            Check(UiKit.TextW(UiKit.ZenBold, name, 14) <= Field.PanelW - 36,
                $"{job.CharacterId} special name fits the HUD");
            Check(UiKit.TextW(UiKit.ZenBold, name, 32) <= 386,
                $"{job.CharacterId} special name fits the shop title");
            Check(UiKit.WrapLines(UiKit.Zen, PlayerSpecial.DescriptionFor(job.Id), 18, 330).Count <= 2,
                $"{job.CharacterId} full special description fits the shop");
            foreach (int tab in new[] { 0, 1, 2 })
            foreach (bool owned in new[] { false, true })
            {
                string desc = HowToCanvas.SpecialControlDescription(job.Id, tab, owned);
                Check(desc.Contains(name) && !desc.Contains("集中")
                    && UiKit.WrapLines(UiKit.Zen, desc, UiKit.FontLabel, 516).Count <= 2,
                    $"{job.CharacterId} special instructions fit tab {tab}, owned={owned}");
            }
        }
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

    private async Task CheckPrerequisiteUi(GameManager game)
    {
        foreach (var (upgrade, prerequisite) in new[] { ("n_dodge_cd", "n_dodge"), ("n_charge", "n_charge_shot") })
        {
            game.ResetPersistent();
            game.CompleteStage("akari");
            game.TrainingSetImpression(10000);
            int index = Array.FindIndex(GameManager.Upgrades, def => def.Id == upgrade);
            int baseIndex = Array.FindIndex(GameManager.Upgrades, def => def.Id == prerequisite);
            var shop = GD.Load<PackedScene>("res://Shop.tscn").Instantiate<Shop>();
            GetTree().Root.AddChild(shop);
            GetTree().CurrentScene = shop;
            Write(shop, "_t", 3.0);
            Write(shop, "_sel", index);
            Write(shop, "_toastT", 0.0);
            await Frames(5);
            Check(!Read<bool>(shop, "_buyBtnActive"), $"{upgrade}: locked purchase button is disabled with sufficient funds");
            await Shot(upgrade + "_locked");
            Input.ActionPress("ui_accept");
            shop._Process(1.0 / 60);
            Input.ActionRelease("ui_accept");
            shop._Process(1.0 / 60);
            Check(!game.Has(upgrade) && game.Impression == 10000
                && Read<string>(shop, "_toast").Contains(GameManager.GetUpgradeDef(prerequisite)!.Name),
                $"{upgrade}: keyboard purchase explains the prerequisite without charging");
            DisplayServer.WindowSetSize(new Vector2I(960, 540));
            await Frames(4);
            await Shot(upgrade + "_locked_small");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            Write(shop, "_sel", baseIndex);
            Call(shop, "OnConfirm");
            Write(shop, "_sel", index);
            Write(shop, "_toastT", 0.0);
            await Frames(4);
            Check(Read<bool>(shop, "_buyBtnActive"), $"{upgrade}: buying the prerequisite enables the button in the same visit");
            await Shot(upgrade + "_unlocked");
            Click(shop, new Rect2(373, 31, 177, 38), -103);
            await Frames(4);
            Check(game.SelectedJob == Job.Melee && !Read<bool>(shop, "_buyBtnActive"),
                $"{upgrade}: switching to another character restores its locked state");
            Call(shop, "OnConfirm");
            Check(!game.Has(upgrade) && game.Impression == 10000 - GameManager.GetUpgradeDef(prerequisite)!.BaseCost,
                $"{upgrade}: another character cannot use Mina's prerequisite");
            Click(shop, new Rect2(373, 31, 177, 38), -103);
            await Frames(4);
            Check(game.SelectedJob == Job.Tank && Read<bool>(shop, "_buyBtnActive"),
                $"{upgrade}: switching back restores the unlocked state");
            long balance = game.Impression - game.GetUpgradeCost(upgrade);
            Click(shop, Read<Rect2>(shop, "_buyBtnRect"), -101);
            await Frames(4);
            Check(game.Has(upgrade) && game.Impression == balance && !Read<bool>(shop, "_buyBtnActive"),
                $"{upgrade}: unlocked mouse purchase succeeds once");
            await Shot(upgrade + "_purchased");
            shop.QueueFree();
            await Frames(3);
        }
    }

    private async Task CheckNavigation(GameManager game)
    {
        game.ResetPersistent();
        game.CompleteStage("akari");
        game.TrainingSetImpression(3000);
        var shop = GD.Load<PackedScene>("res://Shop.tscn").Instantiate<Shop>();
        GetTree().Root.AddChild(shop);
        GetTree().CurrentScene = shop;
        Write(shop, "_t", 3.0);
        await Frames(5);
        Click(shop, new Rect2(373, 31, 177, 38), -103);
        Check(game.SelectedJob == Job.Melee, "customer button switches to unlocked Akari");
        Write(shop, "_sel", GameManager.Upgrades.Length - 1);
        long balance = 3000 - game.GetUpgradeCost("n_slow");
        Call(shop, "OnConfirm");
        Check(game.HasSpecial && game.Impression == balance, "Akari can buy her special first");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Tab, Pressed = true });
        Input.FlushBufferedEvents();
        shop._Process(1.0 / 60);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Tab, Pressed = false });
        Input.FlushBufferedEvents();
        shop._Process(1.0 / 60);
        Check(game.SelectedJob == Job.Tank && !game.HasSpecial && game.Impression == balance, "Tab returns to Mina with a shared wallet and her own upgrades");
        game.TrainingSetImpression(5000);
        Write(shop, "_sel", 0);
        Input.ActionPress("ui_down");
        shop._Process(1.0 / 60);
        Input.ActionRelease("ui_down");
        shop._Process(1.0 / 60);
        Check(Read<int>(shop, "_sel") == 1, "keyboard changes selection");
        var row = (Rect2)typeof(Shop).GetMethod("RowRect", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { 0 })!;
        Click(shop, row, 0);
        Check(Read<int>(shop, "_sel") == 0 && game.Impression == 5000, "first row click selects without purchasing");
        await Frames(2);
        balance = 5000 - game.GetUpgradeCost("n_life_1");
        Click(shop, Read<Rect2>(shop, "_buyBtnRect"), -101);
        Check(game.GetUpgradeLevel("n_life_1") == 1 && game.Impression == balance, "purchase button works with mouse");
        await Frames(2);
        Input.ActionPress("ui_down");
        shop._Process(1.0 / 60);
        Input.ActionRelease("ui_down");
        shop._Process(1.0 / 60);
        balance -= game.GetUpgradeCost("n_dodge");
        Input.ActionPress("ui_accept");
        shop._Process(1.0 / 60);
        Input.ActionRelease("ui_accept");
        shop._Process(1.0 / 60);
        Check(game.GetUpgradeLevel("n_dodge") == 1 && game.Impression == balance, "keyboard purchase works");
        await Frames(2);
        // 2026-09-27：T（トレーニングへ）の文字キーは廃止。→ で [ためし撃ち] にフォーカスを移して Z で行く。
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.T, Pressed = true });
        Input.FlushBufferedEvents();
        shop._Process(1.0 / 60);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.T, Pressed = false });
        Input.FlushBufferedEvents();
        shop._Process(1.0 / 60);
        await Frames(3);
        Check(GetTree().CurrentScene == shop, "T no longer opens practice");
        Input.ActionPress("ui_right");
        shop._Process(1.0 / 60);
        Input.ActionRelease("ui_right");
        shop._Process(1.0 / 60);
        Check(shop.Action == 1 && Read<int>(shop, "_sel") == 1, "right moves focus to the practice button without changing the row");
        Input.ActionPress("ui_left");
        shop._Process(1.0 / 60);
        Input.ActionRelease("ui_left");
        shop._Process(1.0 / 60);
        Check(shop.Action == 0, "left returns focus to the purchase button");
        Click(shop, Read<Rect2>(shop, "_trainBtnRect"), -102);
        await WaitForScene<TrainingRoot>();
        Check(GetTree().CurrentScene is TrainingRoot, "training button opens practice");
        GetTree().ChangeSceneToFile("res://Shop.tscn");
        await Frames(5);
        shop = (Shop)GetTree().CurrentScene;
        Check(game.Impression == balance && game.GetUpgradeLevel("n_dodge") == 1, "practice round trip preserves purchases");
        Write(shop, "_t", 3.0);
        Click(shop, Read<Rect2>(shop, "_backBtnRect"), -100);
        Check(Read<bool>(shop, "_exitPending"), "home button keeps farewell dialogue");
        Write(shop, "_exitDelayT", 0.0);
        await WaitForScene<Hub>();
        Check(GetTree().CurrentScene is Hub, "home button returns to smartphone home");
        GetTree().CurrentScene.QueueFree();
        await Frames(3);
    }

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string? output = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--output="));
        string dir = ProjectSettings.GlobalizePath(output == null ? "res://build/qa_story/shop_style/shots" : output[9..]);
        DirAccess.MakeDirRecursiveAbsolute(dir);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{dir}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
