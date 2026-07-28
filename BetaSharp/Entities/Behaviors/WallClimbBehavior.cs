namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Climbs whatever it is pressed against, rather than needing a ladder — a spider walking up a
///     wall is just a mob that treats any horizontal collision as something to climb.
/// </summary>
public sealed class WallClimbBehavior : IEntityPhysics
{
    public bool? IsClimbing(EntityLiving self) => self.HorizontalCollision;
}
