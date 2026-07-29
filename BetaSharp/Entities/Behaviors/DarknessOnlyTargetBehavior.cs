namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Hunts the closest targetable player within <paramref name="radius" />, but only while the mob
///     itself stands in darkness. Skips the line-of-sight check
///     <see cref="AlwaysHuntTargetBehavior" /> applies: spiders acquire targets through walls.
/// </summary>
public sealed class DarknessOnlyTargetBehavior(double radius = 16.0D) : IEntityTargetBehavior
{
    public Entity? FindPlayerToAttack(EntityCreature self)
    {
        float brightness = self.GetBrightnessAtEyes(1.0F);
        return !(brightness < 0.5F) ? null : self.World.Entities.GetClosestPlayerTarget(self.X, self.Y, self.Z, radius);
    }
}
