using BetaSharp.Items;

namespace BetaSharp.Loot;

/// <summary>
///     A declarative set of loot pools. Every pool whose condition passes contributes stacks, so a
///     table expresses both "one of these" (weighted entries inside a pool) and "all of these"
///     (multiple pools).
///     <para>
///         Not to be confused with the older <see cref="BetaSharp.Blocks.LootTable" />, which blocks
///         still use — see docs/mob-data-driven-migration.md for the pending block migration.
///     </para>
/// </summary>
public sealed class LootTable(params LootPool[] pools)
{
    public IEnumerable<ItemStack> Roll(LootContext context) => pools.SelectMany(pool => pool.Roll(context));

    /// <summary>Single-pool shorthand: <paramref name="min" />..<paramref name="max" /> of one item.</summary>
    public static LootTable Single(Item item, int min, int max) => new(new LootPool([LootEntry.Of(item)], min, max));
}
