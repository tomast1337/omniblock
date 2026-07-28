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
    ///     Answers <c>true</c> when the mob should stay put this tick, or <c>null</c> to leave the
    ///     decision alone. A sitting wolf and a wolf shaking itself dry both stop where they are.
    /// </summary>
    bool? IsMovementCeased(EntityLiving self) => null;

    /// <summary>
    ///     Replaces the mob's fall-distance allowance, or <c>null</c> to keep it. Beta reads this
    ///     value in one place only, as the pitch speed a mob turns its head at, so in practice it is
    ///     how attentively a mob watches a passer-by — a sitting wolf, less.
    /// </summary>
    int? MaxFallDistance(EntityLiving self) => null;

    /// <summary>
    ///     Replaces the test for whether the entity is in water, or <c>null</c> to keep the plain
    ///     flag. A squid tests a box reaching below itself and is carried by the current while it
    ///     looks — the answer and the push are the same operation, which is why this is a hook and
    ///     not a field.
    /// </summary>
    bool? IsInWater(Entity self) => null;

    /// <summary>
    ///     Replaces the visibility test, or <c>null</c> for the default distance check. A lightning
    ///     bolt is drawn only while a flash is on, wherever the camera is.
    /// </summary>
    bool? ShouldRender(Entity self) => null;

    /// <summary>
    ///     Replaces the per-tick water test, or <c>null</c> to keep the default. Like
    ///     <see cref="IsInWater" /> the test and the push are one operation — a dropped item probes
    ///     its full box and is carried by the current while it looks.
    /// </summary>
    bool? CheckWaterCollisions(Entity self) => null;

    /// <summary>
    ///     Runs when the server pushes a velocity to the client's copy, returning <c>true</c> when
    ///     it handled the update. A thrown projectile derives its facing from the velocity the
    ///     first time one arrives, since the object-spawn packet carries no angles.
    /// </summary>
    bool OnVelocityFromServer(Entity self, double vx, double vy, double vz) => false;

    /// <summary>
    ///     Replaces the movement step, returning <c>true</c> when it handled the move. A painting
    ///     does not travel: any displacement at all is what knocks it off its wall.
    /// </summary>
    bool OnMove(Entity self, double dx, double dy, double dz) => false;

    /// <summary>
    ///     Replaces a shove, returning <c>true</c> when it handled it. Sibling of
    ///     <see cref="OnMove" /> — an entity that refuses to be moved refuses to be pushed too.
    /// </summary>
    bool OnAddVelocity(Entity self, double dx, double dy, double dz) => false;
}
