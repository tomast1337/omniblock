using BetaSharp.Entities;

namespace BetaSharp.Loot;

/// <summary>
///     Everything a loot pool's condition or entry may consult while rolling.
///     <para>
///         Blocks are not on this model yet (see docs/mob-data-driven-migration.md) — <see cref="BlockMeta" />
///         is here so the shape is ready for them, and reads 0 for mob drops.
///     </para>
/// </summary>
/// <param name="Self">The entity dropping the loot, or <c>null</c> for a block drop.</param>
/// <param name="Killer">Whatever dealt the killing blow, or <c>null</c> for an environmental death.</param>
/// <param name="BlockMeta">Metadata of the broken block, or 0 for a mob drop.</param>
/// <param name="Random">RNG for count and weight rolls.</param>
public readonly record struct LootContext(Entity? Self, Entity? Killer, int BlockMeta, Random Random)
{
    public static LootContext ForMob(Entity self, Entity? killer) => new(self, killer, 0, System.Random.Shared);
}
