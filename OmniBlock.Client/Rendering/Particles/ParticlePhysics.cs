using OmniBlock.Blocks;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Client.Rendering.Particles;

public static class ParticlePhysics
{
    public static void MoveWithCollision(
        ParticleBuffer buf, int i, IWorldContext world)
    {
        var nx = buf.X[i] + buf.VelX[i];
        var ny = buf.Y[i] + buf.VelY[i];
        var nz = buf.Z[i] + buf.VelZ[i];

        var bx = MathHelper.Floor(nx);
        var by = MathHelper.Floor(ny);
        var bz = MathHelper.Floor(nz);

        var blockId = world.Reader.GetBlockId(bx, by, bz);
        var solid = IsSolid(world.Content.Blocks, blockId);

        if (!solid)
        {
            buf.X[i] = nx;
            buf.Y[i] = ny;
            buf.Z[i] = nz;
            buf.OnGround[i] = false;
        }
        else
        {
            // Try each axis independently using the already-computed target block coords
            // Y axis
            var yBlockId = world.Reader.GetBlockId(MathHelper.Floor(buf.X[i]), by, MathHelper.Floor(buf.Z[i]));
            var ySolid = IsSolid(world.Content.Blocks, yBlockId);
            if (!ySolid)
            {
                buf.Y[i] += buf.VelY[i];
                buf.OnGround[i] = false;
            }
            else
            {
                buf.OnGround[i] = buf.VelY[i] < 0;
                buf.VelY[i] = 0;
            }

            // X axis
            var xBlockId = world.Reader.GetBlockId(bx, MathHelper.Floor(buf.Y[i]), MathHelper.Floor(buf.Z[i]));
            var xSolid = IsSolid(world.Content.Blocks, xBlockId);
            if (!xSolid)
            {
                buf.X[i] += buf.VelX[i];
            }
            else
            {
                buf.VelX[i] = 0;
            }

            // Z axis
            var zBlockId = world.Reader.GetBlockId(MathHelper.Floor(buf.X[i]), MathHelper.Floor(buf.Y[i]), bz);
            var zSolid = IsSolid(world.Content.Blocks, zBlockId);
            if (!zSolid)
            {
                buf.Z[i] += buf.VelZ[i];
            }
            else
            {
                buf.VelZ[i] = 0;
            }
        }
    }

    private static bool IsSolid(IBlockRuntimeView blocks, int blockId) =>
        blockId > 0
        && blocks.TryGetByProtocolId(blockId, out var block)
        && block.Material.BlocksMovement;
}
