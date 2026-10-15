using System.Collections.Generic;

internal static class BossAttackCycle
{
    internal readonly record struct Slot(BossTechniqueKind Kind, int Variant = 0);

    private static readonly Slot[] AkariBefore = {
        new(BossTechniqueKind.Normal, 0), new(BossTechniqueKind.Area, 0),
        new(BossTechniqueKind.Normal, 1), new(BossTechniqueKind.Edge),
        new(BossTechniqueKind.Extra, 0), new(BossTechniqueKind.Normal, 2),
        new(BossTechniqueKind.Area, 1), new(BossTechniqueKind.Normal, 3),
        new(BossTechniqueKind.Extra, 1), new(BossTechniqueKind.Normal, 4),
    };
    private static readonly Slot[] AkariAfter = {
        new(BossTechniqueKind.Crossfire, 0), new(BossTechniqueKind.Signature),
        new(BossTechniqueKind.Crossfire, 1), new(BossTechniqueKind.Extra, 0),
        new(BossTechniqueKind.Crossfire, 2), new(BossTechniqueKind.Dodge),
        new(BossTechniqueKind.Crossfire, 3), new(BossTechniqueKind.Extra, 1),
        new(BossTechniqueKind.Crossfire, 4),
    };
    private static readonly Slot[] KoharuBefore = {
        new(BossTechniqueKind.Area, 1), new(BossTechniqueKind.Normal, 1),
        new(BossTechniqueKind.Area, 0), new(BossTechniqueKind.Normal, 0),
        new(BossTechniqueKind.Edge), new(BossTechniqueKind.Extra, 0),
        new(BossTechniqueKind.Normal, 2), new(BossTechniqueKind.Area, 2),
        new(BossTechniqueKind.Extra, 1),
    };
    private static readonly Slot[] KoharuAfter = {
        new(BossTechniqueKind.Crossfire, 0), new(BossTechniqueKind.Crossfire, 1),
        new(BossTechniqueKind.Signature), new(BossTechniqueKind.Crossfire, 2),
        new(BossTechniqueKind.Extra, 0),
    };
    private static readonly Slot[] KoharuFinale = {
        new(BossTechniqueKind.Finale), new(BossTechniqueKind.Crossfire, 3),
        new(BossTechniqueKind.Extra, 1), new(BossTechniqueKind.Crossfire, 4),
        new(BossTechniqueKind.Dodge),
    };
    private static readonly Slot[] ReiBefore = {
        new(BossTechniqueKind.Normal, 0), new(BossTechniqueKind.Area, 2),
        new(BossTechniqueKind.Extra, 1), new(BossTechniqueKind.Normal, 1),
        new(BossTechniqueKind.Area, 0), new(BossTechniqueKind.Extra, 0),
        new(BossTechniqueKind.Area, 1), new(BossTechniqueKind.Normal, 2),
        new(BossTechniqueKind.Edge),
    };
    private static readonly Slot[] ReiAfter = {
        new(BossTechniqueKind.Signature), new(BossTechniqueKind.Crossfire, 0),
        new(BossTechniqueKind.Extra, 0), new(BossTechniqueKind.Crossfire, 1),
        new(BossTechniqueKind.Crossfire, 2),
    };
    private static readonly Slot[] ReiFinale = {
        new(BossTechniqueKind.Finale), new(BossTechniqueKind.Extra, 1),
        new(BossTechniqueKind.Crossfire, 3), new(BossTechniqueKind.Dodge),
        new(BossTechniqueKind.Crossfire, 4),
    };
    private static readonly Slot[][] Mina = {
        new Slot[] { new(BossTechniqueKind.Area, 0), new(BossTechniqueKind.Normal, 0), new(BossTechniqueKind.Extra, 0), new(BossTechniqueKind.Normal, 1) },
        new Slot[] { new(BossTechniqueKind.Area, 1), new(BossTechniqueKind.Normal, 2), new(BossTechniqueKind.Extra, 1), new(BossTechniqueKind.Edge) },
        new Slot[] { new(BossTechniqueKind.Area, 2), new(BossTechniqueKind.Crossfire, 0), new(BossTechniqueKind.Extra, 0), new(BossTechniqueKind.Crossfire, 1) },
        new Slot[] { new(BossTechniqueKind.Area, 3), new(BossTechniqueKind.Crossfire, 2), new(BossTechniqueKind.Extra, 1), new(BossTechniqueKind.Crossfire, 3) },
        new Slot[] { new(BossTechniqueKind.Area, 4), new(BossTechniqueKind.Crossfire, 4), new(BossTechniqueKind.Dodge) },
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
        BossMina => Mina[segment],
        CameoBoss => Cameo,
        BossAkari => segment == 0 ? AkariBefore : AkariAfter,
        BossKoharu => segment == 0 ? KoharuBefore : segment == 1 ? KoharuAfter : KoharuFinale,
        _ => segment == 0 ? ReiBefore : segment == 1 ? ReiAfter : ReiFinale,
    };
}
