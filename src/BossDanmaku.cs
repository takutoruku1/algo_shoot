using Godot;
using System.Linq;

public sealed class BossDanmaku
{
    private readonly Enemy _boss;
    private double _timer = 0.8;
    private int _phrase, _extraSlot, _crossfireSlot;
    private bool _extraDue, _form;
    private BossCharacterTechnique? _attack;
    private int _cycleSegment = -1, _cycleSlot, _cycleRounds;
    private bool _signaturePose;
    internal int CycleSegment => _cycleSegment;
    internal int CycleSlot => _cycleSlot;
    internal int CycleRounds => _cycleRounds;
    internal BossCharacterTechnique? ActiveTechnique => _attack;
    private bool Active => _attack != null && GodotObject.IsInstanceValid(_attack) && !_attack.Finished;
    internal bool DodgeWaveActive => Active && _attack!.TechniqueKind == BossTechniqueKind.Dodge;
    internal bool CrossfireActive => Active && !DodgeWaveActive;
    public bool Holding => _boss.BodyShotJob.HasValue && (Active || SpecialAttackPending() || _boss.AnimalTechniquePending
        || _boss.GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>().Any(a => !a.Finished));
    public BossDanmaku(Enemy boss)
    {
        _boss = boss;
        _form = boss is BossMina mina ? mina.IsDragonForm : boss.HasEvolved;
    }
    public void Tick(double delta)
    {
        if (!_boss.BodyShotJob.HasValue || Hud.BubblePaused || _boss.GetTree().Paused) return;
        if (_boss.UsesTechniqueCycle) { TickCycle(delta); return; }
        bool form = _boss is BossMina mina ? mina.IsDragonForm : _boss.HasEvolved;
        if (form != _form)
        {
            if (Active) _attack!.Cancel();
            _attack = null;
            _form = form;
            _extraSlot = 0;
            _extraDue = true;
            _timer = 0.7;
        }
        if (_boss.IsPurified || _boss.Transforming || !_boss.IsVisibleInTree()
            || !Field.Rect.HasPoint(_boss.ShotCenter) || _boss.GaugeVulnerable || _boss.GaugeReforming
            || _boss is BossMina { Transitioning: true })
        {
            if (Active) _attack!.Cancel();
            _attack = null;
            _timer = 0.7;
            return;
        }
        if (_attack != null)
        {
            if (Active) return;
            _attack = null;
            _timer = 0.5;
            return;
        }
        if (SpecialAttackPending() || _boss.AnimalTechniqueActive || _boss.AnimalTechniquePending) return;
        if (_boss.GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>().Any(a => !a.Finished)) return;
        _timer -= delta;
        if (_timer > 0 || AreaSpellCaster.HasRemainingShots(_boss.GetTree())) return;
        if (_boss is CameoBoss)
        {
            _attack = BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Cameo, _phrase++ % 2);
            return;
        }
        if (_extraDue)
        {
            _attack = BossCharacterTechnique.Begin(_boss, BossTechniqueKind.Extra, _extraSlot);
            _extraSlot = (_extraSlot + 1) % 2;
            _extraDue = false;
            return;
        }
        var game = GameManager.Instance!;
        var kind = BossTechniqueKind.Normal;
        int variant = _phrase % (_boss is BossAkari ? 5 : 3);
        if (game.IsLunatic)
        {
            kind = game.HasDodge && _phrase % 6 == 5 ? BossTechniqueKind.Dodge : BossTechniqueKind.Crossfire;
            variant = kind == BossTechniqueKind.Dodge ? 0 : _crossfireSlot++ % 5;
        }
        _attack = BossCharacterTechnique.Begin(_boss, kind, variant);
        _phrase++;
        _extraDue = true;
    }

    private void TickCycle(double delta)
    {
        if (_boss.IsPurified || _boss.Transforming || !_boss.IsVisibleInTree()
            || !Field.Rect.HasPoint(_boss.ShotCenter) || _boss.GaugeVulnerable || _boss.GaugeReforming
            || _boss is BossMina { Transitioning: true })
        {
            if (Active) _attack!.Cancel();
            _attack = null;
            _timer = .7;
            return;
        }
        int segment = BossAttackCycle.Segment(_boss);
        if (segment != _cycleSegment)
        {
            if (Active) _attack!.Cancel();
            _attack = null;
            if (_cycleSegment >= 0) _boss.RestartShieldCycle();
            _cycleSegment = segment;
            _cycleSlot = _cycleRounds = 0;
            _timer = .7;
        }
        if (_attack != null)
        {
            if (Active)
            {
                if (_attack.TechniqueKind == BossTechniqueKind.Signature && !_signaturePose
                    && (_attack.IsStriking || _attack.OwnedShots.Any(b => !b.LeadIn && b.CollisionLayer != 0)))
                {
                    _boss.ShowAnimalAttack();
                    _signaturePose = true;
                }
                return;
            }
            _attack = null;
            _timer = .5;
            return;
        }
        var caster = _boss.GetChildren().OfType<AreaSpellCaster>().FirstOrDefault();
        var slots = BossAttackCycle.Slots(_boss, segment);
        var slot = slots[_cycleSlot];
        if (caster is { Suppressed: true } && !(caster.CycleFinalePending && slot.Kind == BossTechniqueKind.Finale)) return;
        if (_boss.GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>().Any(a => !a.Finished)) return;
        _timer -= delta;
        if (_timer > 0 || AreaSpellCaster.HasRemainingShots(_boss.GetTree())) return;
        int startedSlot = _cycleSlot;
        _signaturePose = false;
        if (slot.Kind == BossTechniqueKind.Signature) _boss.SetAnimalWindup(true);
        _attack = BossCharacterTechnique.Begin(_boss, slot.Kind, slot.Variant);
        caster?.ObserveCycleAttack(_attack);
        _boss.GetChildren().OfType<MinaPhaseAttacks>().FirstOrDefault()?.ObserveCycleAttack(_attack);
        if (_boss is BossMina mina && slot.Kind == BossTechniqueKind.Area) mina.ShowSignaturePose();
        _attack.Ended += ended =>
        {
            if (slot.Kind == BossTechniqueKind.Signature) _boss.SetAnimalWindup(false);
            if (!ended.Completed || _cycleSegment != segment || _cycleSlot != startedSlot) return;
            _cycleSlot = (_cycleSlot + 1) % slots.Count;
            if (_cycleSlot == 0) _cycleRounds++;
        };
    }
    private bool SpecialAttackPending() => _boss.GetChildren().Any(child =>
        child is AreaSpellCaster { AttackPending: true } or MinaPhaseAttacks { Active: true });
}
