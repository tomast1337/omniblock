namespace OmniBlock.Entities;

/// <summary>
///     Composable per-tick behavior. Declared on <see cref="Entity" />, not
///     <see cref="EntityLiving" />, because every entity kind ticks: projectiles, vehicles and
///     falling blocks as well as mobs.
///     <para>
///         Tickers are shared across every entity of a type, so they hold no per-entity fields.
///         Mutable state goes in <see cref="State.EntityState" />, addressed by handles the ticker
///         resolved once at load.
///     </para>
/// </summary>
public interface IEntityTicker
{
    /// <summary>Runs from <see cref="Entity.Tick" />, before the base tick.</summary>
    void OnTick(Entity self)
    {
    }

    /// <summary>
    ///     Runs from <see cref="Entity.Tick" /> first of all. Returning <c>true</c> means the
    ///     behavior <em>is</em> the entity's whole tick, and the base tick (ageing, water, fire, the
    ///     void check) is skipped. Primed TNT works this way: its tick is ballistics and a fuse.
    /// </summary>
    bool OnTickEntity(Entity self) => false;

    /// <summary>Runs from <c>EntityLiving.TickMovement</c>, before the shared movement logic.</summary>
    void OnTickMovement(EntityLiving self)
    {
    }

    /// <summary>
    ///     Runs from <c>EntityLiving.TickLiving</c>, the AI tick. Returning <c>true</c> means the
    ///     behavior <em>is</em> the mob's AI, and the default idle logic (ageing, the despawn check,
    ///     glancing at nearby players) is skipped. A ticker that replaces the AI must call whatever
    ///     it still wants from the default, <see cref="EntityLiving.TickDespawn" /> included.
    /// </summary>
    bool OnTickLiving(EntityLiving self) => false;

    /// <summary>
    ///     Runs immediately after the AI tick. <see cref="OnTickLiving" /> cannot serve for work that
    ///     depends on it: that hook runs first, before a creature's pathfinding has happened.
    /// </summary>
    void AfterTickLiving(EntityLiving self)
    {
    }

    /// <summary>
    ///     Runs at the end of <c>EntityLiving.Tick</c>, after movement and AI have resolved.
    /// </summary>
    void OnTickEnd(EntityLiving self)
    {
    }

    /// <summary>
    ///     The idle sound to make this tick, or <c>null</c> to use the one the definition declares.
    ///     A hook, not a field, because the choice can be stateful: a wolf growls when angry and
    ///     whines when hurt.
    /// </summary>
    string? LivingSound(EntityLiving self) => null;
}
