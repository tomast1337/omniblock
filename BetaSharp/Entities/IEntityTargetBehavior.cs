namespace BetaSharp.Entities;

/// <summary>
///     Composable capability for target acquisition. A <c>null</c> <see cref="EntityCreature.Targeting" />
///     slot already means "never targets anything", so there is no explicit never-target implementation.
/// </summary>
public interface IEntityTargetBehavior
{
    /// <summary>
    ///     Returns the entity this mob should start hunting, or <c>null</c> to stay idle. Despite the
    ///     name this is not restricted to players — <see cref="EntityWolf" /> targets sheep.
    /// </summary>
    Entity? FindPlayerToAttack(EntityCreature self) => null;
}
