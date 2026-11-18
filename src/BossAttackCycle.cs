using Godot;
using System.Collections.Generic;
using System.Linq;

internal static class BossAttackCycle
{
    internal readonly record struct Slot(BossTechniqueKind Kind, int Variant = 0);

    internal sealed class History
    {
        private readonly Dictionary<(Slot Slot, bool Evolved), int> _completed = new();
        private int _order;
        internal Slot? Last { get; private set; }

        internal void Start(Slot slot) => Last = slot;

        internal void Complete(Slot slot, bool evolved)
        {
            _completed[(slot, slot.Kind == BossTechniqueKind.Extra && evolved)] = ++_order;
        }

        internal int Next(IReadOnlyList<Slot> slots, int start, bool evolved)
        {
            int selected = start, oldest = int.MaxValue, ties = 0;
            for (int offset = 0; offset < slots.Count; offset++)
            {
                int index = (start + offset) % slots.Count;
                if (slots.Count > 1 && slots[index] == Last) continue;
                int order = _completed.GetValueOrDefault((slots[index], slots[index].Kind == BossTechniqueKind.Extra && evolved));
                if (order < oldest)
                {
                    selected = index;
                    oldest = order;
                    ties = 1;
                }
                else if (order == oldest && GD.RandRange(1, ++ties) == 1)
                    selected = index;
            }
            return selected;
        }
    }

    private static readonly Slot[] AkariBefore = {
        new(BossTechniqueKind.Normal, 5),
        new(BossTechniqueKind.Normal, 0), new(BossTechniqueKind.Normal, 2),
        new(BossTechniqueKind.Crossfire, 2), new(BossTechniqueKind.Normal, 1),
        new(BossTechniqueKind.Edge), new(BossTechniqueKind.Area, 0),
        new(BossTechniqueKind.Area, 1), new(BossTechniqueKind.Crossfire, 3),
        new(BossTechniqueKind.Crossfire, 1), new(BossTechniqueKind.Extra, 1),
        new(BossTechniqueKind.Dodge),
    };
    // Return paths, draft cancellation, clips and dwell stamps already belong to the integrated attacks.
    private static readonly Slot[] AkariAfter = new[] { new Slot(BossTechniqueKind.Signature),
        new Slot(BossTechniqueKind.Extra, 0) }.Concat(AkariBefore).ToArray();
    private static readonly Slot ReiRally = new(BossTechniqueKind.Normal, 4);
    private static readonly Slot[] EasyReiBefore = {
        new(BossTechniqueKind.Normal, 5),
        ReiRally,
        new(BossTechniqueKind.Normal, 0), new(BossTechniqueKind.Normal, 1),
        new(BossTechniqueKind.Normal, 3),
        new(BossTechniqueKind.Area, 3),
        new(BossTechniqueKind.Area, 0), new(BossTechniqueKind.Extra, 0),
        new(BossTechniqueKind.Normal, 2),
        new(BossTechniqueKind.Edge), new(BossTechniqueKind.Area, 1), new(BossTechniqueKind.Area, 2),
        new(BossTechniqueKind.Crossfire, 0), new(BossTechniqueKind.Crossfire, 1),
        new(BossTechniqueKind.Crossfire, 2), new(BossTechniqueKind.Crossfire, 3), new(BossTechniqueKind.Crossfire, 4),
        new(BossTechniqueKind.Extra, 1),
    };
    private static readonly Slot[] EasyKoharuBefore = {
        new(BossTechniqueKind.Normal, 0), new(BossTechniqueKind.Normal, 1),
        new(BossTechniqueKind.Area, 1), new(BossTechniqueKind.Normal, 2),
        new(BossTechniqueKind.Edge), new(BossTechniqueKind.Area, 0), new(BossTechniqueKind.Area, 2),
        new(BossTechniqueKind.Crossfire, 0), new(BossTechniqueKind.Crossfire, 1),
        new(BossTechniqueKind.Crossfire, 2), new(BossTechniqueKind.Crossfire, 4), new(BossTechniqueKind.Dodge),
    };
    private static readonly Slot[] EasyKoharuAfter = {
        new(BossTechniqueKind.Extra, 0), new(BossTechniqueKind.Area, 1),
        new(BossTechniqueKind.Normal, 2), new(BossTechniqueKind.Normal, 0),
        new(BossTechniqueKind.Normal, 1),
        new(BossTechniqueKind.Signature), new(BossTechniqueKind.Edge),
        new(BossTechniqueKind.Area, 0), new(BossTechniqueKind.Area, 2),
        new(BossTechniqueKind.Crossfire, 0), new(BossTechniqueKind.Crossfire, 1),
        new(BossTechniqueKind.Crossfire, 2), new(BossTechniqueKind.Crossfire, 4),
        new(BossTechniqueKind.Dodge), new(BossTechniqueKind.Extra, 1),
    };
    private static readonly Slot[] EasyReiAfter = {
        new(BossTechniqueKind.Normal, 5),
        ReiRally,
        new(BossTechniqueKind.Signature), new(BossTechniqueKind.Normal, 1),
        new(BossTechniqueKind.Normal, 3),
        new(BossTechniqueKind.Area, 3),
        new(BossTechniqueKind.Area, 0), new(BossTechniqueKind.Extra, 1),
        new(BossTechniqueKind.Normal, 0), new(BossTechniqueKind.Normal, 2),
        new(BossTechniqueKind.Edge), new(BossTechniqueKind.Area, 1), new(BossTechniqueKind.Area, 2),
        new(BossTechniqueKind.Crossfire, 0), new(BossTechniqueKind.Crossfire, 1),
        new(BossTechniqueKind.Crossfire, 2), new(BossTechniqueKind.Crossfire, 3), new(BossTechniqueKind.Crossfire, 4),
    };
    // Two-beat rests, mirrored prayers and overlapping gazes have no standalone duplicate slots.
    // Rei's shootable pages are part of Signature; Extra/0 after transformation stays retired.
    private static readonly Slot[][] EasyMina = {
        new Slot[] { new(BossTechniqueKind.Area, 0), new(BossTechniqueKind.Extra, 1), new(BossTechniqueKind.Normal, 0), new(BossTechniqueKind.Extra, 0) },
        new Slot[] { new(BossTechniqueKind.Area, 1), new(BossTechniqueKind.Extra, 1), new(BossTechniqueKind.Edge), new(BossTechniqueKind.Crossfire, 4), new(BossTechniqueKind.Crossfire, 1) },
        new Slot[] { new(BossTechniqueKind.Area, 2), new(BossTechniqueKind.Extra, 1), new(BossTechniqueKind.Extra, 0), new(BossTechniqueKind.Crossfire, 0) },
        new Slot[] { new(BossTechniqueKind.Area, 3), new(BossTechniqueKind.Extra, 1), new(BossTechniqueKind.Extra, 2), new(BossTechniqueKind.Crossfire, 2) },
        new Slot[] { new(BossTechniqueKind.Area, 4), new(BossTechniqueKind.Extra, 1) },
    };
    // Decompression/weight order, cancelled rows, elastic pull and handoff remain inside the Area attacks.
    private static readonly Slot[][] MinaBossMode = Enumerable.Range(0, 5).Select(phase =>
        Enumerable.Range(0, 5).Select(offset => new Slot(BossTechniqueKind.Area, (phase + offset) % 5))
            .Concat(EasyMina[phase].Skip(1)).ToArray()).ToArray();
    private static readonly Slot[] KoharuFinale = new[] { new Slot(BossTechniqueKind.Finale) }.Concat(EasyKoharuAfter).ToArray();
    private static readonly Slot[][] ReiWithRally = {
        EasyReiBefore, EasyReiAfter, new[] { new Slot(BossTechniqueKind.Finale) }.Concat(EasyReiAfter).ToArray(),
    };
    private static readonly Slot[][] ReiWithoutRally = ReiWithRally.Select(slots => slots.Where(slot => slot != ReiRally).ToArray()).ToArray();
    private static readonly Slot[] Cameo = { new(BossTechniqueKind.Cameo, 0), new(BossTechniqueKind.Cameo, 1) };

