namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Removes the mob the moment the world is set to peaceful. <see cref="EntityMonster" /> applies
///     this from its class; a monster with no class of its own declares it here instead.
/// </summary>
public sealed class DespawnOnPeacefulBehavior : IEntityTicker
{
    public bool OnTickLiving(EntityLiving self)
    {
        if (self.World is { IsRemote: false, Difficulty: 0 }) self.MarkDead();

        // Adds to the AI rather than being it.
        return false;
    }
}
