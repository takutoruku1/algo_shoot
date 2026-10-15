using Godot;

public partial class MinaPhaseAttacks : Node
{
    private BossMina _boss = null!;
    private BossCharacterTechnique? _attack;
    private int _phase, _edge;
    private bool _nextEdge;
    private double _cooldown;
    private bool _active;
    public bool Active => _boss != null && _boss.UsesTechniqueCycle
        ? _attack != null && IsInstanceValid(_attack) && !_attack.Finished : _active;
    public bool OpenerCompleted { get; private set; }
    public void Configure(BossMina boss, Node world)
    {
        _boss = boss;
        BeginPhase(0);
    }
    public void BeginPhase(int phase)
    {
        CancelPendingAttacks();
        _phase = phase;
        _nextEdge = false;
        OpenerCompleted = false;
        _cooldown = 1.2;
    }
    public void CancelPendingAttacks()
    {
        if (_attack != null && IsInstanceValid(_attack) && !_attack.Finished) _attack.Cancel();
        _attack = null;
        _active = false;
        _cooldown = 3;
    }
    public override void _Process(double delta)
    {
        if (_boss.UsesTechniqueCycle) return;
        if (_boss.IsPurified || _boss.GaugeVulnerable || _boss.GaugeReforming)
        {
            CancelPendingAttacks();
            if (_boss.GaugeVulnerable) OpenerCompleted = true;
            return;
        }
        if (Hud.BubblePaused || _boss.Transitioning) return;
        if (_attack != null)
        {
            if (IsInstanceValid(_attack) && !_attack.Finished) return;
            _attack = null;
            _active = false;
            _cooldown = _nextEdge ? 6 : 3;
            return;
        }
        _cooldown -= GameManager.EnemyDelta(delta);
        if (_cooldown > 0 || _boss.CrossfireActive || _boss.DodgeWaveActive) return;
        _active = true;
        if (AreaSpellCaster.HasRemainingShots(GetTree())) return;
        var kind = _nextEdge ? BossTechniqueKind.Edge : BossTechniqueKind.Area;
        _attack = BossCharacterTechnique.Begin(_boss, kind, _nextEdge ? _edge++ : _phase);
        _attack.Ended += ended =>
        {
            if (ended.Completed)
            {
                if (ended.TechniqueKind == BossTechniqueKind.Area) OpenerCompleted = true;
                _nextEdge = ended.TechniqueKind == BossTechniqueKind.Area;
            }
        };
        _boss.ShowSignaturePose();
    }
    internal void ObserveCycleAttack(BossCharacterTechnique technique)
    {
        _attack = technique;
        technique.Ended += ended =>
        {
            if (ended.Completed && ended.TechniqueKind == BossTechniqueKind.Area && ended.TechniqueVariant == _phase)
                OpenerCompleted = true;
        };
    }
    public static string SignatureName(int phase) => phase switch
    {
        0 => "消せなかった声", 1 => "未送信、再送", 2 => "拍手の檻",
        3 => "閉じない配信", _ => "リフレイン・帰り道",
    };
    public override void _ExitTree() => CancelPendingAttacks();
}
