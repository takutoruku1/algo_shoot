using Godot;
using System;

public sealed class BossDanmaku
{
    private readonly Enemy _boss;
    private readonly int _stage;
    private double _timer;
    private int _wave, _phrase;
    private Vector2 _origin;
    private double _hold;
    private double _nativeTime;
    internal bool NativeAttackActive => _nativeTime > 0;
    private BossCrossfire? _crossfire;
    private BossNormalBarrage? _normalBarrage;
    private bool NormalBarrageActive => _normalBarrage != null && GodotObject.IsInstanceValid(_normalBarrage) && !_normalBarrage.Finished;
    private BossDodgeWave? _dodgeWave;
    internal bool DodgeWaveActive => _dodgeWave != null && GodotObject.IsInstanceValid(_dodgeWave) && !_dodgeWave.Finished;
    internal bool CrossfireActive => NormalBarrageActive || _crossfire != null && GodotObject.IsInstanceValid(_crossfire) && !_crossfire.Finished;
    public bool Holding => _wave > 0 || _hold > 0 || CrossfireActive || DodgeWaveActive
        || SpecialAttackPending() || _boss.GetTree().GetNodesInGroup("aoe").Count > 0
        || _boss.GetTree().GetNodesInGroup("boss_edge_volleys").Count > 0
        || _boss.GetTree().GetNodesInGroup("boss_animal_techniques").Count > 0;
    internal const float RingRadius = 64f;
    internal static int RingCount(int requested, BulletFootprint footprint)
    {
        float halfWidth = Mathf.Max(Mathf.Abs(footprint.Bounds.Position.Y), Mathf.Abs(footprint.Bounds.End.Y));
        float innerRadius = RingRadius + Mathf.Min(0, footprint.Bounds.Position.X);
        float pitch = halfWidth * 2 + BulletPool.BossLaneGap;
        return Math.Min(requested, Mathf.FloorToInt(Mathf.Pi / Mathf.Asin(pitch / (2 * innerRadius))));
    }

    public BossDanmaku(Enemy boss)
    {
        _boss = boss;
        _timer = boss is CameoBoss ? 0 : 0.7;
        _stage = boss switch
        {
            BossAkari => 0, BossKoharu => 1, BossRei => 2, BossHikage => 3,
            CameoBoss cameo => cameo.Theme.Fire switch
            {
                CameoFireTheme.AkariGrief => 0, CameoFireTheme.KoharuFalling => 1, _ => 2,
            },
            _ => 4,
        };
    }

