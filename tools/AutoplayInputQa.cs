using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

public partial class AutoplayInputQa : Node
{
    private AutoplayInput _input = null!;
    private Player _player = null!;
    private Hud _hud = null!;
    private BulletPool _pool = null!;
    private bool _drive, _shoot = true, _lastZ, _bombs;
    private int _maxCharge, _dialogEdges;
    private double _time, _firstCharge = -1;

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.ResetPersistent();
            game.Difficulty = GameManager.Diff.Normal;
            _pool = GetNode<BulletPool>("/root/Pool");
            _input = new AutoplayInput(GetTree(), game);
            _player = new Player { Position = Field.PlayerStart };
            _hud = new Hud();
            AddChild(_player);
            AddChild(_hud);
            var enemy = new MidEnemy();
            enemy.Configure(EnemyTable.For(StageTheme.Akari).Item1);
            enemy.SetEntry(new Vector2(300, 96));
            AddChild(enemy);
            enemy.ProcessMode = ProcessModeEnum.Disabled;
            enemy.GlobalPosition = new Vector2(300, 96);
            enemy.CollisionLayer = 0;
            enemy.CollisionMask = 0;
            foreach (var panel in enemy.GetChildren().OfType<Panel>())
            {
                panel.CollisionLayer = 0;
                panel.CollisionMask = 0;
            }
            await Frames(3);

            var smallMove = new Vector2(0.1f, -0.15f);
            AutoplayInput.Move(smallMove);
            Input.FlushBufferedEvents();
            Check(Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down").DistanceTo(smallMove) < 0.001f,
                $"small diagonal inputs survive the real input deadzone ({Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down")})");
            _input.Release();
            _drive = true;
            _time = 0;
            await Frames(72);
            Check(_player.LockedOn && _player.LockTarget == enemy, "locks on through Shift input");
            Check(Mathf.Abs(_player.GlobalPosition.Y - enemy.GlobalPosition.Y) < 4f,
                $"tracks a target 12px away without stalling (y={_player.GlobalPosition.Y:0.0})");
            Check(_maxCharge == ChargeTier.First && _firstCharge <= 1.1,
                $"fires a first-tier charge through Z within 1.1s (t={_firstCharge:0.00})");
            Check(!game.HasChargeTier2 && game.ExtraLines == 0 && game.ShotPowerMul == 1,
                "autoplay does not grant upgrades");

            _hud.SetCinematicMode(true);
            await Frames(3);
            _dialogEdges = 0;
            await Frames(30);
            Check(Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down") == Vector2.Zero
                && !Input.IsKeyPressed(Key.Shift), "dialogue releases movement and lock");
            Check(_dialogEdges >= 3, $"dialogue receives repeated advance edges ({_dialogEdges} in 0.5s)");
            _hud.SetCinematicMode(false);
            _hud.HideBubble();
            _maxCharge = 0;
            _pool.DespawnPlayerBullets();
            await Frames(85);
            Check(_player.LockedOn && _maxCharge == ChargeTier.First, "resumes lock and charge after dialogue");

            game.TrainingSetUpgrade("n_charge", true);
            _maxCharge = 0;
            await Frames(130);
            Check(_maxCharge == ChargeTier.Second, "uses second-tier charge only when owned");

            _shoot = false;
            await Frames(3);
            Check(!Input.IsKeyPressed(Key.Z) && !Input.IsKeyPressed(Key.Shift), "noshoot releases charge and lock");

            int dodges = _player.DodgeCount;
            var danger = _pool.Spawn(_player.GlobalPosition + Vector2.Down, Vector2.Zero, true, 4f, 1);
            danger.CollisionLayer = 0;
            danger.CollisionMask = 0;
            await Frames(3);
            Check(_player.DodgeCount == dodges && !game.HasDodge, "does not dodge before the skill is owned");
            game.TrainingSetUpgrade("n_dodge", true);
            danger.GlobalPosition = _player.GlobalPosition + Vector2.Down;
            _bombs = true;
            int bombs = game.Bombs;
            await Frames(2);
            Check(_player.DodgeCount == dodges + 1 && _player.Dodging, "imminent danger triggers a real Space dodge");
            Check(game.Bombs == bombs, "dodge takes priority over a panic bomb");
            _pool.Despawn(danger);
            await Frames(20);
            Check(!Input.IsKeyPressed(Key.Space) && _player.DodgeCount == dodges + 1, "releases Space and respects dodge cooldown");
            _hud.SetCinematicMode(true);
            await Frames(5);
            Check(!Input.IsKeyPressed(Key.Space), "dialogue does not receive a dodge press");
            _drive = false;
            _input.Release();
            Check(Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down") == Vector2.Zero,
                "stopping the pilot releases its movement");
            GD.Print("[AutoplayQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError($"[AutoplayQA] FAIL {error}");
            GetTree().Quit(1);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_drive) return;
        _time += delta;
        _input.Update(delta, shoot: _shoot, bombs: _bombs);
        bool z = Input.IsKeyPressed(Key.Z);
        if (Hud.BubblePaused && z && !_lastZ) _dialogEdges++;
        _lastZ = z;
        foreach (var bullet in _pool.GetChildren().OfType<Bullet>())
        {
            if (!bullet.Active || !bullet.Charged) continue;
            _maxCharge = Math.Max(_maxCharge, bullet.ChargeStage);
            if (_firstCharge < 0) _firstCharge = _time;
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print($"[AutoplayQA] PASS {message}");
    }
}
