namespace BetaSharp.Blocks;

/// <summary>
///     One weighted possibility in a <see cref="LootTable" />. The item id is resolved lazily via
///     <paramref name="itemId" /> so it may reference another block's or item's static field
///     regardless of static-initializer declaration order (the same forward-reference concern
///     <c>MeltBehavior</c>/<c>StairsBehavior</c> handle the same way).
/// </summary>
public readonly struct LootEntry
{
    private readonly Func<int> _itemId;

    public int Weight { get; }

    public LootEntry(Func<int> itemId, int weight = 1)
    {
        _itemId = itemId;
        Weight = weight;
    }

    public int ItemId => _itemId();
}

/// <summary>
///     A static, declarative table of weighted item drops. Replaces ad-hoc conditional
///     <c>Func&lt;int&gt;</c> drop logic (e.g. gravel's inline flint-chance ternary) with an explicit,
///     enumerable set of weighted entries.
/// </summary>
public sealed class LootTable
{
    private readonly LootEntry[] _entries;
    private readonly int _totalWeight;

    public LootTable(params LootEntry[] entries)
    {
        _entries = entries;
        foreach (LootEntry entry in entries)
        {
            _totalWeight += entry.Weight;
        }
    }

    public int Roll(Random random)
    {
        int roll = random.Next(_totalWeight);
        int cumulative = 0;
        foreach (LootEntry entry in _entries)
        {
            cumulative += entry.Weight;
            if (roll < cumulative)
            {
                return entry.ItemId;
            }
        }

        return _entries[^1].ItemId;
    }
}
