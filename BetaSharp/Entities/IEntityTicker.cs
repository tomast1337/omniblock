namespace BetaSharp.Entities;

/// <summary>
///     Composable per-tick behavior. Declared on <see cref="Entity" /> rather than
///     <see cref="EntityLiving" /> because every entity kind ticks — projectiles, vehicles and
///     falling blocks override <c>Tick</c> just as mobs do.
///     <para>
///         Tickers are shared across every entity of a type, so they hold no per-entity fields.
///         Mutable state goes in <see cref="State.EntityState" />, addressed by handles the ticker
///         resolved once at load.
///     </para>
/// </summary>
public interface IEntityTicker
{
    /// <summary>Runs from <see cref="Entity.Tick" />, before the base tick.</summary>
    void OnTick(Entity self) { }

    /// <summary>Runs from <c>EntityLiving.TickMovement</c>, before the shared movement logic.</summary>
    void OnTickMovement(EntityLiving self) { }

    /// <summary>Runs from <c>EntityLiving.TickLiving</c> — the AI tick.</summary>
    void OnTickLiving(EntityLiving self) { }
}
