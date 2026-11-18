using Godot;
using System.Collections.Generic;
using System.Linq;

public sealed class BossDanmaku
{
    private readonly Enemy _boss;
    private double _timer = 0.8;
    private int _phrase, _extraSlot, _crossfireSlot;
    private bool _extraDue, _form;
    private BossCharacterTechnique? _attack;
    private int _cycleSegment = -1, _cycleSlot, _cycleRounds;
    private int _easySegment = -1, _easySlot, _easyRounds;
    private int _cycleCompleted, _easyCompleted;
    private int _slotCount;
    private readonly BossAttackCycle.History _history;
    private bool _signaturePose;
    private bool _startAfterFormChange;
    internal int CycleSegment => _cycleSegment;
    internal int CycleSlot => _cycleSlot;
    internal int CycleRounds => _cycleRounds;
    internal int EasySlot => _easySlot;
    internal int EasyRounds => _easyRounds;
    internal BossCharacterTechnique? ActiveTechnique => _attack;
    private bool Active => _attack != null && GodotObject.IsInstanceValid(_attack) && !_attack.Finished;
    internal bool DodgeWaveActive => Active && _attack!.TechniqueKind == BossTechniqueKind.Dodge;
    internal bool CrossfireActive => Active && !DodgeWaveActive;
    public bool Holding => _boss.BodyShotJob.HasValue && (Active || SpecialAttackPending() || _boss.AnimalTechniquePending
        || _boss.GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>().Any(a => !a.Finished));
    public BossDanmaku(Enemy boss)
    {
        _boss = boss;
        _history = boss.TechniqueHistory;
        _form = boss is BossMina mina ? mina.IsDragonForm : boss.HasEvolved;
    }
    internal void ReadyAfterFormChange() => _startAfterFormChange = true;

    public void Tick(double delta)
    {
        if (!_boss.BodyShotJob.HasValue || _boss.BossFrameReturning || Hud.BubblePaused || _boss.GetTree().Paused) return;
        if (_boss.UsesTechniqueCycle) { TickCycle(delta); return; }
        if (_boss.UsesEasyTechniqueSlots) { TickEasyCharacter(delta); return; }
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
            _timer = _attack.Completed ? 0 : .5;
            _attack = null;
            if (_timer > 0) return;
        }
        if (_boss.GaugeAttackResolving || SpecialAttackPending() || _boss.AnimalTechniqueActive || _boss.AnimalTechniquePending) return;
        if (_boss.GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>().Any(a => !a.Finished)) return;
        _timer = _startAfterFormChange ? 0 : _timer - delta;
        if (_timer > 0 || AreaSpellCaster.HasRemainingShots(_boss.GetTree())) return;
        _startAfterFormChange = false;
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

