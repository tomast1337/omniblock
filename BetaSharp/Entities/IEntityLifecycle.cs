namespace BetaSharp.Entities;

/// <summary>
///     Composable capability for single-shot reactions to a mob-level event, for logic that is
///     self-contained enough not to need the mob's own synced state (Slime's death split, Pig's
///     lightning conversion). Mobs whose reaction drives ongoing synced state — Creeper's powered
///     flag, Wolf's anger — keep their own override instead.
/// </summary>
public interface IEntityLifecycle
{
    /// <summary>Called before the mob is flagged dead, whether killed, despawned, or removed.</summary>
    void OnMarkDead(EntityLiving self) { }

    /// <summary>
    ///     Called when lightning strikes the mob. Returning <c>true</c> means the behavior fully
    ///     handled the strike and the default fire/damage response is skipped.
    /// </summary>
    bool OnStruckByLightning(EntityLiving self, EntityLightningBolt bolt) => false;
}
