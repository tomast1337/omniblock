namespace BetaSharp.Loot.Conditions;

/// <summary>
///     Escape hatch for a mob-specific gate that reads the mob's own synced state — a sheep's
///     sheared flag, a slime's size. Phase 4 will need named, serializable conditions for the JSON
///     schema; until then a closure captured in the mob's constructor is the honest representation,
///     since none of this state has a general vocabulary yet.
/// </summary>
public sealed class EntityStateCondition(Func<bool> predicate) : ILootCondition
{
    public bool Test(in LootContext context) => predicate();
}