    private void TickEasyCharacter(double delta)
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
        int segment = _boss is BossMina mina ? mina.AttackPhase : _boss.HasEvolved ? 1 : 0;
        var slots = BossAttackCycle.EasySlots(_boss, segment);
        if (_easySegment != segment || slots.Count != _slotCount)
        {
            if (Active) _attack!.Cancel();
            _attack = null;
            _easySegment = segment;
            _slotCount = slots.Count;
            _easySlot = NextSlot(slots, 0);
            _easyRounds = _easyCompleted = 0;
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
            _timer = _attack.Completed ? 0 : .35;
            _attack = null;
            if (_timer > 0) return;
        }
        if (_boss.GaugeAttackResolving || SpecialAttackPending() || _boss.AnimalTechniqueActive || _boss.AnimalTechniquePending) return;
        var caster = _boss.GetChildren().OfType<AreaSpellCaster>().FirstOrDefault();
        if (caster is { Suppressed: true, DeferringFinale: false }) return;
        if (_boss.GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>().Any(a => !a.Finished)) return;
        _timer = _startAfterFormChange ? 0 : _timer - delta;
        if (_timer > 0 || AreaSpellCaster.HasRemainingShots(_boss.GetTree())) return;
        _startAfterFormChange = false;
        int startedSlot = _easySlot;
        var slot = slots[startedSlot];
        _signaturePose = false;
        if (slot.Kind == BossTechniqueKind.Signature) _boss.SetAnimalWindup(true);
        _attack = BossCharacterTechnique.Begin(_boss, slot.Kind, slot.Variant);
        if (slot.Kind is BossTechniqueKind.Area or BossTechniqueKind.Edge) caster?.ObserveCycleAttack(_attack);
        _boss.GetChildren().OfType<MinaPhaseAttacks>().FirstOrDefault()?.ObserveCycleAttack(_attack);
        if (_boss is BossMina bossMina && slot.Kind == BossTechniqueKind.Area) bossMina.ShowSignaturePose();
        _attack.Ended += ended =>
        {
            if (slot.Kind == BossTechniqueKind.Signature) _boss.SetAnimalWindup(false);
            if (_easySegment != segment || _easySlot != startedSlot) return;
            if (ended.Completed)
            {
                _history.Complete(slot, ended.IsEnhanced);
                _easyRounds = ++_easyCompleted / slots.Count;
            }
            _easySlot = NextSlot(slots, (_easySlot + 1) % slots.Count);
        };
    }

    private void TickCycle(double delta)
    {
        if (_boss.FinishingSealedBarAttack) return;
        if (_boss.IsPurified || _boss.Transforming || !_boss.IsVisibleInTree()
            || !Field.Rect.HasPoint(_boss.ShotCenter) || _boss.GaugeVulnerable || _boss.GaugeReforming
            || _boss is BossMina { Transitioning: true })
        {
            if (Active) _attack!.Cancel();
            _attack = null;
            _timer = .7;
            return;
        }
        int segment = _boss.TechniquePractice ? 0 : BossAttackCycle.Segment(_boss);
        var slots = _boss.PracticeSlots ?? BossAttackCycle.Slots(_boss, segment);
        if (segment != _cycleSegment || slots.Count != _slotCount)
        {
            if (Active) _attack!.Cancel();
            _attack = null;
            if (_cycleSegment >= 0 && segment != _cycleSegment) _boss.RestartShieldCycle();
            _cycleSegment = segment;
            _slotCount = slots.Count;
            _cycleSlot = NextSlot(slots, 0);
            _cycleRounds = _cycleCompleted = 0;
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
            _timer = _boss.TechniquePractice ? BossPracticeRoot.Active?.RepeatDelay ?? .5 : _attack.Completed ? 0 : .5;
            _attack = null;
            if (_boss.TechniquePractice || _timer > 0) return;
        }
        if (_boss.GaugeAttackResolving) return;
        var caster = _boss.GetChildren().OfType<AreaSpellCaster>().FirstOrDefault();
        if (caster is { CycleFinalePending: true, DeferringFinale: false })
            for (int index = 0; index < slots.Count; index++)
                if (slots[index].Kind == BossTechniqueKind.Finale) { _cycleSlot = index; break; }
        var slot = slots[_cycleSlot];
        if (caster is { Suppressed: true, DeferringFinale: false }
            && !(caster.CycleFinalePending && slot.Kind == BossTechniqueKind.Finale)) return;
        if (_boss.GetTree().GetNodesInGroup("boss_character_techniques").OfType<BossCharacterTechnique>().Any(a => !a.Finished)) return;
        _timer = _startAfterFormChange ? 0 : _timer - delta;
        if (_timer > 0 || AreaSpellCaster.HasRemainingShots(_boss.GetTree())) return;
        _startAfterFormChange = false;
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
            if (_cycleSegment != segment || _cycleSlot != startedSlot) return;
            if (ended.Completed)
            {
                _history.Complete(slot, ended.IsEnhanced);
                _cycleRounds = ++_cycleCompleted / slots.Count;
            }
            _cycleSlot = NextSlot(slots, (_cycleSlot + 1) % slots.Count);
        };
    }
    private int NextSlot(IReadOnlyList<BossAttackCycle.Slot> slots, int start)
    {
        if (_boss.TechniquePractice) return start;
        if (_boss is BossMina && _history.Last != slots[0]
            && _boss.GetChildren().OfType<MinaPhaseAttacks>().Any(caster => !caster.OpenerCompleted)) return 0;
        return _history.Next(slots, start, _boss is BossMina formMina ? formMina.IsDragonForm : _boss.HasEvolved);
    }
    private bool SpecialAttackPending() => _boss.GetChildren().Any(child =>
        child is AreaSpellCaster { AttackPending: true } or MinaPhaseAttacks { Active: true });
}
