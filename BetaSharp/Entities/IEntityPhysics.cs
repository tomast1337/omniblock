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
}
