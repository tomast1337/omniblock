namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Absorbs landing entirely: no fall damage, no step sound, and nothing propagated to a
///     passenger.
/// </summary>
public sealed class IgnoreFallDamageBehavior : IEntityPhysics
{
    public bool OnLanding(EntityLiving self, float fallDistance) => true;
}