    internal static int Segment(Enemy boss) => boss switch
    {
        BossMina mina => mina.AttackPhase,
        CameoBoss => 0,
        BossAkari => boss.HasEvolved ? 1 : 0,
        _ => !boss.HasEvolved ? 0 : boss.HpRatio > .26f ? 1 : 2,
    };

    private static IReadOnlyList<Slot> ReiSlots(int segment) =>
        (GameManager.Instance!.HasChargeShot ? ReiWithRally : ReiWithoutRally)[segment];

    internal static IReadOnlyList<Slot> Slots(Enemy boss, int segment) => boss switch
    {
        BossMina => GameManager.BossRushActive ? MinaBossMode[segment] : EasyMina[segment],
        CameoBoss => Cameo,
        BossAkari => segment == 0 ? AkariBefore : AkariAfter,
        BossKoharu => segment == 0 ? EasyKoharuBefore : segment == 1 ? EasyKoharuAfter : KoharuFinale,
        _ => ReiSlots(segment),
    };

    internal static IReadOnlyList<Slot> EasySlots(Enemy boss, int segment) => boss switch
    {
        BossAkari => segment == 0 ? AkariBefore : AkariAfter,
        BossMina => GameManager.BossRushActive ? MinaBossMode[segment] : EasyMina[segment],
        BossRei => ReiSlots(segment == 0 ? 0 : 1),
        _ => segment == 0 ? EasyKoharuBefore : EasyKoharuAfter,
    };
}
