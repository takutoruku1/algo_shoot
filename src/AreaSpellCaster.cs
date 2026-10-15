using Godot;
using System.Linq;

public partial class AreaSpellCaster : Node2D
{
    private Enemy _owner = null!;
    private string _key = "";
    private double _clock, _interval;
    private int _area, _edge;
    private bool _nextEdge = true, _pending, _finalePending, _suppressed;
    private BossCharacterTechnique? _attack;
    public bool EdgeAttackActive => (_pending && _nextEdge) || Active && _attack!.TechniqueKind == BossTechniqueKind.Edge;
    public bool AoeActive => _finalePending || Active && _attack!.TechniqueKind == BossTechniqueKind.Finale;
    internal bool AttackPending => _pending || _finalePending || Active || Suppressed;
    private bool Active => _attack != null && IsInstanceValid(_attack) && !_attack.Finished;
    public Vector2 LastChainSafe { get; private set; }
    public bool FinaleCompleted { get; private set; }
    internal bool CycleFinalePending => _finalePending;
    public bool Suppressed
    {
        get => _suppressed;
        set
        {
            _suppressed = value;
            if (value && !AoeActive) CancelPendingAttacks();
        }
    }
    public void Configure(string key, Node world)
    {
        _key = key;
        _interval = BossTuning.F(key, "aoe_interval", key == "akari" ? 9 : key == "rei" ? 8 : 7);
    }
    public void CancelPendingAttacks()
    {
        bool cycle = _owner != null && _owner.UsesTechniqueCycle;
        bool retryFinale = cycle && !_owner!.IsPurified
            && (_finalePending || Active && _attack!.TechniqueKind == BossTechniqueKind.Finale);
        if (Active) _attack!.Cancel();
        _attack = null;
        _pending = false;
        _finalePending = retryFinale;
        _clock = 0;
        if (!cycle || retryFinale) FinaleCompleted = false;
    }
    internal void RequestFinale()
    {
        if (AoeActive) return;
        _owner ??= (Enemy)GetParent();
        if (_owner.UsesTechniqueCycle)
        {
            _finalePending = true;
            FinaleCompleted = false;
            return;
        }
        _owner.CancelAnimalTechnique();
        if (Active) _attack!.Cancel();
        _attack = null;
        _pending = false;
        _finalePending = true;
        FinaleCompleted = false;
    }
    public override void _Process(double delta)
    {
        _owner ??= (Enemy)GetParent();
        if (_owner.IsPurified) { CancelPendingAttacks(); return; }
        if (_owner.UsesTechniqueCycle) return;
        if (_owner.GaugeVulnerable || _owner.GaugeReforming || _owner.Transforming)
        {
            bool retryFinale = _finalePending || Active && _attack!.TechniqueKind == BossTechniqueKind.Finale;
            bool completed = FinaleCompleted;
            CancelPendingAttacks();
            FinaleCompleted = completed;
            _finalePending = retryFinale;
            return;
        }
        if (Hud.BubblePaused || ! _owner.IsVisibleInTree()) return;
        if (_attack != null)
        {
            if (Active) return;
            _attack = null;
            _clock = 0;
            return;
        }
        if (Suppressed && !_finalePending || _owner.AnimalTechniqueActive || _owner.AnimalTechniquePending) return;
        if (!_pending && !_finalePending) _clock += GameManager.EnemyDelta(delta);
        if (_clock >= _interval) _pending = true;
        if (_owner.CrossfireActive || _owner.DodgeWaveActive || HasRemainingShots(GetTree())) return;
        if (!_pending && !_finalePending) return;
        var kind = _finalePending ? BossTechniqueKind.Finale : _nextEdge ? BossTechniqueKind.Edge : BossTechniqueKind.Area;
        int variant = kind == BossTechniqueKind.Area ? _area++ % (_key == "akari" ? 2 : 3)
            : kind == BossTechniqueKind.Edge ? _edge++ : 0;
        _attack = BossCharacterTechnique.Begin(_owner, kind, variant);
        _attack.Ended += finished =>
        {
            LastChainSafe = finished.LastPosition;
            FinaleCompleted = finished.TechniqueKind == BossTechniqueKind.Finale && finished.Completed;
            if (finished.TechniqueKind == BossTechniqueKind.Finale && !finished.Completed && !_owner.IsPurified)
                _finalePending = true;
        };
        _pending = _finalePending = false;
        if (kind != BossTechniqueKind.Finale) _nextEdge = !_nextEdge;
    }
    internal void ObserveCycleAttack(BossCharacterTechnique technique)
    {
        _owner ??= (Enemy)GetParent();
        _attack = technique;
        if (technique.TechniqueKind == BossTechniqueKind.Finale)
        {
            _finalePending = false;
            FinaleCompleted = false;
        }
        technique.Ended += ended =>
        {
            if (ended.TechniqueKind != BossTechniqueKind.Finale) return;
            LastChainSafe = ended.LastPosition;
            FinaleCompleted = ended.Completed;
            _finalePending = !ended.Completed && !_owner.IsPurified;
        };
    }
    public float WarnMul() => GameManager.Instance!.Difficulty switch
    { GameManager.Diff.Hard => 0.9f, GameManager.Diff.Lunatic => 0.8f, _ => 1f };
    internal static bool HasRemainingShots(SceneTree tree) => tree.GetNodesInGroup("enemy_bullets").OfType<Bullet>().Any(b => b.Active);
    public override void _ExitTree()
    {
        if (Active) _attack!.Cancel();
    }
}
