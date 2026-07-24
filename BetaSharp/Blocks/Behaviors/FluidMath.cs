using BetaSharp.Blocks.Materials;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;
using Silk.NET.Maths;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Fluid geometry shared by both the flowing and stationary variants of water/lava. Every
///     function is parameterized by <see cref="Material" /> rather than a block instance — the
///     original per-instance <c>getFlow</c> never actually depended on anything but the material, so
///     there's no need for separate behavior instances per fluid type here. Consumed cross-assembly
///     by the client's fluid/particle renderers and by <c>Entity</c>'s swim-height check.
/// </summary>
public static class FluidMath
{
    public static float GetFluidHeightFromMeta(int meta)
    {
        if (meta >= 8)
        {
            meta = 0;
        }

        return (meta + 1) / 9.0F;
    }

    public static double GetFlowingAngle(IBlockReader reader, int x, int y, int z, Material material)
    {
        Vector3D<double> flowVec = GetFlow(reader, x, y, z, material);
        return flowVec is { X: 0.0D, Z: 0.0D } ? -1000.0D : Math.Atan2(flowVec.Z, flowVec.X) - Math.PI * 0.5D;
    }

    private static Vector3D<double> GetFlow(IBlockReader reader, int x, int y, int z, Material material)
    {
        Vector3D<double> flowVector = new(0.0);
        int depth = GetLiquidDepth(reader, x, y, z, material);

        for (int direction = 0; direction < 4; ++direction)
        {
            int neighborX = x;
            int neighborZ = z;
            switch (direction)
            {
                case 0:
                    neighborX = x - 1;
                    break;
                case 1:
                    neighborZ = z - 1;
                    break;
                case 2:
                    ++neighborX;
                    break;
                case 3:
                    ++neighborZ;
                    break;
            }

            int neighborDepth = GetLiquidDepth(reader, neighborX, y, neighborZ, material);
            int depthDiff;
            if (neighborDepth < 0)
            {
                if (reader.GetMaterial(neighborX, y, neighborZ).BlocksMovement)
                {
                    continue;
                }

                neighborDepth = GetLiquidDepth(reader, neighborX, y - 1, neighborZ, material);
                if (neighborDepth < 0)
                {
                    continue;
                }

                depthDiff = neighborDepth - (depth - 8);
            }
            else
            {
                depthDiff = neighborDepth - depth;
            }

            flowVector += new Vector3D<double>((neighborX - x) * depthDiff, 0.0, (neighborZ - z) * depthDiff);
        }

        if (reader.GetBlockMeta(x, y, z) < 8) return Normalize(flowVector);

        bool hasAdjacentSolid =
            IsSolidFace(reader, x, y, z - 1, 2, material) ||
            IsSolidFace(reader, x, y, z + 1, 3, material) ||
            IsSolidFace(reader, x - 1, y, z, 4, material) ||
            IsSolidFace(reader, x + 1, y, z, 5, material) ||
            IsSolidFace(reader, x, y + 1, z - 1, 2, material) ||
            IsSolidFace(reader, x, y + 1, z + 1, 3, material) ||
            IsSolidFace(reader, x - 1, y + 1, z, 4, material) ||
            IsSolidFace(reader, x + 1, y + 1, z, 5, material);

        if (hasAdjacentSolid)
        {
            flowVector = Normalize(flowVector) + new Vector3D<double>(0.0, -0.6, 0.0);
        }

        return Normalize(flowVector);
    }

    /// <summary>
    ///     Lava meeting water solidifies: to <paramref name="sourceSolidified" /> (vanilla obsidian)
    ///     at meta 0 (source), or <paramref name="flowSolidified" /> (vanilla cobblestone) at low
    ///     flowing meta. Both are the caller's own configured params (see
    ///     <see cref="StationaryFluidBehavior" />/<see cref="FlowingFluidBehavior" />) — this class
    ///     has no behavior-level state of its own, it's shared geometry/rules only.
    /// </summary>
    public static void CheckBlockCollisions(Block block, IBlockReader reader, IBlockWriter writer, WorldEventBroadcaster broadcaster, int x, int y, int z, Block sourceSolidified, Block flowSolidified)
    {
        if (reader.GetBlockId(x, y, z) != block.id) return;
        if (block.material != Material.Lava) return;

        bool hasWaterAdjacent =
            reader.GetMaterial(x, y, z - 1) == Material.Water ||
            reader.GetMaterial(x, y, z + 1) == Material.Water ||
            reader.GetMaterial(x - 1, y, z) == Material.Water ||
            reader.GetMaterial(x + 1, y, z) == Material.Water ||
            reader.GetMaterial(x, y + 1, z) == Material.Water;

        if (!hasWaterAdjacent) return;

        int meta = reader.GetBlockMeta(x, y, z);
        if (meta == 0)
        {
            writer.SetBlock(x, y, z, sourceSolidified.id);
            Fizz(broadcaster, x, y, z);
            return;
        }

        if (meta > 4) return;

        writer.SetBlock(x, y, z, flowSolidified.id);
        Fizz(broadcaster, x, y, z);
    }

