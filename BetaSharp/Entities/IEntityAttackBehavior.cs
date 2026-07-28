namespace BetaSharp.Entities;

/// <summary>Composable capability for how a mob attacks its current target.</summary>
public interface IEntityAttackBehavior
{
    /// <summary>
    ///     Called once per tick while the mob has a visible target. <paramref name="distance" /> is the
    ///     target's distance from the mob, already computed by <see cref="EntityCreature.TickLiving" />.
    /// </summary>
    void AttackEntity(EntityCreature self, Entity target, float distance) { }

    /// <summary>
    ///     Called instead of <see cref="AttackEntity" /> when the mob has a target it cannot see.
    ///     A creeper uses this to let its fuse burn back down while the player is behind cover.
    /// </summary>
    void AttackBlockedEntity(EntityCreature self, Entity target, float distance) { }
}
