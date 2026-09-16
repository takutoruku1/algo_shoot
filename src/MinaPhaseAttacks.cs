using Godot;
using System.Collections.Generic;

public partial class MinaPhaseAttacks : Node
{
    private BossMina _boss = null!;
    private Node _world = null!;
    private readonly List<AreaStrike> _strikes = new();
    private int _phase, _wave;
    private double _wait, _cooldown;
    private Vector2 _lastSafe;
    public bool Active { get; private set; }
    public bool OpenerCompleted { get; private set; }

    public void Configure(BossMina boss, Node world)
    {
        _boss = boss;
        _world = world;
        BeginPhase(0);
    }

    public void BeginPhase(int phase)
    {
        CancelPendingAttacks();
        _phase = phase;
        OpenerCompleted = false;
        _cooldown = 1.2;
    }

    public void CancelPendingAttacks()
    {
        foreach (var strike in _strikes)
            if (IsInstanceValid(strike)) strike.QueueFree();
        _strikes.Clear();
        Active = false;
        _wave = 0;
        _wait = 0;
        _cooldown = 3;
        if (IsInstanceValid(_boss) && !_boss.IsPurified) _boss.SetBodyContactEnabled(true);
    }

    public override void _Process(double delta)
    {
        if (_boss.IsPurified) { CancelPendingAttacks(); return; }
        if (Hud.BubblePaused || _boss.Transitioning) return;
        delta = GameManager.EnemyDelta(delta);
        if (!Active)
        {
            _cooldown -= delta;
            if (_cooldown > 0) return;
            Active = true;
            _wave = 0;
            _wait = 0.8;
            _lastSafe = Vector2.Zero;
            GetNode<BulletPool>("/root/Pool").DespawnAll();
            _boss.SetBodyContactEnabled(false);
            _boss.ShowSignaturePose();
            (GetTree().GetFirstNodeInGroup("hud") as Hud)?.AnnounceSpell(
                "ミナ", BossHandles.MinaBattle, SignatureName(_phase), BossMina.PhaseTint(_phase));
            return;
        }
        _strikes.RemoveAll(s => !IsInstanceValid(s) || s.IsQueuedForDeletion());
        if (_strikes.Count > 0) return;
        _wait -= delta;
        if (_wait > 0) return;
        if (_wave >= WaveCount(_phase))
        {
            Active = false;
            OpenerCompleted = true;
            _cooldown = _phase == 4 ? 6 : 9;
            _boss.SetBodyContactEnabled(true);
            return;
        }
        SpawnWave();
        _wave++;
        _wait = 0.35;
    }

    public static string SignatureName(int phase) => phase switch
    {
        0 => "消せなかった声",
        1 => "未送信、再送",
        2 => "拍手の檻",
        3 => "閉じない配信",
        _ => "リフレイン・帰り道",
    };

    private static int WaveCount(int phase) => phase switch { 0 => 1, 4 => 7, _ => 3 };
    private Player Player => (Player)GetTree().GetFirstNodeInGroup("player");
    private GameManager.Diff Difficulty => GetNode<GameManager>("/root/Game").Difficulty;
    private float WarnMul => Difficulty switch
    {
        GameManager.Diff.Easy => 1.3f, GameManager.Diff.Hard => 0.9f,
        GameManager.Diff.Lunatic => 0.8f, _ => 1f,
    };

    // The current game runs at 75 px/s before job/lock modifiers; warnings must use that speed.
    private double TravelWarning(float distance, float minimum)
        => Mathf.Max(minimum * WarnMul, Mathf.Max(0, distance) / Player.SlowestMoveSpeed + 0.45f);

    private void SpawnWave()
    {
        int kind = _phase == 4 ? (_wave < 2 ? 1 : _wave < 4 ? 2 : 3) : _phase;
        var motif = kind switch
        {
            1 => AreaStrike.Motif.Rain, 2 => AreaStrike.Motif.Screen,
            3 => AreaStrike.Motif.Stream, _ => AreaStrike.Motif.Data,
        };
        if (kind <= 1) SpawnLock(motif);
        else if (kind == 2) SpawnCurtain(motif);
        else SpawnRelay(_phase == 4 ? AreaStrike.Motif.Data : motif);
        _boss.ShowSignaturePose();
    }

