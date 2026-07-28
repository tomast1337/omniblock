using BetaSharp.Blocks;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityAnimal(IWorldContext world, EntityType? type = null) : EntityCreature(world, type)
{
    protected override float GetBlockPathWeight(int x, int y, int z) => World.Reader.GetBlockId(x, y - 1, z) == BlockRegistry.Get("grass_block").id ? 10.0F : World.Lighting.GetLuminance(x, y, z) - 0.5F;

    protected override bool CanSpawnHere()
    {
        int x = MathHelper.Floor(X);
        int y = MathHelper.Floor(BoundingBox.MinY);
        int z = MathHelper.Floor(Z);
        return World.Reader.GetBlockId(x, y - 1, z) == BlockRegistry.Get("grass_block").id && World.Reader.GetBrightness(x, y, z) > 8 && base.CanSpawnHere();
    }
}
