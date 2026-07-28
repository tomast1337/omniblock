using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Loot.Conditions;

/// <summary>Passes while the mob still has its wool. Reads the fleece through its wool behavior.</summary>
public sealed class SheepNotShearedCondition : ILootCondition
{
    public bool Test(in LootContext context) =>
        context.Self?.Behaviors.Interactable is WoolBehavior wool && !wool.IsShearedOn(context.Self);
}
