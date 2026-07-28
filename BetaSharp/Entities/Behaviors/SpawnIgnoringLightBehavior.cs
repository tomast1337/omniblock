namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Spawns wherever the body fits, ignoring the darkness rule monsters normally obey — a zombie
///     pigman, which the Nether produces in full light. Optionally gated on the world not being
///     peaceful.
/// </summary>
public sealed class SpawnIgnoringLightBehavior(bool requiresDifficulty) : IEntityPhysics
{
    public bool? CanSpawn(EntityLiving self) =>
        (!requiresDifficulty || self.World.Difficulty > 0)
        && self.World.Entities.CanSpawnEntity(self.BoundingBox)
        && self.World.Entities.GetEntityCollisionsScratch(self, self.BoundingBox).Count == 0
        && !self.World.Reader.IsMaterialInBox(self.BoundingBox, m => m.IsFluid);
}
