using BetaSharp.Entities;

namespace BetaSharp.Loot.Conditions;

/// <summary>
///     Passes only for an entity whose declared <c>size</c> is exactly <paramref name="size" /> — a
///     slime drops only at its smallest, because larger ones split instead.
/// </summary>
public sealed class SizeCondition(int size) : ILootCondition
{
    public bool Test(in LootContext context) => context.Self?.Synced<byte>("size")?.Value == size;
}