    public static int GetTexture(Block block, Side side) => side != Side.Down && side != Side.Up ? block.TextureId + 1 : block.TextureId;

    public static bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility)
    {
        Material mat = reader.GetMaterial(x, y, z);
        return mat != block.material && mat != Material.Ice && (side == Side.Up || defaultVisibility);
    }

    public static float GetLuminance(ILightProvider lighting, int x, int y, int z)
    {
        float luminance = lighting.GetLuminance(x, y, z);
        float luminanceAbove = lighting.GetLuminance(x, y + 1, z);
        return luminance > luminanceAbove ? luminance : luminanceAbove;
    }

    public static Vec3D ApplyVelocity(IBlockReader reader, int x, int y, int z, Material material)
    {
        Vector3D<double> flow = GetFlow(reader, x, y, z, material);
        return new Vec3D(flow.X, flow.Y, flow.Z);
    }

    public static void Fizz(WorldEventBroadcaster broadcaster, int x, int y, int z)
    {
        broadcaster.WorldEvent(1004, x, y, z, 0);

        for (int particleIndex = 0; particleIndex < 8; ++particleIndex)
        {
            broadcaster.AddParticle("largesmoke", x + Random.Shared.NextDouble(), y + 1.2D, z + Random.Shared.NextDouble(), 0.0D, 0.0D, 0.0D);
        }
    }

    public static void RandomDisplayTick(Block block, OnTickEvent @event)
    {
        if (block.material == Material.Water && Random.Shared.Next(64) == 0)
        {
            int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
            if (meta is > 0 and < 8)
            {
                @event.World.Broadcaster.PlaySoundAtPos(
                    @event.X + 0.5F,
                    @event.Y + 0.5F,
                    @event.Z + 0.5F,
                    "liquid.water",
                    Random.Shared.NextSingle() * 0.25F + 12.0F / 16.0F,
                    Random.Shared.NextSingle() * 1.0F + 0.5F
                );
            }
        }

        if (block.material != Material.Lava ||
            @event.World.Reader.GetMaterial(@event.X, @event.Y + 1, @event.Z) != Material.Air ||
            @event.World.Reader.IsOpaque(@event.X, @event.Y + 1, @event.Z) || Random.Shared.Next(100) != 0)
        {
            return;
        }

        double particleX = @event.X + Random.Shared.NextSingle();
        double particleY = @event.Y + block.BoundingBox.MaxY;
        double particleZ = @event.Z + Random.Shared.NextSingle();
        @event.World.Broadcaster.AddParticle("lava", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);
    }

    private static int GetLiquidDepth(IBlockReader reader, int x, int y, int z, Material material)
    {
        if (reader.GetMaterial(x, y, z) != material)
        {
            return -1;
        }

        int depth = reader.GetBlockMeta(x, y, z);
        if (depth >= 8)
        {
            depth = 0;
        }

        return depth;
    }

    private static bool IsSolidFace(IBlockReader reader, int x, int y, int z, int face, Material material)
    {
        Material mat = reader.GetMaterial(x, y, z);
        return mat != material && mat != Material.Ice && (face == 1 || mat.IsSolid);
    }

    private static Vector3D<double> Normalize(Vector3D<double> vec)
    {
        double length = MathHelper.Sqrt(vec.X * vec.X + vec.Y * vec.Y + vec.Z * vec.Z);
        return length < 1.0E-4D ? new Vector3D<double>(0.0) : new Vector3D<double>(vec.X / length, vec.Y / length, vec.Z / length);
    }
}
