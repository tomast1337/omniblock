using BetaSharp.Blocks.Materials;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Source water/lava: reverts to its flowing counterpart (<c>block.id - 1</c>) whenever a
///     neighbor changes (the "check every tick" quasi-flow-restart vanilla does via neighbor
///     notification), and lava sources randomly ignite nearby flammable terrain.
///     <para>
///         Ignition target block and the two lava/water-contact solidification products are all
///         required, (see <c>BehaviorRegistry</c>'s <c>"stationary_fluid"</c> entry).
///     </para>
/// </summary>
public sealed class StationaryFluidBehavior(Block ignitionTarget, Block sourceSolidified, Block flowSolidified) : IBlockPhysics, IBlockVisuals, IBlockLifecycle, IBlockTicker
{
    public void OnPlaced(Block block, OnPlacedEvent @event)
        => FluidMath.CheckBlockCollisions(block, @event.World.Reader, @event.World.Writer, @event.World.Broadcaster, @event.X, @event.Y, @event.Z, sourceSolidified, flowSolidified);

    public bool HasCollision(Block block, int meta, bool allowLiquids, bool defaultHasCollision) => allowLiquids && meta == 0;

    public Vec3D ApplyVelocity(Block block, OnApplyVelocityEvent @event, Vec3D defaultVelocity) => FluidMath.ApplyVelocity(@event.World.Reader, @event.X, @event.Y, @event.Z, block.material);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        FluidMath.CheckBlockCollisions(block, @event.World.Reader, @event.World.Writer, @event.World.Broadcaster, @event.X, @event.Y, @event.Z, sourceSolidified, flowSolidified);
        if (@event.World.Reader.GetBlockId(@event.X, @event.Y, @event.Z) != block.id)
        {
            return;
        }

        ConvertToFlowing(block, @event);
    }

    public void RandomDisplayTick(Block block, OnTickEvent @event) => FluidMath.RandomDisplayTick(block, @event);

    public void OnTick(Block block, OnTickEvent @event)
    {
        (int x, int y, int z) = (@event.X, @event.Y, @event.Z);

        if (block.material != Material.Lava) return;

        int attempts = @event.World.Random.NextInt(3);

        for (int attempt = 0; attempt < attempts; ++attempt)
        {
            x += @event.World.Random.NextInt(3) - 1;
            ++y;
            z += @event.World.Random.NextInt(3) - 1;
            int neighborBlockId = @event.World.Reader.GetBlockId(x, y, z);
            if (neighborBlockId == 0)
            {
                if (!IsFlammable(@event.World.Reader, x - 1, y, z) && !IsFlammable(@event.World.Reader, x + 1, y, z) && !IsFlammable(@event.World.Reader, x, y, z - 1) &&
                    !IsFlammable(@event.World.Reader, x, y, z + 1) && !IsFlammable(@event.World.Reader, x, y - 1, z) && !IsFlammable(@event.World.Reader, x, y + 1, z))
                {
                    continue;
                }

                @event.World.Writer.SetBlock(x, y, z, ignitionTarget.id);
                return;
            }

            if (Block.Blocks[neighborBlockId].material.BlocksMovement)
            {
                return;
            }
        }
    }

    public int GetTexture(Block block, Side side, int defaultTexture) => FluidMath.GetTexture(block, side);

    public bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility)
        => FluidMath.IsSideVisible(block, reader, x, y, z, side, defaultVisibility);

    public float GetLuminance(Block block, ILightProvider lighting, int x, int y, int z, float defaultLuminance) => FluidMath.GetLuminance(lighting, x, y, z);

    private static void ConvertToFlowing(Block block, OnTickEvent @event)
    {
        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        @event.World.Writer.SetBlockWithoutNotifyingNeighbors(@event.X, @event.Y, @event.Z, block.id - 1, meta, false);
        @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.id - 1, block.TickRate);
    }

    private static bool IsFlammable(IBlockReader world, int x, int y, int z) => world.GetMaterial(x, y, z).IsBurnable;
}