    private void Add(AreaStrike strike, Vector2 position)
    {
        strike.SetOwner(_boss);
        _world.AddChild(strike);
        strike.GlobalPosition = position;
        _strikes.Add(strike);
    }

    private void SpawnLock(AreaStrike.Motif motif)
    {
        float radius = _phase == 0 ? 22f : 28f;
        var zone = new AreaStrike();
        zone.Configure(AreaStrike.Shape.Circle, radius, radius,
            TravelWarning(radius + 5, 1.2f), BossMina.PhaseTint(_phase), Colors.White, motif);
        Add(zone, Player.GlobalPosition);
    }

    private void SpawnCurtain(AreaStrike.Motif motif)
    {
        bool vertical = _wave % 2 == 0;
        float halfGap = Difficulty == GameManager.Diff.Easy ? 32f : 26f;
        float low = vertical ? Field.Left : Field.Top;
        float high = vertical ? Field.Right : Field.Bottom;
        float axis = vertical ? Player.GlobalPosition.X : Player.GlobalPosition.Y;
        float center = (low + high) * 0.5f;
        float gap = Mathf.Clamp(axis + (axis < center ? 78 : -78), low + halfGap + 12, high - halfGap - 12);
        double warn = TravelWarning(Mathf.Abs(gap - axis) - halfGap + 6, 1.5f);
        AddCurtain(low, gap - halfGap, vertical, warn, motif);
        AddCurtain(gap + halfGap, high, vertical, warn, motif);
    }

    private void AddCurtain(float start, float end, bool vertical, double warn, AreaStrike.Motif motif)
    {
        var zone = new AreaStrike();
        zone.Configure(AreaStrike.Shape.Rect,
            vertical ? (end - start) * 0.5f : Field.Width * 0.5f,
            vertical ? Field.Height * 0.5f : (end - start) * 0.5f,
            warn, BossMina.PhaseTint(_phase), Colors.White, motif);
        Add(zone, vertical ? new Vector2((start + end) * 0.5f, Field.CenterY)
            : new Vector2(Field.CenterX, (start + end) * 0.5f));
    }

    private void SpawnRelay(AreaStrike.Motif motif)
    {
        float radius = Difficulty switch
        {
            GameManager.Diff.Easy => 36f, GameManager.Diff.Hard => 28f,
            GameManager.Diff.Lunatic => 26f, _ => 32f,
        };
        Vector2 safe = PickSafe(radius);
        double warn = TravelWarning(Player.GlobalPosition.DistanceTo(safe) - radius + 8, 1.6f);
        var zone = new AreaStrike();
        zone.ConfigureFullscreen(safe, radius, warn, BossMina.PhaseTint(_phase), Colors.White, motif);
        Add(zone, Vector2.Zero);
        _lastSafe = safe;
    }

    private Vector2 PickSafe(float radius)
    {
        Vector2 player = Player.GlobalPosition;
        Vector2 best = new(Field.CenterX, Field.CenterY);
        float bestDistance = float.MaxValue;
        float margin = radius + 10;
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 5; col++)
            {
                var point = new Vector2(Mathf.Lerp(Field.Left + margin, Field.Right - margin, col / 4f),
                    Mathf.Lerp(Field.Top + margin, Field.Bottom - margin, row / 2f));
                float distance = player.DistanceTo(point);
                if (distance <= radius + 24 || distance >= bestDistance) continue;
                if (_lastSafe != Vector2.Zero && point.DistanceTo(_lastSafe) < radius * 2 + 8) continue;
                best = point;
                bestDistance = distance;
            }
        return best;
    }

    public override void _ExitTree()
    {
        foreach (var strike in _strikes)
            if (IsInstanceValid(strike)) strike.QueueFree();
    }
}
