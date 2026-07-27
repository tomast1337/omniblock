using BetaSharp.Entities;

namespace BetaSharp.Loot.Conditions;

/// <summary>Passes while the sheep still has its wool.</summary>
public sealed class SheepNotShearedCondition : ILootCondition
{
    public bool Test(in LootContext context) => context.Self is EntitySheep { IsSheared: false };
}
