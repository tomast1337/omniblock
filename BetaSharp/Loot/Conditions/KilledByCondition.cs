using BetaSharp.Entities;

namespace BetaSharp.Loot.Conditions;

/// <summary>
///     Passes only when the killing blow came from the named entity type — the rule behind a creeper
///     dropping a music disc when a skeleton shoots it.
///     <para>
///         Matched by registry id rather than by C# class, because most mobs no longer have one: a
///         skeleton and a zombie are both an <c>EntityMonster</c>.
///     </para>
/// </summary>
public sealed class KilledByCondition(string killerId) : ILootCondition
{
    public bool Test(in LootContext context) =>
        context.Killer is { } killer && EntityRegistry.GetId(killer) == killerId;
}
