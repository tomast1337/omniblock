namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Spawns anywhere the body physically fits, without the usual rejection of a spot full of
///     fluid, which is what a squid needs.
/// </summary>
public sealed class SpawnInFluidBehavior : IEntityPhysics
{
    public bool? CanSpawn(EntityLiving self) => self.World.Entities.CanSpawnEntity(self.BoundingBox);
}
