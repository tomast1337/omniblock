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
}
