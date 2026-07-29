namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Treats any horizontal collision as something to climb, so the mob needs no ladder. Used by
///     the spider.
/// </summary>
public sealed class WallClimbBehavior : IEntityPhysics
{
    public bool? IsClimbing(EntityLiving self) => self.HorizontalCollision;
}
