using Godot;
using System.Collections.Generic;

internal sealed class AutoplayInput
{
    private const float Horizon = 0.5f;
    private const int Steps = 8;
    private const int Directions = 16;
    private const float ComfortGap = 8f;
    private readonly SceneTree _tree;
    private readonly GameManager _game;
    private readonly List<(Vector2 pos, Vector2 vel, float radius)> _threats = new();
    private Player? _player;
    private bool _combat, _zDown, _shiftDown, _xDown, _dodgeDown;
    private double _dialogTime, _chargeDelay, _bombDelay;
    private int _previousDirection;

    public AutoplayInput(SceneTree tree, GameManager game)
    {
        _tree = tree;
        _game = game;
    }

    public void Update(double delta, bool shoot = true, bool bombs = true, bool dodge = true)
    {
        UpdateInputs(delta, shoot, bombs, dodge);
        // 合成イベントを次の描画まで溜めず、同じ物理フレームの Player に届ける。
        Input.FlushBufferedEvents();
    }

    private void UpdateInputs(double delta, bool shoot, bool bombs, bool dodge)
    {
        var player = _tree.GetFirstNodeInGroup("player") as Player;
        var hud = _tree.GetFirstNodeInGroup("hud") as Hud;
        bool talking = Hud.BubblePaused || hud?.CinematicMode == true || player == null;
        bool combat = !talking && player!.Lives > 0;
        if (player != _player)
        {
            Release();
            _player = player;
        }
        _bombDelay = System.Math.Max(0, _bombDelay - delta);
        if (_xDown) SetKey(Key.X, false, ref _xDown);
        if (_dodgeDown) SetKey(Key.Space, false, ref _dodgeDown);

        if (!combat)
        {
            Move(Vector2.Zero);
            SetKey(Key.Shift, false, ref _shiftDown);
            _combat = false;
            _chargeDelay = 0;
            _dialogTime = (_dialogTime + delta) % 0.12;
            SetKey(Key.Z, talking && _dialogTime < 0.06, ref _zDown);
            return;
        }
        if (!_combat)
        {
            // 会話を送った Z/Shift を一度離し、Player の入力持ち越し防止を解除する。
            Release();
            _combat = true;
            return;
        }

        SetKey(Key.Shift, shoot && (!_shiftDown || player!.LockArmed), ref _shiftDown);
        if (player!.Dodging)
        {
            Move(Vector2.Zero);
            SetKey(Key.Z, false, ref _zDown);
            return;
        }
        float gap = DriveMovement(player!, (float)delta);
        if (dodge && player.DodgeReady && gap < 4f)
        {
            Move(DodgeDirection(player.GlobalPosition));
            SetKey(Key.Z, false, ref _zDown);
            SetKey(Key.Space, true, ref _dodgeDown);
            _chargeDelay = 0.1;
            return;
        }
        _chargeDelay -= delta;
        bool ready = _game.HasChargeTier2 ? player!.ChargeFull2 : player!.ChargeFull;
        if (!shoot || !player!.LockedOn || (ready || player.ChargeFull && gap < ComfortGap))
        {
            if (_zDown) _chargeDelay = 0.35;
            SetKey(Key.Z, false, ref _zDown);
        }
        else if (_chargeDelay <= 0)
            SetKey(Key.Z, true, ref _zDown);

        if (bombs && gap < 1f && _game.Bombs > 0 && _bombDelay <= 0)
        {
            SetKey(Key.X, true, ref _xDown);
            _bombDelay = 2;
        }
    }

    public void Release()
    {
        Move(Vector2.Zero);
        SetKey(Key.Z, false, ref _zDown);
        SetKey(Key.Shift, false, ref _shiftDown);
        SetKey(Key.X, false, ref _xDown);
        SetKey(Key.Space, false, ref _dodgeDown);
        _combat = false;
        _dialogTime = 0;
        _chargeDelay = 0;
        _previousDirection = 0;
        Input.FlushBufferedEvents();
    }

