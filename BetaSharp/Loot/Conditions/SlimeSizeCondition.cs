using BetaSharp.Entities;

namespace BetaSharp.Loot.Conditions;

/// <summary>Passes only for a slime of exactly <paramref name="size" /> — larger ones split instead of dropping.</summary>
public sealed class SlimeSizeCondition(int size) : ILootCondition
{
    public bool Test(in LootContext context) => context.Self is EntitySlime slime && slime.SlimeSize == size;
}
