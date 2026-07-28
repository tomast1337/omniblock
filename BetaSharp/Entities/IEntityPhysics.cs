namespace BetaSharp.Entities;

/// <summary>
///     Composable movement and collision response, the entity counterpart of the block
///     <c>Physics</c> slot. Landing is the first hook: a chicken ignores the fall entirely, a pig
///     credits the rider who survived it.
/// </summary>
public interface IEntityPhysics
{
    /// <summary>
    ///     Runs from <c>EntityLiving.OnLanding</c>, before the default response. Returning
    ///     <c>true</c> means the behavior handled the landing and the default — fall damage, the
    ///     step sound, and propagation to the passenger — is skipped entirely.
    /// </summary>
    bool OnLanding(EntityLiving self, float fallDistance) => false;

    /// <summary>
    ///     Runs after the movement tick has fully resolved, so it sees this tick's position and
    ///     ground state — where a mob that overrode <c>TickMovement</c> put its own code.
    /// </summary>
    void AfterTickMovement(EntityLiving self) { }

    /// <summary>
    ///     Replaces the movement body — how velocity is accelerated, applied and damped — returning
    ///     <c>true</c> when it handled the tick. Flight is what needs it: no gravity, no ladder
    ///     clamp, and drag on all three axes rather than two.
    /// </summary>
    bool Travel(EntityLiving self, float strafe, float forward) => false;

    /// <summary>
    ///     Replaces the type's natural-spawn check, or <c>null</c> to keep it. Consulted before a
    ///     monster applies its darkness rule, which is what lets a zombie pigman spawn in the lit
    ///     Nether. Spawn validity is placement — bounding box, fluid, the block below — so it lives
    ///     here rather than with the lifecycle events.
    /// </summary>
    bool? CanSpawn(EntityLiving self) => null;

    /// <summary>
    ///     Replaces how attractive a block is to path towards, or <c>null</c> to keep the type's own
    ///     rule. A giant inverts the usual monster preference and seeks out the light.
    /// </summary>
    float? GetBlockPathWeight(EntityLiving self, int x, int y, int z) => null;

    /// <summary>
    ///     Replaces the test for whether the mob is climbing, or <c>null</c> to keep the ladder
    ///     check. A spider climbs whatever it is pressed against.
    /// </summary>
    bool? IsClimbing(EntityLiving self) => null;

    /// <summary>
    ///     Replaces the test for whether the entity is in water, or <c>null</c> to keep the plain
    ///     flag. A squid tests a box reaching below itself and is carried by the current while it
    ///     looks — the answer and the push are the same operation, which is why this is a hook and
    ///     not a field.
    /// </summary>
    bool? IsInWater(Entity self) => null;
}
