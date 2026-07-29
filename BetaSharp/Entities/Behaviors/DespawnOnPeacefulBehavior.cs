namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Removes the mob the moment the world is set to peaceful, for monsters that are not an
///     <see cref="EntityMonster" /> and so do not get the rule from their class.
/// </summary>
public sealed class DespawnOnPeacefulBehavior : IEntityTicker
{
    public bool OnTickLiving(EntityLiving self)
    {
        if (self.World is { IsRemote: false, Difficulty: 0 })
        {
            self.MarkDead();
        }

        // Adds to the AI instead of replacing it.
        return false;
    }
}