    private float DriveMovement(Player player, float delta)
    {
        var position = player.GlobalPosition;
        float speed = player.MoveSpeed;
        if (player.ReturningToStart)
        {
            Move(Vector2.Zero);
            return float.MaxValue;
        }
        _threats.Clear();
        foreach (Node node in _tree.GetNodesInGroup("enemy_bullets"))
            if (node is Bullet bullet && bullet.Active && position.DistanceSquaredTo(bullet.GlobalPosition) < 150f * 150f)
                _threats.Add((bullet.GlobalPosition, bullet.Velocity * GameManager.EnemyTimeScale, bullet.Radius));

        Enemy? nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (Node node in _tree.GetNodesInGroup("enemies"))
        {
            if (node is not Enemy enemy || enemy.IsPurified) continue;
            float distance = position.DistanceSquaredTo(enemy.GlobalPosition);
            if (distance < 150f * 150f) _threats.Add((enemy.GlobalPosition, Vector2.Zero, 14f));
            if (distance < nearestDistance) { nearest = enemy; nearestDistance = distance; }
        }

        var aim = player.LockTarget ?? nearest;
        var home = Clamp(new Vector2(Field.PlayerStartX, aim?.GlobalPosition.Y ?? Field.PlayerStartY));
        float bestScore = float.NegativeInfinity;
        float bestGap = float.NegativeInfinity;
        Vector2 bestVelocity = Vector2.Zero;
        int bestDirection = 0;

        void Consider(Vector2 velocity, int direction, float stopTime = Horizon)
        {
            float gap = Clearance(position, velocity, stopTime);
            bestGap = Mathf.Max(bestGap, gap);
            float score = Mathf.Min(gap, 24f);
            if (gap >= ComfortGap)
            {
                Vector2 end = Clamp(position + velocity * Mathf.Min(Horizon, stopTime));
                score += 8f * (1f - Mathf.Clamp(end.DistanceTo(home) / 120f, 0f, 1f));
            }
            if (direction == _previousDirection) score += 0.12f;
            if (direction == 0) score += 0.08f;
            if (score > bestScore)
            {
                bestScore = score;
                bestVelocity = velocity;
                bestDirection = direction;
            }
        }

        Consider(Vector2.Zero, 0);
        for (int i = 0; i < Directions; i++)
            Consider(Vector2.FromAngle(Mathf.Tau * i / Directions) * speed, i + 1);
        Vector2 toHome = home - position;
        if (toHome.LengthSquared() > 0.25f)
            Consider((toHome / delta).LimitLength(speed), Directions + 1, Mathf.Max(delta, toHome.Length() / speed));
        _previousDirection = bestDirection;
        Move(bestVelocity / speed);
        return bestGap;
    }

    private float Clearance(Vector2 position, Vector2 velocity, float stopTime)
    {
        float gap = float.MaxValue;
        for (int step = 0; step <= Steps; step++)
        {
            float time = Horizon * step / Steps;
            Vector2 playerPosition = Clamp(position + velocity * Mathf.Min(time, stopTime));
            foreach (var threat in _threats)
            {
                float distance = playerPosition.DistanceTo(threat.pos + threat.vel * time) - threat.radius - 2f;
                gap = Mathf.Min(gap, distance);
                if (gap < -4f) return gap;
            }
        }
        return gap;
    }

    private Vector2 DodgeDirection(Vector2 position)
    {
        Vector2 best = Vector2.Zero;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i <= Directions; i++)
        {
            Vector2 direction = i == Directions ? Vector2.Zero : Vector2.FromAngle(Mathf.Tau * i / Directions);
            Vector2 end = Clamp(position + direction * _game.DodgeDistance);
            float gap = 9999f;
            foreach (var threat in _threats)
                for (int step = 0; step < 6; step++)
                    gap = Mathf.Min(gap, end.DistanceTo(threat.pos + threat.vel * (0.45f + step * 0.05f)) - threat.radius - 2f);
            float score = Mathf.Min(gap, 48f) - end.DistanceTo(Field.PlayerStart) * 0.02f;
            if (score > bestScore) { bestScore = score; best = direction; }
        }
        return best;
    }

    private static Vector2 Clamp(Vector2 position) => new(
        Mathf.Clamp(position.X, Field.Left, Field.Right),
        Mathf.Clamp(position.Y, Field.Top, Field.Bottom));

    internal static void Move(Vector2 direction)
    {
        float length = Mathf.Min(direction.Length(), 1f);
        if (length > 0)
        {
            // GetVector の円形デッドゾーンを逆算し、近距離の弱い入力も指定速度で届かせる。
            float deadzone = (InputMap.ActionGetDeadzone("ui_left") + InputMap.ActionGetDeadzone("ui_right")
                + InputMap.ActionGetDeadzone("ui_up") + InputMap.ActionGetDeadzone("ui_down")) / 4f;
            direction = direction.Normalized() * (deadzone + (1f - deadzone) * length);
        }
        Axis("ui_left", "ui_right", direction.X);
        Axis("ui_up", "ui_down", direction.Y);
    }

    private static void Axis(string negative, string positive, float value)
    {
        Input.ParseInputEvent(new InputEventAction { Action = positive, Pressed = value > 0, Strength = Mathf.Max(0, value) });
        Input.ParseInputEvent(new InputEventAction { Action = negative, Pressed = value < 0, Strength = Mathf.Max(0, -value) });
    }

    private static void SetKey(Key key, bool down, ref bool held)
    {
        if (held == down) return;
        held = down;
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = down });
    }
}