    public void Tick(double delta)
    {
        if (Hud.BubblePaused) return;
        if (_boss is CameoBoss) { TickCameo(delta); return; }
        _hold = Math.Max(0, _hold - delta);
        var tree = _boss.GetTree();
        if (!_boss.IsVisibleInTree() || _boss.IsPurified || _boss.Transforming
            || !Field.Rect.HasPoint(_boss.ShotCenter) || _boss is BossMina { Transitioning: true }
            || _boss.GaugeVulnerable || _boss.GaugeReforming
            || tree.GetNodesInGroup("aoe").Count > 0
            || tree.GetNodesInGroup("boss_edge_volleys").Count > 0
            || tree.GetNodesInGroup("boss_animal_techniques").Count > 0)
        {
            _timer = GameManager.Instance!.Difficulty == GameManager.Diff.Easy ? 0.7 : 0.05;
            if (_wave > 0 || (_crossfire?.Wave ?? 0) > 0 || (_normalBarrage?.Wave ?? 0) > 0 || NativeAttackActive || _dodgeWave != null) _phrase++;
            _wave = 0;
            _hold = 0;
            _nativeTime = 0;
            CancelCrossfire();
            if (DodgeWaveActive) _dodgeWave!.Cancel();
            _dodgeWave = null;
            return;
        }
        if (!GameManager.Instance!.IsLunatic && _boss is BossAkari or BossKoharu or BossRei or BossMina)
        {
            if (_normalBarrage != null)
            {
                if (NormalBarrageActive) _normalBarrage.Tick(delta);
                if (!NormalBarrageActive)
                {
                    _normalBarrage = null;
                    _phrase++;
                    _hold = _timer = GameManager.Instance.Difficulty switch
                    { GameManager.Diff.Easy => 0.45, GameManager.Diff.Normal => 0.05, _ => 0 };
                }
            }
            else if (!SpecialAttackPending())
            {
                _timer -= delta;
                _hold = Math.Max(_hold, 0.1);
                if (_timer <= 0 && (GameManager.Instance.Difficulty != GameManager.Diff.Easy || !HasHostileShots()))
                    _normalBarrage = BossNormalBarrage.Begin(_boss, _phrase);
            }
            return;
        }
        if (_dodgeWave != null)
        {
            if (DodgeWaveActive) _dodgeWave.Tick(delta);
            if (!DodgeWaveActive)
            {
                _dodgeWave = null;
                _phrase++;
                _hold = 0.4;
                _timer = 0.8;
            }
            return;
        }
        if (_crossfire != null)
        {
            if (CrossfireActive) _crossfire.Tick(delta);
            if (!CrossfireActive)
            {
                _crossfire = null;
                _phrase++;
                int difficulty = (int)GameManager.Instance!.Difficulty;
                _hold = difficulty >= 2 ? 0.05 : difficulty == 0 ? 0.7 : 0.35;
                _timer = difficulty >= 2 ? 0.12 : _hold + (difficulty == 1 ? 0.35 : 1.6) * GameManager.Instance.DanmakuIntervalMul;
            }
            return;
        }
        if (NativeAttackActive)
        {
            _nativeTime = Math.Max(0, _nativeTime - delta);
            if (!NativeAttackActive) { _phrase++; _timer = 0.5; }
            return;
        }
        var game = GameManager.Instance!;
        int tier = (int)game.Difficulty;
        bool remote = (tier >= 2 || _phrase % 4 == 0) && _boss is BossAkari or BossKoharu or BossRei or BossMina;
        bool curtain = _phrase % 2 == 1;
        bool dodgeSequence = tier == 3 && game.HasDodge && _phrase % 8 == 6
            && _boss is BossAkari or BossKoharu or BossRei or BossMina;
        if (tree.GetFirstNodeInGroup("player") is Node2D player
            && !remote && !curtain && !dodgeSequence && player.GlobalPosition.DistanceTo(_boss.ShotCenter) < RingRadius + 24)
        {
            _timer = Math.Max(_timer, 0.25);
            _wave = 0;
            _hold = 0;
            if (tier >= 1) { _phrase |= 1; _timer = 0.35; }
            return;
        }
        _timer -= delta;
        if (_timer > 0) return;

        if (remote && !curtain && !dodgeSequence)
        {
            if (SpecialAttackPending()) { _timer = 0.15; return; }
            _crossfire = BossCrossfire.Begin(_boss, pincer: true, diagonal: tier >= 2 && _phrase % 4 == 2);
            return;
        }

        if (tier >= 1 && _phrase % 4 == 2)
        {
            if (dodgeSequence)
            {
                if (SpecialAttackPending()) { _timer = 0.7; return; }
                _dodgeWave = BossDodgeWave.Begin(_boss);
                return;
            }
            _nativeTime = tier == 1 ? 5.5 : tier == 2 ? 4.5 : 3.5;
            return;
        }
        if (curtain && _boss is BossAkari or BossKoharu or BossRei or BossMina)
        {
            if (SpecialAttackPending()) { _timer = 0.7; return; }
            bool sweep = tier >= 1 && _phrase % 4 == 3;
            _crossfire = BossCrossfire.Begin(_boss, sweep, _phrase % 8 == 3, weave: tier >= 2 && _phrase % 8 == 5);
            return;
        }
        _origin = _boss.ShotCenter;
        if (_wave == 0) _boss.AnnounceTechnique(_stage switch
        {
            0 => "溜め込んだ言葉", 1 => "祈りの輪郭", 2 => "反響の花冠", _ => "記憶の波紋",
        });
        if (tier >= 2 && _wave > 0)
        {
            Vector2 targetPoint = (tree.GetFirstNodeInGroup("player") as Node2D)?.GlobalPosition ?? Field.PlayerStart;
            Vector2 shift = _origin.DirectionTo(targetPoint).Orthogonal() * (_wave % 2 == 0 ? -20 : 20);
            _origin = (_origin + shift).Clamp(new Vector2(Field.Left + 40, Field.Top + 40), new Vector2(Field.Right - 40, Field.Bottom - 40));
        }
        Vector2 target = (tree.GetFirstNodeInGroup("player") as Node2D)?.GlobalPosition ?? Field.PlayerStart;
        Vector2 aim = _origin.DirectionTo(target);
        var pool = _boss.GetNode<BulletPool>("/root/Pool");
        var footprint = _boss.BodyShotJob is Job character ? BulletPool.BossHitShape(character, _boss.HasEvolved)
            : BulletFootprint.Texture(BulletArt.Get("mina_memory")!, BulletArt.Get("mina_memory")!.GetSize() / 2, 2.2f * 2 * Bullet.SpriteFit);
        int count = curtain ? 5 + tier * 2 : RingCount(20 + tier * 4 + (_stage == 4 ? 2 : 0), footprint);
        if (_stage == 2) count = curtain ? 3 : Math.Min(8, 6 + tier);
        float pitch = _stage == 2 ? 76f : footprint.Bounds.Size.Y + BulletPool.BossLaneGap;
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = curtain ? aim : aim.Rotated(Mathf.Tau * i / count);
            Vector2 destination = _origin + direction * (_stage == 2 ? 48f : RingRadius);
            if (curtain) destination += aim.Orthogonal() * ((i - count / 2) * pitch);
            bool centered = curtain ? i == count / 2 : i == 0;
            if (_boss.BodyShotJob is Job job)
            {
                var shot = pool.SpawnBossShot(_boss.ShotCenter, direction, job, _boss.HasEvolved,
                    curtain ? BossShotStyle.Aimed : BossShotStyle.Ring);
                if (centered) shot.BossAimTarget = target;
                shot.DeployBossFormation(destination);
            }
            else
            {
                var bullet = pool.Spawn(_boss.ShotCenter, direction * (54 + _stage * 3), true, 2.2f, 1, BulletShape.Dart, new Color("967bd8"));
                bullet.SetSprite(BulletArt.Get("mina_memory"));
            }
        }
        int waves = curtain ? 3 + tier : tier == 0 ? 2 : tier == 1 ? 5 : tier == 2 ? 4 : 5;
        if (_boss.HasEvolved) waves++;
        _wave++;
        if (_wave >= waves)
        {
            _wave = 0;
            _phrase++;
            _hold = tier >= 2 ? 0.35 : _stage == 0 ? 1.05 : 0.35;
            _timer = _hold + (tier >= 1 ? 0.5 : 1.6) * game.DanmakuIntervalMul;
        }
        else _timer = (_stage == 0 ? (tier >= 1 ? 0.65 : 1.3) : _stage == 2 ? (tier >= 1 ? 0.7 : 1.0) : curtain ? 0.7 : 0.5)
            * (tier == 1 ? 1 : game.DanmakuIntervalMul) * (tier >= 2 ? 0.62 : 1);
    }

    private void CancelCrossfire()
    {
        if (NormalBarrageActive) _normalBarrage!.Cancel();
        _normalBarrage = null;
        if (_crossfire != null && GodotObject.IsInstanceValid(_crossfire) && !_crossfire.Finished) _crossfire.Cancel();
        _crossfire = null;
    }

    private bool HasHostileShots()
    {
        foreach (var node in _boss.GetTree().GetNodesInGroup("enemy_bullets"))
            if (node is Bullet { Active: true }) return true;
        return false;
    }

    private bool SpecialAttackPending()
    {
        foreach (var child in _boss.GetChildren())
            if (child is AreaSpellCaster { AttackPending: true } or MinaPhaseAttacks { Active: true }) return true;
        return false;
    }

    private void TickCameo(double delta)
    {
        if (!_boss.IsVisibleInTree() || _boss.Transforming || !Field.Rect.HasPoint(_boss.ShotCenter)) return;
        _timer -= delta;
        if (_timer > 0) return;
        var game = GameManager.Instance!;
        bool reduced = _boss.GaugeVulnerable || _boss.GaugeReforming
            || _boss.GetTree().GetNodesInGroup("aoe").Count > 0;
        _timer = (_boss.HasEvolved ? 0.65 : 0.42) * game.DanmakuIntervalMul * (reduced ? 1.7 : 1) * 1.3;
        var pool = _boss.GetNode<BulletPool>("/root/Pool");
        int arms = _stage + 3;
        Vector2 target = (_boss.GetTree().GetFirstNodeInGroup("player") as Node2D)?.GlobalPosition ?? Field.PlayerStart;
        Vector2 aim = _boss.ShotCenter.DirectionTo(target);
        for (int i = 0; i < arms; i++)
        {
            Vector2 direction = aim.Rotated(Mathf.Tau * i / arms);
            var shot = pool.SpawnBossShot(_boss.ShotCenter, direction, _boss.BodyShotJob!.Value, _boss.HasEvolved, BossShotStyle.Ring);
            if (i == 0) shot.BossAimTarget = target;
        }
        _wave++;
    }
}
