using OmniBlock.Blocks.Materials;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Source water/lava: reverts to its flowing counterpart (<c>block.id - 1</c>) whenever a
///     neighbor changes (the "check every tick" quasi-flow-restart vanilla does via neighbor
///     notification), and lava sources randomly ignite nearby flammable terrain.
///     <para>
///         Ignition target block and the two lava/water-contact solidification products are all
///         required, (see <c>BehaviorRegistry</c>'s <c>"stationary_fluid"</c> entry).
///     </para>
/// </summary>
public sealed class StationaryFluidBehavior(Block ignitionTarget, Block sourceSolidified, Block flowSolidified, int still, int flowing) : BlockRuntimeBehavior, IBlockPhysics, IBlockVisuals, IBlockLifecycle, IBlockTicker
{
    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        FluidMath.CheckBlockCollisions(block, @event.World.Reader, @event.World.Writer, @event.World.Broadcaster, @event.X, @event.Y, @event.Z, sourceSolidified, flowSolidified);
    }

    public bool HasCollision(Block block, int meta, bool allowLiquids, bool defaultHasCollision)
    {
        return allowLiquids && meta == 0;
    }

    public Vec3D ApplyVelocity(Block block, OnApplyVelocityEvent @event, Vec3D defaultVelocity)
    {
        return FluidMath.ApplyVelocity(@event.World.Reader, @event.X, @event.Y, @event.Z, block.Material);
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        FluidMath.CheckBlockCollisions(block, @event.World.Reader, @event.World.Writer, @event.World.Broadcaster, @event.X, @event.Y, @event.Z, sourceSolidified, flowSolidified);
        if (@event.World.Reader.GetBlockId(@event.X, @event.Y, @event.Z) != block.Id) return;

        ConvertToFlowing(block, @event);
    }

    public void RandomDisplayTick(Block block, OnTickEvent @event)
    {
        FluidMath.RandomDisplayTick(block, @event);
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        var (x, y, z) = (@event.X, @event.Y, @event.Z);

        if (block.Material != Material.Lava) return;

        var attempts = @event.World.Random.NextInt(3);

        for (var attempt = 0; attempt < attempts; ++attempt)
        {
            x += @event.World.Random.NextInt(3) - 1;
            ++y;
            z += @event.World.Random.NextInt(3) - 1;
            var neighborBlockId = @event.World.Reader.GetBlockId(x, y, z);
            if (neighborBlockId == 0)
            {
                if (!IsFlammable(@event.World.Reader, x - 1, y, z) && !IsFlammable(@event.World.Reader, x + 1, y, z) && !IsFlammable(@event.World.Reader, x, y, z - 1) &&
                    !IsFlammable(@event.World.Reader, x, y, z + 1) && !IsFlammable(@event.World.Reader, x, y - 1, z) && !IsFlammable(@event.World.Reader, x, y + 1, z))
                    continue;

                @event.World.Writer.SetBlock(x, y, z, ignitionTarget.Id);
                return;
            }

            if (Blocks.GetByProtocolId(neighborBlockId).Material.BlocksMovement) return;
        }
    }

    public int GetTexture(Block block, Side side, int defaultTexture)
    {
        return FluidMath.GetTexture(side, still, flowing);
    }

    public bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility)
    {
        return FluidMath.IsSideVisible(block, reader, x, y, z, side, defaultVisibility);
    }

    public float GetLuminance(Block block, ILightProvider lighting, int x, int y, int z, float defaultLuminance)
    {
        return FluidMath.GetLuminance(lighting, x, y, z);
    }

    public LightLevels GetLightLevels(Block block, ILightProvider lighting, int x, int y, int z, LightLevels defaultLevels)
    {
        return FluidMath.GetLightLevels(lighting, x, y, z, Blocks.GetLightEmission(block.Id));
    }

    private static void ConvertToFlowing(Block block, OnTickEvent @event)
    {
        var meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        @event.World.Writer.SetBlockWithoutNotifyingNeighbors(@event.X, @event.Y, @event.Z, block.Id - 1, meta, false);
        @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.Id - 1, block.TickRate);
    }

    private static bool IsFlammable(IBlockReader world, int x, int y, int z)
    {
        return world.GetMaterial(x, y, z).IsBurnable;
    }
}