namespace OmniBlock.Entities;

/// <summary>
///     Composable capability for single-shot reactions to an entity-level event: creation, death,
///     damage, a lightning strike, a status byte from the server.
/// </summary>
public interface IEntityLifecycle
{
    /// <summary>
    ///     Called by <see cref="EntityType.Create" /> once the entity is constructed, for
    ///     per-individual state that must exist before anything reads it: a slime's size, which
    ///     decides its body, or a TNT's lit fuse. Unlike <see cref="OnPostSpawn" /> this runs for
    ///     every entity however it came to exist, so one split off another or loaded from disk is
    ///     initialised too, then overwritten by what it was split from or read back.
    /// </summary>
    void OnCreated(Entity self)
    {
    }

    /// <summary>Called before the mob is flagged dead, whether killed, despawned, or removed.</summary>
    void OnMarkDead(EntityLiving self)
    {
    }

    /// <summary>
    ///     Called before damage is applied, with whatever dealt it (<c>null</c> for the environment).
    ///     A zombie pigman uses this to anger its whole neighbourhood at the attacker.
    /// </summary>
    void OnDamaged(EntityLiving self, Entity? attacker, int amount)
    {
    }

    /// <summary>
    ///     Adjusts incoming damage before any of it lands. A wolf halves everything but a player's
    ///     own hand.
    /// </summary>
    int ModifyDamage(EntityLiving self, Entity? attacker, int amount) => amount;

    /// <summary>
    ///     Called only once damage has actually landed, unlike <see cref="OnDamaged" />, which runs
    ///     whether or not the hit gets through the hurt-resistance window. Retaliation belongs here:
    ///     a wolf pack turns on an attacker that drew blood, not one that swung too soon.
    /// </summary>
    void OnDamageApplied(EntityLiving self, Entity? attacker, int amount)
    {
    }

    /// <summary>
    ///     Handles a server-sent entity status byte on the client: the particle bursts and one-shot
    ///     animations that have no state of their own. Returning <c>true</c> means it was handled and
    ///     the default statuses are not consulted.
    /// </summary>
    bool OnEntityStatus(EntityLiving self, sbyte status) => false;

    /// <summary>
    ///     Called once after a natural spawn places the mob, for state rolled per individual instead
    ///     of declared: a sheep's fleece colour, a spider's rider.
    /// </summary>
    void OnPostSpawn(EntityLiving self)
    {
    }

    /// <summary>
    ///     Called when lightning strikes the mob. Returning <c>true</c> means the behavior fully
    ///     handled the strike and the default fire/damage response is skipped.
    /// </summary>
    bool OnStruckByLightning(EntityLiving self, Entity bolt) => false;

    /// <summary>
    ///     Replaces how a <em>non-living</em> entity takes damage, or <c>null</c> for the default
    ///     response. The return value is the <c>Damage</c> answer: whether the hit registered. A
    ///     dropped item has five hit points against fire and explosions. Living entities never consult
    ///     this; their damage pipeline has its own hooks.
    /// </summary>
    bool? Damage(Entity self, Entity? attacker, int amount) => null;

    /// <summary>
    ///     Plays the client-side reaction to a hit the server announced, returning <c>true</c> when
    ///     handled. A boat rocks, replaying the wobble the real hit produced.
    /// </summary>
    bool OnAnimateHurt(Entity self) => false;

    /// <summary>
    ///     Runs as a <em>non-living</em> entity is removed, however it was removed. A chest minecart
    ///     spills its cargo here, so every way of destroying it scatters.
    /// </summary>
    void OnRemoved(Entity self)
    {
    }
}
