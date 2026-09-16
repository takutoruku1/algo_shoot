using Godot;
using System.Collections.Generic;

public partial class ScoreShards : Node2D
{
    public const float Lifetime = 5f;
    public const float MagnetRadius = 48f;
    private const float ScatterTime = 0.24f;
    private const float CollectRadius = 6f;
    private const int Capacity = 256;

    private sealed class Shard
    {
        public FxLayer.P Particle = null!;
        public Vector2 Position, Velocity;
        public float Age, Delay;
        public int Points;
        public bool Attracted;
    }

    private readonly List<Shard> _shards = new();
    private GameManager _game = null!;
    private Player? _player;
    private FxLayer _fx = null!;
    private float _soundCooldown, _collectIdle;
    private int _chain;

    public override void _Ready()
    {
        ZIndex = -6;
        ZAsRelative = false;
        _game = GetNode<GameManager>("/root/Game");
        _fx = GetParent<FxLayer>();
    }

    public bool Add(FxLayer.P particle, int points)
    {
        if (_shards.Count == Capacity) return false;
        _shards.Add(new Shard
        {
            Particle = particle,
            Position = ToLocal(new Vector2(particle.X, particle.Y)),
            Velocity = new Vector2(particle.Vx, particle.Vy),
            Delay = ScatterTime + particle.Ttl * 0.08f,
            Points = points,
        });
        QueueRedraw();
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        bool sweep = _game.StageCleared;
        Visible = !Hud.BubblePaused || sweep;
        if (!Visible) return;
        if (!IsInstanceValid(_player))
            _player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (_player == null) return;
        if (_player.Lives <= 0)
        {
            _shards.Clear();
            QueueRedraw();
            return;
        }

        float dt = (float)delta;
        _soundCooldown -= dt;
        _collectIdle += dt;
        if (_collectIdle > 0.3f) _chain = 0;
        Vector2 target = ToLocal(_player.GlobalPosition);
        int collected = 0, collectedCount = 0;
        Color pickupColor = FxLayer.Heart;
        for (int i = _shards.Count - 1; i >= 0; i--)
        {
            var shard = _shards[i];
            shard.Age += dt;
            shard.Particle.Rot += shard.Particle.Spin * dt;
            Vector2 toPlayer = target - shard.Position;
            float distance = toPlayer.Length();
            if (shard.Age >= shard.Delay && (sweep || distance <= MagnetRadius))
                shard.Attracted = true;

            if (shard.Attracted)
            {
                float speed = Mathf.Min(430f, Mathf.Max(95f, shard.Velocity.Length()) + 1000f * dt);
                if (distance <= CollectRadius + speed * dt)
                {
                    collected += shard.Points;
                    collectedCount++;
                    pickupColor = shard.Particle.Col;
                    _shards.RemoveAt(i);
                    continue;
                }
                shard.Velocity = shard.Velocity.MoveToward(toPlayer / distance * speed, 2200f * dt);
                shard.Position += shard.Velocity * dt;
            }
            else
            {
                if (shard.Age >= Lifetime)
                {
                    _shards.RemoveAt(i);
                    continue;
                }
                if (shard.Age <= ScatterTime)
                {
                    shard.Velocity.Y += shard.Particle.Grav * dt;
                    shard.Velocity *= Mathf.Max(0f, 1f - shard.Particle.Drag * dt);
                }
                else shard.Velocity *= Mathf.Exp(-5f * dt);
                shard.Position += shard.Velocity * dt;
                if (shard.Age > 0.5f) shard.Position.X -= 5f * dt;
                var global = ToGlobal(shard.Position);
                global.X = Mathf.Clamp(global.X, Field.Left + 5f, Field.Right - 5f);
                global.Y = Mathf.Clamp(global.Y, Field.Top + 19f, Field.Bottom - 9f);
                shard.Position = ToLocal(global);
            }
        }
        if (collectedCount > 0)
        {
            _game.AddScoreShard(collected);
            _collectIdle = 0;
            if (_soundCooldown <= 0)
            {
                Audio.Instance?.PlayScorePickup(_chain++);
                _fx.ScorePickup(_player.GlobalPosition, pickupColor);
                _soundCooldown = 0.065f;
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var shard in _shards)
        {
            float alpha = shard.Attracted ? 1f : Mathf.Clamp((Lifetime - shard.Age) / 0.7f, 0, 1);
            var particle = shard.Particle;
            var position = shard.Position;
            if (!shard.Attracted) position.Y += Mathf.Sin(shard.Age * 4f + shard.Delay * 10f) * 1.2f;
            if (shard.Attracted && shard.Velocity.LengthSquared() > 1f)
            {
                Vector2 tail = -shard.Velocity.Normalized() * Mathf.Min(12f, shard.Velocity.Length() * 0.03f);
                DrawLine(position, position + tail, new Color(particle.Col, 0.4f), 0.8f, true);
            }
            particle.X = position.X;
            particle.Y = position.Y;
            particle.Life = (1f - alpha) * particle.Ttl;
            FxLayer.DrawP(this, particle, null);
        }
    }
}
