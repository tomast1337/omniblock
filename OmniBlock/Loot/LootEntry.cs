using OmniBlock.Items;

namespace OmniBlock.Loot;

/// <summary>
///     One weighted possibility in a <see cref="LootPool" />. The stack is resolved lazily from the
///     rolling <see cref="LootContext" />, so an entry can depend on runtime state (a sheep's fleece
///     colour, a pig's burning state) as well as on another block's or item's id regardless of
///     initialization order.
/// </summary>
public readonly struct LootEntry(Func<LootContext, ItemStack> resolve, int weight = 1)
{
    public int Weight { get; } = weight;

    public ItemStack Resolve(in LootContext context) => resolve(context);

    /// <summary>Convenience for the common "always this item, one per roll" entry.</summary>
    public static LootEntry Of(Item item, int weight = 1) => new(_ => new ItemStack(item, 1), weight);
}
