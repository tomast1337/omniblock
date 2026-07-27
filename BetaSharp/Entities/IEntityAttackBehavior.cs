namespace BetaSharp.Entities;

/// <summary>Composable capability for how a mob attacks its current target.</summary>
public interface IEntityAttackBehavior
{
    /// <summary>
    ///     Called once per tick while the mob has a visible target. <paramref name="distance" /> is the
    ///     target's distance from the mob, already computed by <see cref="EntityCreature.TickLiving" />.
    /// </summary>
    void AttackEntity(EntityCreature self, Entity target, float distance) { }
}
