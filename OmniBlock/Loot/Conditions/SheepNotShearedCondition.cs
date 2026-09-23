using OmniBlock.Entities.Behaviors;

namespace OmniBlock.Loot.Conditions;

/// <summary>Passes while the mob still has its wool. Reads the fleece through its wool behavior.</summary>
public sealed class SheepNotShearedCondition : ILootCondition
{
    public bool Test(in LootContext context) => context.Self?.Behaviors.Find<WoolBehavior>() is { } wool && !wool.IsShearedOn(context.Self);
}
