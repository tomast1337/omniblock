namespace OmniBlock.Loot.Conditions;

/// <summary>Passes while the dropping entity is burning — a pig killed on fire yields cooked porkchops.</summary>
public sealed class OnFireCondition(bool expected = true) : ILootCondition
{
    public bool Test(in LootContext context) => context.Self is { } self && self.IsOnFire == expected;
}
