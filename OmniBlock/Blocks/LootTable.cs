using System;

namespace OmniBlock.Blocks;

public sealed record LootEntryDefinition(string ItemName, int Weight = 1);

public sealed record LootTableDefinition(LootEntryDefinition[] Entries, int MinCount = 1, int MaxCount = -1, int Meta = 0);

/// <summary>
///     One weighted possibility in a <see cref="LootTable" />. The item id is resolved lazily via
///     <paramref name="itemId" /> so it may reference another block's or item's static field
///     regardless of static-initializer declaration order (the same forward-reference concern
///     <c>MeltBehavior</c>/<c>StairsBehavior</c> handle the same way).
/// </summary>
public readonly struct LootEntry(Func<int> itemId, int weight = 1)
{
    public int Weight { get; } = weight;
    public int ItemId => itemId();
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
        Array.Sort(_entries, static (a, b) => b.Weight.CompareTo(a.Weight));
        foreach (LootEntry entry in _entries)
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
            if (roll < cumulative) return entry.ItemId;
        }

        return _entries[^1].ItemId;
    }

    public int GetPrimaryItemId() => _entries[0].ItemId;
}
