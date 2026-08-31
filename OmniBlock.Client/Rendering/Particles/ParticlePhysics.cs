using OmniBlock.Blocks;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Client.Rendering.Particles;

public static class ParticlePhysics
{
    public static void MoveWithCollision(
        ParticleBuffer buf, int i, IWorldContext world)
    {
        double nx = buf.X[i] + buf.VelX[i];
        double ny = buf.Y[i] + buf.VelY[i];
        double nz = buf.Z[i] + buf.VelZ[i];

        int bx = MathHelper.Floor(nx);
        int by = MathHelper.Floor(ny);
        int bz = MathHelper.Floor(nz);

        int blockId = world.Reader.GetBlockId(bx, by, bz);
        bool solid = IsSolid(blockId);

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
            int yBlockId = world.Reader.GetBlockId(MathHelper.Floor(buf.X[i]), by, MathHelper.Floor(buf.Z[i]));
            bool ySolid = IsSolid(yBlockId);
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
            int xBlockId = world.Reader.GetBlockId(bx, MathHelper.Floor(buf.Y[i]), MathHelper.Floor(buf.Z[i]));
            bool xSolid = IsSolid(xBlockId);
            if (!xSolid)
            {
                buf.X[i] += buf.VelX[i];
            }
            else
            {
                buf.VelX[i] = 0;
            }

            // Z axis
            int zBlockId = world.Reader.GetBlockId(MathHelper.Floor(buf.X[i]), MathHelper.Floor(buf.Y[i]), bz);
            bool zSolid = IsSolid(zBlockId);
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

    private static bool IsSolid(int blockId) =>
        blockId > 0
        && BlockRegistry.TryGetByProtocolId(blockId, out Block? block)
        && block.Material.BlocksMovement;
}
