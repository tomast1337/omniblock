namespace OmniBlock.Loot;

/// <summary>Gate deciding whether a <see cref="LootPool" /> produces anything at all.</summary>
public interface ILootCondition
{
    bool Test(in LootContext context);
}
