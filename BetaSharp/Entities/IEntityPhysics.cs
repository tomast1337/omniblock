namespace BetaSharp.Entities;

/// <summary>
///     Composable movement and collision response, the entity counterpart of the block
///     <c>Physics</c> slot.
/// </summary>
public interface IEntityPhysics
{
    /// <summary>
    ///     Runs from <c>EntityLiving.OnLanding</c>, before the default response. Returning
    ///     <c>true</c> means the behavior handled the landing and the default (fall damage, the step
    ///     sound, propagation to the passenger) is skipped.
    /// </summary>
    bool OnLanding(EntityLiving self, float fallDistance) => false;

    /// <summary>
    ///     Runs after the movement tick has fully resolved, so it sees this tick's position and
    ///     ground state.
    /// </summary>
    void AfterTickMovement(EntityLiving self)
    {
    }

    /// <summary>
    ///     Replaces the movement body (how velocity is accelerated, applied and damped), returning
    ///     <c>true</c> when it handled the tick. Flight needs it: no gravity, no ladder clamp, and
    ///     drag on all three axes instead of two.
    /// </summary>
    bool Travel(EntityLiving self, float strafe, float forward) => false;

    /// <summary>
    ///     Replaces the type's natural-spawn check, or <c>null</c> to keep it. Consulted before a
    ///     monster applies its darkness rule, which is how a zombie pigman spawns in the lit Nether.
    ///     Spawn validity is placement (bounding box, fluid, the block below), so it lives here rather
    ///     than with the lifecycle events.
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
    ///     how attentively a mob watches a passer-by.
    /// </summary>
    int? MaxFallDistance(EntityLiving self) => null;

    /// <summary>
    ///     Replaces the test for whether the entity is in water, or <c>null</c> to keep the plain
    ///     flag. A squid tests a box reaching below itself and is carried by the current while it
    ///     looks: the answer and the push are one operation, hence a hook rather than a field.
    /// </summary>
    bool? IsInWater(Entity self) => null;

    /// <summary>
    ///     Replaces the visibility test, or <c>null</c> for the default distance check. A lightning
    ///     bolt is drawn only while a flash is on, wherever the camera is.
    /// </summary>
    bool? ShouldRender(Entity self) => null;

    /// <summary>
    ///     Replaces the per-tick water test, or <c>null</c> to keep the default. As with
    ///     <see cref="IsInWater" />, the test and the push are one operation: a dropped item probes
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
    ///     does not travel: any displacement knocks it off its wall.
    /// </summary>
    bool OnMove(Entity self, double dx, double dy, double dz) => false;

    /// <summary>
    ///     Replaces a shove, returning <c>true</c> when it handled it. Sibling of
    ///     <see cref="OnMove" />: an entity that refuses to be moved refuses to be pushed too.
    /// </summary>
    bool OnAddVelocity(Entity self, double dx, double dy, double dz) => false;

    /// <summary>
    ///     Replaces what a synced position from the server does, returning <c>true</c> when handled.
    ///     A fishing bobber records it as a target and eases towards it over the given number of
    ///     ticks instead of snapping.
    /// </summary>
    bool OnPositionSync(Entity self, double x, double y, double z, float yaw, float pitch, int steps) => false;

    /// <summary>
    ///     Replaces where a passenger is carried, returning <c>true</c> when handled. A boat seats
    ///     its rider offset along its own facing, not straight above its middle.
    /// </summary>
    bool OnUpdatePassengerPosition(Entity self) => false;

    /// <summary>
    ///     Replaces what being bumped into does, returning <c>true</c> when handled. Minecarts trade
    ///     momentum with each other instead of simply shoving apart.
    /// </summary>
    bool OnCollision(Entity self, Entity other) => false;
}
