using OmniBlock.Blocks;
using OmniBlock.Util.Maths;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     What it means to be a farm animal: it spawns on well-lit grass and paths towards grass in
///     preference to anything else.
/// </summary>
public sealed class GrazingAnimalBehavior : IEntityPhysics
{
    public float? GetBlockPathWeight(EntityLiving self, int x, int y, int z) =>
        StandsOnGrass(self, x, y, z) ? 10.0F : self.World.Lighting.GetLuminance(x, y, z) - 0.5F;

    public bool? CanSpawn(EntityLiving self)
    {
        int x = MathHelper.Floor(self.X);
        int y = MathHelper.Floor(self.BoundingBox.MinY);
        int z = MathHelper.Floor(self.Z);

        // The path-weight floor the creature spawn rule also applies is not repeated: grass scores
        // 10, so anything reaching this point already clears it.
        return StandsOnGrass(self, x, y, z)
               && self.World.Reader.GetBrightness(x, y, z) > 8
               && self.World.Entities.CanSpawnEntity(self.BoundingBox)
               && self.World.Entities.GetEntityCollisionsScratch(self, self.BoundingBox).Count == 0
               && !self.World.Reader.IsMaterialInBox(self.BoundingBox, m => m.IsFluid);
    }

    private static bool StandsOnGrass(EntityLiving self, int x, int y, int z) =>
        self.World.Reader.GetBlockId(x, y - 1, z) == BlockRegistry.Get("grass_block").Id;
}
