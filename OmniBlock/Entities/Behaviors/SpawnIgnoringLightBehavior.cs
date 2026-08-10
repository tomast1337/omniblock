namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Spawns wherever the body fits, ignoring the darkness rule monsters normally obey, as the
///     zombie pigman does in the lit Nether. Optionally gated on the world not being peaceful, and on
///     a die roll for something that should stay rare.
/// </summary>
public sealed class SpawnIgnoringLightBehavior(bool requiresDifficulty, int chanceOneIn) : IEntityPhysics
{
    public bool? CanSpawn(EntityLiving self) =>
        // Rolled first, and only when it can fail, so a mob without a chance gate draws no random
        // number and its spawn attempts stay on the sequence they were on before.
        (chanceOneIn <= 1 || self.Random.NextInt(chanceOneIn) == 0)
        && (!requiresDifficulty || self.World.Difficulty > 0)
        && self.World.Entities.CanSpawnEntity(self.BoundingBox)
        && self.World.Entities.GetEntityCollisionsScratch(self, self.BoundingBox).Count == 0
        && !self.World.Reader.IsMaterialInBox(self.BoundingBox, m => m.IsFluid);
}
