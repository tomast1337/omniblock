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

    /// <summary>
    ///     Runs from <c>EntityLiving.TickLiving</c> — the AI tick. Returning <c>true</c> means the
    ///     behavior <em>is</em> the mob's AI and the default idle logic — ageing, the despawn check,
    ///     glancing at nearby players — is skipped, matching a mob that overrode <c>TickLiving</c>
    ///     without calling base. A ticker that replaces the AI owns whatever it still wants from the
    ///     default, <see cref="EntityLiving.TickDespawn" /> included.
    /// </summary>
    bool OnTickLiving(EntityLiving self) => false;

    /// <summary>
    ///     Runs immediately after the AI tick — where a mob that overrode <c>TickLiving</c> and
    ///     <em>did</em> call base put the rest of its work. <see cref="OnTickLiving" /> cannot serve
    ///     for that: it runs first, and for a creature the pathfinding AI has not happened yet.
    /// </summary>
    void AfterTickLiving(EntityLiving self) { }

    /// <summary>
    ///     Runs at the end of <c>EntityLiving.Tick</c>, after movement and AI have resolved — where
    ///     a mob that overrode <c>Tick</c> put the work it did after calling base.
    /// </summary>
    void OnTickEnd(EntityLiving self) { }

    /// <summary>
    ///     The idle sound to make this tick, or <c>null</c> to use the one the definition declares.
    ///     A hook rather than a field because the choice can be stateful: a wolf growls when angry
    ///     and whines when hurt.
    /// </summary>
    string? LivingSound(EntityLiving self) => null;
}
