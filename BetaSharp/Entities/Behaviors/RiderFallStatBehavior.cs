namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Credits a riding player with an achievement when the mount survives a long enough drop, as
///     the pig's "when pigs fly" does. Falls through to the default landing response, so the mount
///     still takes its own fall damage.
/// </summary>
public sealed class RiderFallStatBehavior(Achievement achievement, float minimumDistance) : IEntityPhysics
{
    public bool OnLanding(EntityLiving self, float fallDistance)
    {
        if (fallDistance > minimumDistance && self.Passenger is EntityPlayer player)
        {
            player.IncrementStat(achievement);
        }

        return false;
    }
}
