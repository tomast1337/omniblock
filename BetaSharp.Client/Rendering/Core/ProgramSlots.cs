namespace BetaSharp.Client.Rendering.Core;

/// <summary>The fallback chain every <see cref="ProgramSlot" /> resolves through, and its names on disk.</summary>
/// <remarks>
///     <para>
///         Each slot names a parent to fall back to, and every path leads to
///         <see cref="ProgramSlot.Basic" />. A draw asks for the most specific slot that describes
///         it; whoever is resolving walks <see cref="ResolutionChain" /> and takes the first slot
///         that has a program. So a pack supplying only <see cref="ProgramSlot.Basic" /> is complete
///         — every slot resolves to it — and one that wants waving leaves supplies
///         <see cref="ProgramSlot.Terrain" /> as well and still inherits the rest.
///     </para>
///     <para>
///         That asymmetry is the whole design: slots are cheap and shader files are not, so the
///         table below is deliberately finer than the set of programs anyone intends to write.
///         Splitting later means dropping in a file, which stops the chain earlier, and touching no
///         call site.
///     </para>
/// </remarks>
public static class ProgramSlots
{
    private readonly record struct SlotInfo(string PackName, ProgramSlot Parent, bool IsRoot);

    private static readonly Dictionary<ProgramSlot, SlotInfo> s_slots = Build();

    private static readonly Dictionary<string, ProgramSlot> s_byPackName =
        s_slots.ToDictionary(entry => entry.Value.PackName, entry => entry.Key, StringComparer.Ordinal);

    /// <summary>The name a pack file uses for this slot.</summary>
    /// <remarks>
    ///     Kept as data rather than derived from the enum member, because these are an external
    ///     contract with pack authors — <c>gbuffers_damagedblock</c> and <c>gbuffers_block</c> are
    ///     not what a mechanical transform of <see cref="ProgramSlot.DamagedBlock" /> or
    ///     <see cref="ProgramSlot.BlockEntity" /> would produce, and matching Iris matters more than
    ///     matching our own spelling.
    /// </remarks>
    public static string PackName(ProgramSlot slot) => s_slots[slot].PackName;

    /// <summary>The slot to try next when <paramref name="slot" /> has no program of its own.</summary>
    /// <returns><c>false</c> for <see cref="ProgramSlot.Basic" />, which is where the chain stops.</returns>
    public static bool TryGetParent(ProgramSlot slot, out ProgramSlot parent)
    {
        SlotInfo info = s_slots[slot];
        parent = info.Parent;

        return !info.IsRoot;
    }

    /// <summary>The slot named by <paramref name="packName" />, if a pack file by that name would mean anything.</summary>
    public static bool TryParsePackName(string packName, out ProgramSlot slot) =>
        s_byPackName.TryGetValue(packName, out slot);

    /// <summary>
    ///     <paramref name="slot" /> followed by its ancestors, ending at
    ///     <see cref="ProgramSlot.Basic" />. The order to look for a program in.
    /// </summary>
    public static IEnumerable<ProgramSlot> ResolutionChain(ProgramSlot slot)
    {
        yield return slot;

        while (TryGetParent(slot, out ProgramSlot parent))
        {
            yield return parent;
            slot = parent;
        }
    }

    private static Dictionary<ProgramSlot, SlotInfo> Build()
    {
        Dictionary<ProgramSlot, SlotInfo> slots = new()
        {
            [ProgramSlot.Basic] = Root("gbuffers_basic"),
            [ProgramSlot.Line] = Under("gbuffers_line", ProgramSlot.Basic),
            [ProgramSlot.Textured] = Under("gbuffers_textured", ProgramSlot.Basic),
            [ProgramSlot.SkyBasic] = Under("gbuffers_skybasic", ProgramSlot.Basic),
            [ProgramSlot.SkyTextured] = Under("gbuffers_skytextured", ProgramSlot.Textured),
            [ProgramSlot.Clouds] = Under("gbuffers_clouds", ProgramSlot.Textured),
            [ProgramSlot.TexturedLit] = Under("gbuffers_textured_lit", ProgramSlot.Textured),
            [ProgramSlot.Weather] = Under("gbuffers_weather", ProgramSlot.TexturedLit),
            [ProgramSlot.Hand] = Under("gbuffers_hand", ProgramSlot.TexturedLit),
            [ProgramSlot.Entities] = Under("gbuffers_entities", ProgramSlot.TexturedLit),
            [ProgramSlot.EntitiesGlowing] = Under("gbuffers_entities_glowing", ProgramSlot.Entities),
            [ProgramSlot.Terrain] = Under("gbuffers_terrain", ProgramSlot.TexturedLit),
            [ProgramSlot.DamagedBlock] = Under("gbuffers_damagedblock", ProgramSlot.Terrain),
            [ProgramSlot.BlockEntity] = Under("gbuffers_block", ProgramSlot.Terrain),
            [ProgramSlot.Water] = Under("gbuffers_water", ProgramSlot.Terrain),

            // No Iris counterpart, so no gbuffers_ prefix to inherit and nothing for a ported pack
            // to collide with.
            [ProgramSlot.Gui] = Under("gui", ProgramSlot.Basic),
        };

        Validate(slots);

        return slots;

        static SlotInfo Root(string packName) => new(packName, ProgramSlot.Basic, IsRoot: true);
        static SlotInfo Under(string packName, ProgramSlot parent) => new(packName, parent, IsRoot: false);
    }

    /// <summary>
    ///     Fails the first time anything touches this rather than the first time a particular slot is
    ///     drawn, so a new enum member that nobody gave a parent cannot reach a release as a slot
    ///     that silently resolves to nothing.
    /// </summary>
    private static void Validate(Dictionary<ProgramSlot, SlotInfo> slots)
    {
        foreach (ProgramSlot slot in Enum.GetValues<ProgramSlot>())
        {
            if (!slots.ContainsKey(slot))
            {
                throw new InvalidOperationException($"{nameof(ProgramSlot)}.{slot} has no fallback declared.");
            }

            ProgramSlot walk = slot;
            for (int steps = 0; steps <= slots.Count; steps++)
            {
                if (!slots[walk].IsRoot)
                {
                    walk = slots[walk].Parent;
                    continue;
                }

                if (walk != ProgramSlot.Basic)
                {
                    throw new InvalidOperationException(
                        $"{nameof(ProgramSlot)}.{slot} falls back to {walk}, which is not the root of the chain.");
                }

                break;
            }

            if (!slots[walk].IsRoot)
            {
                throw new InvalidOperationException($"{nameof(ProgramSlot)}.{slot} has a cyclic fallback chain.");
            }
        }
    }
}
