using OmniBlock.Items;

namespace OmniBlock.Loot;

/// <summary>
///     One independent roll group: if <paramref name="Condition" /> passes, the pool yields a uniform
///     <paramref name="MinCount" />..<paramref name="MaxCount" /> number of stacks, each picked from
///     <paramref name="Entries" /> by weight.
///     <para>
///         Pools are additive — a table with two pools rolls both (a skeleton's arrows *and* its
///         bones). Entries within one pool are exclusive — one is chosen per stack (gravel's 9:1
///         gravel-or-flint).
///     </para>
/// </summary>
/// <param name="MaxCount">Inclusive upper bound; -1 means "same as <paramref name="MinCount" />".</param>
public sealed record LootPool(LootEntry[] Entries, int MinCount = 1, int MaxCount = -1, ILootCondition? Condition = null)
{
    private readonly int _totalWeight = Entries.Sum(entry => entry.Weight);

    public IEnumerable<ItemStack> Roll(LootContext context)
    {
        if (Condition?.Test(context) == false) yield break;

        var max = MaxCount < 0 ? MinCount : MaxCount;
        var count = MinCount == max ? MinCount : MinCount + context.Random.Next(max - MinCount + 1);

        for (var i = 0; i < count; i++)
        {
            yield return Pick(context).Resolve(context);
        }
    }

    private LootEntry Pick(in LootContext context)
    {
        if (Entries.Length == 1) return Entries[0];

        var roll = context.Random.Next(_totalWeight);
        var cumulative = 0;
        foreach (var entry in Entries)
        {
            cumulative += entry.Weight;
            if (roll < cumulative) return entry;
        }

        return Entries[^1];
    }
}
