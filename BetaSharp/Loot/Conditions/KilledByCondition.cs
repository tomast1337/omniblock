namespace BetaSharp.Loot.Conditions;

/// <summary>
///     Passes only when the killing blow came from a <typeparamref name="T" /> — the rule behind a
///     creeper dropping a music disc when a skeleton shoots it.
/// </summary>
public sealed class KilledByCondition<T> : ILootCondition where T : Entities.Entity
{
    public bool Test(in LootContext context) => context.Killer is T;
}
