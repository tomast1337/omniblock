namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Spawns anywhere the body physically fits, without the usual rejection of a spot full of
///     fluid — a squid needs the water the default rule refuses. This was the whole of the
///     <c>EntityWaterMob</c> spawn override.
/// </summary>
public sealed class SpawnInFluidBehavior : IEntityPhysics
{
    public bool? CanSpawn(EntityLiving self) => self.World.Entities.CanSpawnEntity(self.BoundingBox);
}
