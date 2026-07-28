namespace BetaSharp.Entities;

/// <summary>
///     Composable capability for single-shot reactions to a mob-level event, for logic that is
///     self-contained enough not to need the mob's own synced state (Slime's death split, Pig's
///     lightning conversion). Mobs whose reaction drives ongoing synced state — Creeper's powered
///     flag, Wolf's anger — keep their own override instead.
/// </summary>
public interface IEntityLifecycle
{
    /// <summary>
    ///     Called by <see cref="EntityType.Create" /> once the mob is constructed, for per-individual
    ///     state that must exist before anything reads it — a slime's size, which decides its body.
    ///     Unlike <see cref="OnPostSpawn" /> this runs for every mob however it came to exist, so a
    ///     slime split off another or loaded from disk is sized too (and then overwritten, exactly as
    ///     the constructor's own roll used to be).
    /// </summary>
    void OnCreated(EntityLiving self) { }

    /// <summary>Called before the mob is flagged dead, whether killed, despawned, or removed.</summary>
    void OnMarkDead(EntityLiving self) { }

    /// <summary>
    ///     Called before damage is applied, with whatever dealt it (<c>null</c> for the environment).
    ///     A zombie pigman uses this to anger its whole neighbourhood at the attacker.
    /// </summary>
    void OnDamaged(EntityLiving self, Entity? attacker, int amount) { }

    /// <summary>
    ///     Adjusts incoming damage before any of it lands — resistance to everything but a player's
    ///     own hand, which is what makes a wolf hard for other mobs to kill.
    /// </summary>
    int ModifyDamage(EntityLiving self, Entity? attacker, int amount) => amount;

    /// <summary>
    ///     Called only once damage has actually landed, unlike <see cref="OnDamaged" />, which runs
    ///     whether or not the hit gets through the hurt-resistance window. Retaliation belongs here:
    ///     a wolf pack turns on an attacker that drew blood, not one that swung too soon.
    /// </summary>
    void OnDamageApplied(EntityLiving self, Entity? attacker, int amount) { }

    /// <summary>
    ///     Handles a server-sent entity status byte on the client — the particle bursts and one-shot
    ///     animations that have no state of their own. Returning <c>true</c> means it was handled and
    ///     the default statuses are not consulted.
    /// </summary>
    bool OnEntityStatus(EntityLiving self, sbyte status) => false;

    /// <summary>
    ///     Called once after a natural spawn places the mob, for state that is rolled per individual
    ///     rather than declared — a sheep's fleece colour, a spider's rider.
    /// </summary>
    void OnPostSpawn(EntityLiving self) { }

    /// <summary>
    ///     Called when lightning strikes the mob. Returning <c>true</c> means the behavior fully
    ///     handled the strike and the default fire/damage response is skipped.
    /// </summary>
    bool OnStruckByLightning(EntityLiving self, EntityLightningBolt bolt) => false;
}
