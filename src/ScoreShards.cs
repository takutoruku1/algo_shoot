using Godot;
using System.Collections.Generic;

public partial class ScoreShards : Node2D
{
    public const float Lifetime = 5f;
    public const float MagnetRadius = 48f;
    private const float ScatterTime = 0.24f;
    private const float CollectRadius = 6f;
    // 256→640：ボス撃破で 104〜128 粒が一度に入る（+道中の取り残し）。上限に当たると
    // Add が false を返して「ただのパーティクル＝拾えない花びら」に落ちるため、格の差を
    // 出すには容量が先に要る。描画は 1粒 2〜6 DrawCall の単純ループで、640 でも実測余裕（下の _Draw 参照）。
    private const int Capacity = 640;

    private sealed class Shard
    {
        public FxLayer.P Particle = null!;
        public Vector2 Position, Velocity;
        public float Age, Delay;
        public int Points;
        // 2026-09-17 経済改修：欠片はスコア(Points)とショップ通貨の基礎額(Imp)を別々に運ぶ。
        //   Imp は GameManager.AddScoreShard 経由で GainImpression の倍率を通る（難易度倍率は経済側の1本だけ）。
        public int Imp;
        public bool Attracted;
    }

    private readonly List<Shard> _shards = new();
    private GameManager _game = null!;
    private Player? _player;
    private FxLayer _fx = null!;
    private float _soundCooldown, _collectIdle;
    private int _chain;
    // 強制吸引（ラッシュ）の残り秒。ボス/中ボス撃破の瞬間に FxLayer.PurifyBurst から立てる。
    // この間は (a) 距離に関係なく全欠片が吸引状態になり (b) 会話(BubblePaused)中でも動き続ける。
    // 撃破直後は必ず改心会話へ入るので、これが無いと大量に撒いた欠片が凍って寿命で消える＝損した気分になる。
    private float _rush;

    // 撃破の瞬間から duration 秒だけ全欠片を強制回収する。
    public void BeginRush(float duration) => _rush = Mathf.Max(_rush, duration);

    public override void _Ready()
    {
        ZIndex = -6;
        ZAsRelative = false;
        _game = GetNode<GameManager>("/root/Game");
        _fx = GetParent<FxLayer>();
    }

    public bool Add(FxLayer.P particle, int points, int imp = 0)
    {
        if (_shards.Count == Capacity) return false;
        _shards.Add(new Shard
        {
            Particle = particle,
            Position = ToLocal(new Vector2(particle.X, particle.Y)),
            Velocity = new Vector2(particle.Vx, particle.Vy),
            Delay = ScatterTime + particle.Ttl * 0.08f,
            Points = points,
            Imp = imp,
        });
        QueueRedraw();
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_rush > 0) _rush = Mathf.Max(0f, _rush - dt);
        // ラッシュ中は会話が始まっていても回収を走らせる（撃破の一拍を会話で殺さない）。
        bool sweep = _game.StageCleared || _rush > 0;
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

        _soundCooldown -= dt;
        _collectIdle += dt;
        if (_collectIdle > 0.3f) _chain = 0;
        Vector2 target = ToLocal(_player.GlobalPosition);
        int collected = 0, collectedImp = 0, collectedCount = 0;
        Color pickupColor = FxLayer.Heart;
        for (int i = _shards.Count - 1; i >= 0; i--)
        {
            var shard = _shards[i];
            shard.Age += dt;
            shard.Particle.Rot += shard.Particle.Spin * dt;
            Vector2 toPlayer = target - shard.Position;
            float distance = toPlayer.Length();
            // ラッシュ中は散りの一拍（Delay）を半分に詰める＝「咲いて → すぐ吸い込まれる」テンポ。
            float delay = _rush > 0 ? shard.Delay * 0.5f : shard.Delay;
            if (shard.Age >= delay && (sweep || distance <= MagnetRadius))
                shard.Attracted = true;

            if (shard.Attracted)
            {
                // ラッシュ中だけ上限を 430→620 に上げる。ボス撃破は 100粒超が最大200px先から
                // 集まるので、据え置きだと尾が長く伸びて会話に食い込む（テンポ §3）。
                float cap = _rush > 0 ? 620f : 430f;
                float speed = Mathf.Min(cap, Mathf.Max(95f, shard.Velocity.Length()) + 1000f * dt);
                if (distance <= CollectRadius + speed * dt)
                {
                    collected += shard.Points;
                    collectedImp += shard.Imp;
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
            _game.AddScoreShard(collected, collectedImp);
            _collectIdle = 0;
            if (_soundCooldown <= 0)
            {
                Audio.Instance?.PlayScorePickup(_chain++);
                _fx.ScorePickup(_player.GlobalPosition, pickupColor);
                // ラッシュ（ボス/中ボス撃破）中は間隔を詰めて連鎖を密に鳴らす（0.065→0.045s）。
                // 0.045s＝約22音/秒。これ以上詰めると単音が潰れて「ノイズ」になる境界。
                _soundCooldown = _rush > 0 ? 0.045f : 0.065f;
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        // 尾を引く線のアンチエイリアスは 1本あたりのコストが高い。ボス撃破ラッシュでは
        // 100本超が同時に尾を引くので、粒が多いフレームだけ AA を落とす（見た目の差はほぼ無い／
        // フレーム落ちのほうが手触りを壊す＝§11 完成度は面白さの前提）。
        bool aa = _shards.Count <= 96;
        foreach (var shard in _shards)
        {
            float alpha = shard.Attracted ? 1f : Mathf.Clamp((Lifetime - shard.Age) / 0.7f, 0, 1);
            var particle = shard.Particle;
            var position = shard.Position;
            if (!shard.Attracted) position.Y += Mathf.Sin(shard.Age * 4f + shard.Delay * 10f) * 1.2f;
            // 尾は「速く飛んでいる」ものにだけ。低速の粒に短い線を足しても見えず、描画だけ増える。
            if (shard.Attracted && shard.Velocity.LengthSquared() > 3600f)
            {
                Vector2 tail = -shard.Velocity.Normalized() * Mathf.Min(12f, shard.Velocity.Length() * 0.03f);
                DrawLine(position, position + tail, new Color(particle.Col, 0.4f), 0.8f, aa);
            }
            particle.X = position.X;
            particle.Y = position.Y;
            particle.Life = (1f - alpha) * particle.Ttl;
            FxLayer.DrawP(this, particle, null);
        }
    }
}
