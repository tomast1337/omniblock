namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Removes the mob the moment the world is set to peaceful, for a mob that wants the rule
///     without the rest of <see cref="HostileMonsterBehavior" />.
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
