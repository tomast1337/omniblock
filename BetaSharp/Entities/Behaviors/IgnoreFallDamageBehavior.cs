namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Absorbs landing entirely: no fall damage, no step sound, and nothing propagated to a
///     passenger. A chicken flaps its way down, so it never lands hard enough to be hurt.
/// </summary>
public sealed class IgnoreFallDamageBehavior : IEntityPhysics
{
    public bool OnLanding(EntityLiving self, float fallDistance) => true;
}
