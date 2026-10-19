using Godot;
using System.Collections.Generic;

public enum BossShotStyle { Ring, Aimed }

// BulletPool : Autoload シングルトン (/root/Pool)。
// _Ready で 512 発を事前生成しプール（非アクティブ）。
// Spawn でプールから取り出して Activate、Despawn で Deactivate して返却。
// 生成した Bullet は Pool 自身の子として add する。
public partial class BulletPool : Node2D
{
    private readonly HashSet<Bullet> _reiBullets = new();
    private readonly List<Bullet> _reiFrameBullets = new();

    private sealed partial class ReiUpdater : Node
    {
        internal BulletPool Pool = null!;
        public override void _PhysicsProcess(double delta)
        {
            // Despawning during movement mutates the active set.
            Pool._reiFrameBullets.Clear();
            Pool._reiFrameBullets.AddRange(Pool._reiBullets);
            foreach (var bullet in Pool._reiFrameBullets) bullet.StepRei(delta);
        }
    }
    internal void TrackReiBullet(Bullet bullet) => _reiBullets.Add(bullet);
    internal void UntrackReiBullet(Bullet bullet) => _reiBullets.Remove(bullet);

    public override void _PhysicsProcess(double delta)
    {
        foreach (var bullet in _reiBullets) bullet.SyncReiCollision();
    }

    private const int PreallocCount = 512;
    public const float BossRadius = 2.2f;
    public const float BossChargeRadius = 2.6f;
    public const float BossLaneGap = Player.HitRadius * 2 + 4f;
    internal static BulletFootprint BossHitShape(Job job, bool charged) => Bullet.BossFootprint(job,
        (charged ? BossChargeRadius : BossRadius) * (job == Job.Magic ? 1.5f : 1), charged);

    // 非アクティブな弾のスタック（再利用用）
    private readonly Stack<Bullet> _free = new Stack<Bullet>();

    public override void _Ready()
    {
        // Sync collision pieces after both the player and every bullet have moved.
        ProcessPhysicsPriority = 100;
        AddChild(new ReiUpdater { Pool = this, ProcessPhysicsPriority = -1 });
        // 512 発を事前生成しプール（非アクティブ）
        for (int i = 0; i < PreallocCount; i++)
        {
            var b = CreateBullet();
            _free.Push(b);
        }
    }

    private Bullet CreateBullet()
    {
        var b = new Bullet();
        AddChild(b); // Pool 自身の子として add（_Ready 内で Deactivate される）
        return b;
    }

    // pos に vel で移動する弾を取り出して有効化。
    // プール枯渇時は追加生成して可。shape/tint で弾形・スペル色を指定（敵弾のみ反映）。
    public Bullet Spawn(Vector2 pos, Vector2 vel, bool isEnemy, float radius = 3f, int damage = 1,
        BulletShape shape = BulletShape.Orb, Color? tint = null, bool homing = false, bool backwardHoming = false)
    {
        // 難易度で敵弾の速度をスケール（やさしいほど遅く＝避けやすい）。
        if (isEnemy)
        {
            float mul = GetNodeOrNull<GameManager>("/root/Game")?.BulletSpeedMul ?? 1f;
            vel *= mul;
        }

        Bullet b;
        if (_free.Count > 0)
        {
            b = _free.Pop();
        }
        else
        {
            // 枯渇時は追加生成
            b = CreateBullet();
        }

        b.Activate(pos, vel, isEnemy, radius, damage, shape, tint, homing, backwardHoming);
        // 会話開始と同フレームの遅延発射も、表示・衝突させずに返す。
        if (Hud.BubblePaused) Despawn(b);
        return b;
    }

    public Bullet SpawnBossShot(Vector2 position, Vector2 direction, Job character, bool charged,
        BossShotStyle style = BossShotStyle.Aimed)
    {
        var job = Jobs.Get(character);
        float speed = character switch
        {
            Job.Melee => charged ? 100f : 88f,
            Job.Heal => charged ? 72f : 60f,
            Job.Magic => charged ? 72f : 62f,
            _ => charged ? 100f : 82f,
        };
        float radius = (charged ? BossChargeRadius : BossRadius) * (character == Job.Magic ? 1.5f : 1);
        var velocity = direction.Normalized() * speed;
        var bullet = Spawn(position, velocity, true, radius, 1, job.ShotShape,
            homing: job.Mode == GameManager.ShotMode.Homing);
        if (bullet.Homing) bullet.TurnRateOverride = job.HomingTurnRate;
        if (charged)
        {
            bullet.MakeCharged(character);
            if (bullet.Homing) bullet.TurnRateOverride = 240;
        }
        if (job.Mode == GameManager.ShotMode.Accel)
        {
            float mul = GameManager.Instance!.BulletSpeedMul;
            bullet.MakeAccel(0.6f * mul, speed * mul, 0.25f, charged ? 2.4f : 2.1f);
        }
        if (character == Job.Magic) bullet.MakeReiSplit(2);
        bullet.UseCharacterProjectile(character, style);
        return bullet;
    }

    internal Bullet SpawnReiFragment(Bullet source, Vector2 direction, int splitsRemaining, float offset, Vector2? target = null, bool warnedLaunch = false)
    {
        float radius = source.Radius * (splitsRemaining > 0 ? 2f / 3f : 0.62f);
        Vector2 destination = source.GlobalPosition + source.Velocity.Normalized().Rotated(Mathf.Pi / 2) * offset;
        var velocity = direction * source.Velocity.Length() * 0.9f;
        var fragment = Spawn(source.GlobalPosition, velocity, true, radius, source.Damage, source.Shape);
        // The parent's velocity already includes difficulty and any graze slowdown.
        fragment.Velocity = velocity;
        if (source.Charged) fragment.MakeCharged(Job.Magic);
        fragment.MakeReiSplit(splitsRemaining);
        fragment.BossAimTarget = target;
        fragment.BossBarrageWave = source.BossBarrageWave;
        fragment.BossBarrageId = source.BossBarrageId;
        fragment.UseCharacterProjectile(Job.Magic, source.BossStyle);
        if (source.BossPattern >= 0) fragment.UseBossPatternVisual(Job.Magic, source.BossPattern);
        fragment.DeployBossFormation(destination, edgeLaunch: true, warnedLaunch: warnedLaunch);
        return fragment;
    }

    // 画面上の全弾を一括で非アクティブ化（リスタート時のクリア用）。
    public void DespawnAll()
    {
        foreach (Node c in GetChildren())
            if (c is Bullet b && b.Active)
                Despawn(b);
    }

    // 画面上の自機弾だけを一括で非アクティブ化（戦闘終了＝ボス浄化の瞬間に残弾を片付ける用）。
    public void DespawnPlayerBullets(bool preserveCharged = false)
    {
        foreach (Node n in GetTree().GetNodesInGroup("player_bullets"))
            if (n is Bullet b && b.Active && !(preserveCharged && b.Charged))
                Despawn(b);
    }

    // 弾を非アクティブ化してプールへ返却。
    public void Despawn(Bullet b)
    {
        if (b == null)
            return;

        // 既に返却済みのものを二重に積まないようガード
        if (!b.Active)
        {
            // 念のため非アクティブ化を保証しつつ重複返却を避ける
            return;
        }

        b.Deactivate();
        _free.Push(b);
    }
}
