using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class TrainingQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameManager _game = null!;
    private BulletPool _pool = null!;
    private TrainingRoot _root = null!;
    private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Write(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private Player Player => Read<Player>(_root, "_player");
    private TrainingDummy Dummy => Read<TrainingDummy>(_root, "_dummy");
    private Bullet[] Shots => _pool.GetChildren().OfType<Bullet>().Where(b => b.Active && !b.IsEnemy).ToArray();

    private static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception(text);
        GD.Print($"[TrainingQA] PASS {text}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save directory");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            DisplayServer.WindowSetPosition(new Vector2I(-16000, -16000));
            _game = GetNode<GameManager>("/root/Game");
            _pool = GetNode<BulletPool>("/root/Pool");
            _game.ResetPersistent();
            _game.SelectedJob = Job.Melee;
            _game.TrainingSetImpression(2345);
            _game.AddFollowers(432);
            for (int i = 0; i < 3; i++) _game.TrainingSetUpgrade(GameManager.Upgrades[i].Id, true);
            var before = _game.SnapshotMeta();
            bool autoSave = _game.AutoSaveEnabled;
            var poolMode = _pool.ProcessMode;
            await Frames(2);
            _root = GD.Load<PackedScene>("res://Training.tscn").Instantiate<TrainingRoot>();
            GetTree().Root.AddChild(_root);
            GetTree().CurrentScene = _root;
            _root.SetProcess(false);
            Player.SetPhysicsProcess(false);
            Check(SameMeta(before, _game.SnapshotMeta()), "entry preserves purchased upgrades, character and currency");
            Check(!_game.AutoSaveEnabled && _game.TrainingMode && _game.TutorialNoConsume, "trial is isolated from saves and consumption");
            Check(Dummy.GetChildren().OfType<CollisionShape2D>().Count() == 1, "target has one body collision shape");
            Call(_root, "SetPanelOpen", true);
            await Frames(4);
            long pausedDamage = Read<long>(_root, "_shotDamage");
            await Frames(30);
            Check(Read<long>(_root, "_shotDamage") == pausedDamage && Shots.Length == 0, "settings pause shooting and measurement");
            await Shot("settings");

            for (int depth = 0; depth <= GameManager.Upgrades.Length; depth++)
            {
                Call(_root, "SetUpgradeDepth", depth);
                Player.SetPhysicsProcess(false);
                Check(GameManager.Upgrades.Select((def, index) => _game.Has(def.Id) == (index < depth)).All(x => x), $"shop progression {depth} respects prerequisites");
                Check(Read<int>(Player, "_optionCount") == _game.OptionSubCount && Player.Lives == _game.StartLives
                    && Mathf.IsEqualApprox(Read<float>(Player, "_hitR"), Player.HitRadius * _game.HitRadiusMul), "fixed player properties update immediately");
            }
            Write(_root, "_scroll", (float)Call(_root, "MaxScroll")!);
            await Shot("settings_last_upgrades");
            Call(_root, "ApplyPreset", 0);
            Check(before.Upgrades.OrderBy(x => x.Key).SequenceEqual(_game.SnapshotMeta().Upgrades.OrderBy(x => x.Key)), "owned preset restores the entry loadout");
            Call(_root, "ApplyPreset", 1);
            Check(GameManager.Upgrades.All(def => !_game.Has(def.Id)), "unupgraded preset");
            Call(_root, "ApplyPreset", 2);
            Check(GameManager.Upgrades.All(def => _game.Has(def.Id)), "maximum preset");
            Call(_root, "SetPanelOpen", false);
            Player.SetPhysicsProcess(false);

            await CheckDamage();
            CheckMeter();
            Call(_root, "SetJob", Job.Tank);
            Player.SetPhysicsProcess(false);
            for (int i = 0; i < 180; i++)
            {
                if (i % 8 == 0) Call(Player, "Fire");
                await Frames(1);
            }
            Check(Read<long>(_root, "_shotDamage") > 0, "real projectiles reach and damage the target");
            await Shot("shooting");
            DisplayServer.WindowSetSize(new Vector2I(960, 540));
            Call(_root, "SetPanelOpen", true);
            await Shot("settings_small");
            await CheckControls();
            Call(_root, "SetPanelOpen", true);
            _root.QueueFree();
            await Frames(5);
            Check(SameMeta(before, _game.SnapshotMeta()), "exit restores currency, followers, character, shot mode and upgrades");
            Check(_game.AutoSaveEnabled == autoSave && !_game.TrainingMode && !_game.TutorialNoConsume
                && _pool.ProcessMode == poolMode, "exit while settings are open restores global flags and bullet processing");
            Audio.Instance?.StopMusic(0);
            GD.Print("[TrainingQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[TrainingQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private async Task CheckDamage()
    {
        var live = new Enemy();
        Write(live, "PanelCount", 0);
        GetTree().Root.AddChild(live);
        live.SetPhysicsProcess(false);
        live.Position = Dummy.Position;
        var phase = typeof(Enemy).GetField("_phase", Private)!;
        phase.SetValue(live, Enum.Parse(phase.FieldType, "Exposed"));
        Write(live, "_maxHp", 10000);
        foreach (var job in Jobs.All)
        foreach (float distance in new[] { 36f, 84f, 132f })
        for (int power = 0; power <= Player.PowerLevelCap; power++)
        {
            Call(_root, "SetJob", job.Id);
            Call(_root, "SetDistance", distance);
            Call(_root, "SetPower", PowerKind.Line, power);
            Call(_root, "SetPower", PowerKind.Speed, power);
            Player.SetPhysicsProcess(false);
            Check(Player.LinePower == power && Player.SpeedPower == power, $"{job.CharacterId} distance={distance} power={power}");
            for (int tier = 0; tier <= ChargeTier.Second; tier++)
            {
                _pool.DespawnPlayerBullets();
                if (tier == 0) Call(Player, "Fire");
                else Call(Player, "FireCharge", tier);
                var sample = Shots.First(b => tier == 0 || b.Charged);
                int raw = sample.Damage, percent = sample.ShotPowerPercent;
                _pool.DespawnPlayerBullets();
                int actual = 0;
                Write(Player, "_shotPowerRemainder", 0);
                for (int n = 0; n < 10; n++)
                {
                    var bullet = Bullet(raw, percent, tier);
                    Write(live, "_hp", 10000);
                    Write(live, "_windowDamage", 0);
                    Write(live, "_bodyHitCd", 0d);
                    Call(live, "OnBodyHitByPlayerBullet", bullet);
                    actual += 10000 - Read<int>(live, "_hp");
                    _pool.DespawnPlayerBullets();
                }
                long before = Read<long>(_root, "_shotDamage");
                Write(Player, "_shotPowerRemainder", 0);
                for (int n = 0; n < 10; n++)
                {
                    Dummy.ResetMeasurement();
                    Call(Dummy, "OnHitByPlayerBullet", Bullet(raw, percent, tier));
                    _pool.DespawnPlayerBullets();
                }
                Check(Read<long>(_root, "_shotDamage") - before == actual && actual > 0,
                    $"tier {tier}: training equals live body damage across ten hits ({actual})");
            }
            await Frames(1);
        }
        live.QueueFree();
        Call(_root, "ResetTrial");
        Player.SetPhysicsProcess(false);
        var repeated = Bullet(12, 100, ChargeTier.First);
        Call(Dummy, "OnHitByPlayerBullet", repeated);
        int hits = Read<int>(_root, "_hits");
        Call(Dummy, "OnHitByPlayerBullet", repeated);
        Check(Read<int>(_root, "_hits") == hits, "piercing charge cannot hit the same target twice");
        Dummy.ResetMeasurement();
        Call(Dummy, "OnHitByPlayerBullet", Bullet(4, 100, 0));
        hits = Read<int>(_root, "_hits");
        Call(Dummy, "OnHitByPlayerBullet", Bullet(4, 100, 0));
        Check(Read<int>(_root, "_hits") == hits, "simultaneous normal hits respect the live cooldown");
        Call(Dummy, "OnHitByPlayerBullet", Bullet(12, 100, ChargeTier.First));
        Check(Read<int>(_root, "_hits") == hits + 1, "charge bypasses the normal hit cooldown");
        Call(_root, "ResetTrial");
        Player.SetPhysicsProcess(false);
        Check(Shots.Length == 0 && Read<long>(_root, "_shotDamage") == 0 && Player.LinePower == Player.PowerLevelCap,
            "reset clears old projectiles and metrics while preserving trial power levels");
    }

    private Bullet Bullet(int damage, int power, int tier)
    {
        var bullet = _pool.Spawn(new Vector2(-200, -200), Vector2.Zero, false, damage: damage);
        bullet.ShotPowerPercent = power;
        if (tier > 0) bullet.MakeCharged(_game.SelectedJob, tier);
        return bullet;
    }

    private void CheckMeter()
    {
        Call(_root, "ResetTrial");
        Player.SetPhysicsProcess(false);
        _root.SetPhysicsProcess(false);
        Call(_root, "OnDummyDamaged", 30, false);
        _root._PhysicsProcess(1);
        Check(Mathf.IsEqualApprox((float)Call(_root, "DummyDps")!, 30), "DPS starts at the first hit");
        Dummy.Purify();
        Check(Read<long>(_root, "_bombDamage") == Mathf.RoundToInt(Enemy.BombStrikeBase * _game.BombPowerMul)
            && Read<long>(_root, "_shotDamage") == 30, "bomb damage is measured separately from shooting");
        Call(_root, "SetPanelOpen", true);
        _root._PhysicsProcess(10);
        Check(Read<double>(_root, "_elapsed") == 1, "settings time does not dilute DPS");
        Call(_root, "SetPanelOpen", false);
        Player.SetPhysicsProcess(false);
        Call(_root, "OnDummyDamaged", 60, false);
        _root._PhysicsProcess(3.1);
        Check((float)Call(_root, "DummyDps")! == 0, "rolling DPS falls to zero when shots stop");
        for (int i = 0; i < 100; i++) Dummy.Purify();
        Check(!Dummy.IsPurified && Dummy.Monitoring && Dummy.IsInGroup("enemies"), "target never dies or stops accepting damage");
        _root.SetPhysicsProcess(true);
    }

    private static bool SameMeta(GameManager.MetaSnapshot a, GameManager.MetaSnapshot b)
        => a.Impression == b.Impression && a.Followers == b.Followers && a.SelectedJob == b.SelectedJob
        && a.SelectedShotMode == b.SelectedShotMode && a.Upgrades.OrderBy(x => x.Key).SequenceEqual(b.Upgrades.OrderBy(x => x.Key));

    private async Task CheckControls()
    {
        _root.SetProcess(true);
        await Press(Key.Tab);
        Check(!Read<bool>(_root, "_panelOpen"), "Tab closes settings");
        await Press(Key.Bracketleft);
        Check(GameManager.Upgrades.All(def => !_game.Has(def.Id)), "keyboard unupgraded preset");
        await Press(Key.Bracketright);
        Check(GameManager.Upgrades.All(def => _game.Has(def.Id)), "keyboard maximum preset");
        await Press(Key.Backslash);
        Check(GameManager.Upgrades.Count(def => _game.Has(def.Id)) == 3, "keyboard owned preset");
        await Press(Key.Tab);
        Check(Read<bool>(_root, "_panelOpen"), "Tab opens settings");
        await Press(Key.Pageup);
        await Press(Key.Pageup);
        Check(Read<float>(_root, "_scroll") == 0, "keyboard scroll reaches the first upgrade");
        await Press(Key.Pagedown);
        await Press(Key.Pagedown);
        Check(Read<float>(_root, "_scroll") == (float)Call(_root, "MaxScroll")!, "keyboard scroll reaches the final upgrade");
        await Press(Key.Escape);
        Check(!Read<bool>(_root, "_panelOpen") && GetTree().CurrentScene == _root, "Escape closes settings without exiting training");
        _root.SetProcess(false);
    }

    private async Task Press(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = true });
        await Frames(3);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
        await Frames(3);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--shots")) return;
        _root.GetNode<Control>("UiLayer").QueueRedraw();
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string directory = ProjectSettings.GlobalizePath("res://build/qa_story/training");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng(directory + "/" + name + ".png");
    }
}
