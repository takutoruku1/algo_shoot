using System.Collections.Generic;
using System.Linq;

internal static class BossAttackCycle
{
    internal readonly record struct Slot(BossTechniqueKind Kind, int Variant = 0);

    internal static readonly Slot[] EasyAkariSlots = {
        new(BossTechniqueKind.Normal, 0), new(BossTechniqueKind.Normal, 2),
        new(BossTechniqueKind.Crossfire, 2), new(BossTechniqueKind.Normal, 1),
        new(BossTechniqueKind.Extra, 0), new(BossTechniqueKind.Edge),
        new(BossTechniqueKind.Area, 0), new(BossTechniqueKind.Extra, 1),
        new(BossTechniqueKind.Area, 1), new(BossTechniqueKind.Crossfire, 3),
        new(BossTechniqueKind.Extra, 0), new(BossTechniqueKind.Extra, 1),
    };
    private static readonly Slot[] Akari = EasyAkariSlots.Where(slot => slot.Kind != BossTechniqueKind.Extra).ToArray();
    private static readonly Slot[] EasyReiBefore = {
        new(BossTechniqueKind.Normal, 0), new(BossTechniqueKind.Normal, 1),
        new(BossTechniqueKind.Area, 0), new(BossTechniqueKind.Extra, 0),
        new(BossTechniqueKind.Normal, 2),
    };
    private static readonly Slot[] EasyKoharuBefore = {
        new(BossTechniqueKind.Normal, 0), new(BossTechniqueKind.Normal, 1),
        new(BossTechniqueKind.Area, 1), new(BossTechniqueKind.Normal, 2),
    };
    private static readonly Slot[] EasyKoharuAfter = {
        new(BossTechniqueKind.Extra, 0), new(BossTechniqueKind.Area, 1),
        new(BossTechniqueKind.Normal, 2), new(BossTechniqueKind.Normal, 0),
        new(BossTechniqueKind.Normal, 1),
    };
    private static readonly Slot[] EasyReiAfter = {
        new(BossTechniqueKind.Signature), new(BossTechniqueKind.Normal, 1),
        new(BossTechniqueKind.Area, 0), new(BossTechniqueKind.Extra, 1),
        new(BossTechniqueKind.Normal, 0), new(BossTechniqueKind.Normal, 2),
    };
    private static readonly Slot[][] EasyMina = {
        new Slot[] { new(BossTechniqueKind.Area, 0) },
        new Slot[] { new(BossTechniqueKind.Area, 1) },
        new Slot[] { new(BossTechniqueKind.Area, 2) },
        new Slot[] { new(BossTechniqueKind.Area, 3) },
        new Slot[] { new(BossTechniqueKind.Area, 4) },
    };
    private static readonly Slot[] KoharuFinale = {
        new(BossTechniqueKind.Finale), new(BossTechniqueKind.Extra, 0),
        new(BossTechniqueKind.Area, 1), new(BossTechniqueKind.Normal, 2),
        new(BossTechniqueKind.Normal, 0), new(BossTechniqueKind.Normal, 1),
    };
    private static readonly Slot[] ReiFinale = {
        new(BossTechniqueKind.Finale), new(BossTechniqueKind.Signature),
        new(BossTechniqueKind.Normal, 1), new(BossTechniqueKind.Area, 0),
        new(BossTechniqueKind.Extra, 1), new(BossTechniqueKind.Normal, 0),
        new(BossTechniqueKind.Normal, 2),
    };
    private static readonly Slot[] Cameo = { new(BossTechniqueKind.Cameo, 0), new(BossTechniqueKind.Cameo, 1) };

    internal static int Segment(Enemy boss) => boss switch
    {
        BossMina mina => mina.AttackPhase,
        CameoBoss => 0,
        BossAkari => boss.HasEvolved ? 1 : 0,
        _ => !boss.HasEvolved ? 0 : boss.HpRatio > .26f ? 1 : 2,
    };

    internal static IReadOnlyList<Slot> Slots(Enemy boss, int segment) => boss switch
    {
        BossMina => EasyMina[segment],
        CameoBoss => Cameo,
        BossAkari => Akari,
        BossKoharu => segment == 0 ? EasyKoharuBefore : segment == 1 ? EasyKoharuAfter : KoharuFinale,
        _ => segment == 0 ? EasyReiBefore : segment == 1 ? EasyReiAfter : ReiFinale,
    };

    internal static IReadOnlyList<Slot> EasySlots(Enemy boss, int segment) => boss switch
    {
        BossAkari => EasyAkariSlots,
        BossMina => EasyMina[segment],
        BossRei => segment == 0 ? EasyReiBefore : EasyReiAfter,
        _ => segment == 0 ? EasyKoharuBefore : EasyKoharuAfter,
    };
}
