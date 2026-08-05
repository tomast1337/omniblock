using BetaSharp.Blocks.Materials;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Chunks;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Flowing (non-source) water/lava: spreads outward from the shortest path to a gap, drops to
///     source state when fed from 2+ adjacent sources (water) or falling from a source above, and
///     reverts to its stationary/source counterpart (<c>block.id + 1</c>, the vanilla water/lava
///     id-pairing convention) once its level stabilizes. All per-tick state is thread-local and
///     reset at the top of every call.
///     <para>
///         Pass-through obstacle set and the two lava/water-contact solidification products are
///         all required, (see <c>BehaviorRegistry</c>'s <c>"flowing_fluid"</c> entry).
///     </para>
/// </summary>
public sealed class FlowingFluidBehavior(Block[] passable, Block sourceSolidified, Block flowSolidified, int still, int flowing) : IBlockPhysics, IBlockVisuals, IBlockLifecycle, IBlockTicker
{
    private readonly ThreadLocal<int> _adjacentSources = new(() => 0);
    private readonly ThreadLocal<int[]> _distanceToGap = new(() => new int[4]);
    private readonly ThreadLocal<bool[]> _spread = new(() => new bool[4]);

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        FluidMath.CheckBlockCollisions(block, @event.World.Reader, @event.World.Writer, @event.World.Broadcaster, @event.X, @event.Y, @event.Z, sourceSolidified, flowSolidified);
        int placedId = @event.World.Reader.GetBlockId(@event.X, @event.Y, @event.Z);
        if (placedId == block.Id && !@event.World.IsRemote)
        {
            @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.Id, block.TickRate);
        }
    }

    public bool HasCollision(Block block, int meta, bool allowLiquids, bool defaultHasCollision) => allowLiquids && meta == 0;

    public Vec3D ApplyVelocity(Block block, OnApplyVelocityEvent @event, Vec3D defaultVelocity) => FluidMath.ApplyVelocity(@event.World.Reader, @event.X, @event.Y, @event.Z, block.Material);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        FluidMath.CheckBlockCollisions(block, @event.World.Reader, @event.World.Writer, @event.World.Broadcaster, @event.X, @event.Y, @event.Z, sourceSolidified, flowSolidified);
        if (@event.World.Reader.GetBlockId(@event.X, @event.Y, @event.Z) == block.Id)
        {
            @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.Id, block.TickRate);
        }
    }

    public void RandomDisplayTick(Block block, OnTickEvent @event) => FluidMath.RandomDisplayTick(block, @event);

    public void OnTick(Block block, OnTickEvent ctx)
    {
        int currentState = GetLiquidState(ctx.World.Reader, ctx.X, ctx.Y, ctx.Z, block.Material);
        sbyte spreadRate = 1;
        if (block.Material == Material.Lava && !ctx.World.Dimension.EvaporatesWater)
        {
            spreadRate = 2;
        }

        bool convertToSource = true;
        int newLevel;
        if (currentState > 0)
        {
            const int minDepth = -100;
            _adjacentSources.Value = 0;
            int lowestNeighborDepth = GetLowestDepth(ctx.World.Reader, ctx.X - 1, ctx.Y, ctx.Z, minDepth, block.Material);
            lowestNeighborDepth = GetLowestDepth(ctx.World.Reader, ctx.X + 1, ctx.Y, ctx.Z, lowestNeighborDepth, block.Material);
            lowestNeighborDepth = GetLowestDepth(ctx.World.Reader, ctx.X, ctx.Y, ctx.Z - 1, lowestNeighborDepth, block.Material);
            lowestNeighborDepth = GetLowestDepth(ctx.World.Reader, ctx.X, ctx.Y, ctx.Z + 1, lowestNeighborDepth, block.Material);
            newLevel = lowestNeighborDepth + spreadRate;
            if (newLevel >= 8 || lowestNeighborDepth < 0)
            {
                newLevel = -1;
            }

            int stateAbove = GetLiquidState(ctx.World.Reader, ctx.X, ctx.Y + 1, ctx.Z, block.Material);
            if (stateAbove >= 0)
            {
                if (stateAbove >= 8)
                {
                    newLevel = stateAbove;
                }
                else
                {
                    newLevel = stateAbove + 8;
                }
            }

            if (_adjacentSources.Value >= 2 && block.Material == Material.Water)
            {
                Material matUnder = ctx.World.Reader.GetMaterial(ctx.X, ctx.Y - 1, ctx.Z);
                // block under is solid or water source
                if (matUnder.IsSolid || (matUnder == block.Material && ctx.World.Reader.GetBlockMeta(ctx.X, ctx.Y, ctx.Z) == 0))
                {
                    newLevel = 0;
                }
            }
            else if (block.Material == Material.Lava && currentState < 8 && newLevel < 8 && newLevel > currentState && ctx.World.Random.NextInt(4) != 0)
            {
                newLevel = currentState;
                convertToSource = false;
            }

            if (newLevel != currentState)
            {
                currentState = newLevel;
                if (newLevel < 0)
                {
                    ctx.World.Writer.SetBlock(ctx.X, ctx.Y, ctx.Z, 0);
                    return;
                }

                ctx.World.Writer.SetBlockMeta(ctx.X, ctx.Y, ctx.Z, newLevel);
            }
            else if (convertToSource)
            {
                ConvertToSource(block, ctx.World, ctx.X, ctx.Y, ctx.Z);
            }
            else
            {
                ctx.World.TickScheduler.ScheduleBlockUpdate(ctx.X, ctx.Y, ctx.Z, block.Id, block.TickRate);
            }
        }
        else
        {
            const int minDepth = -100;
            _adjacentSources.Value = 0;
            GetLowestDepth(ctx.World.Reader, ctx.X - 1, ctx.Y, ctx.Z, minDepth, block.Material);
            GetLowestDepth(ctx.World.Reader, ctx.X + 1, ctx.Y, ctx.Z, minDepth, block.Material);
            GetLowestDepth(ctx.World.Reader, ctx.X, ctx.Y, ctx.Z - 1, minDepth, block.Material);
            GetLowestDepth(ctx.World.Reader, ctx.X, ctx.Y, ctx.Z + 1, minDepth, block.Material);
        }

        if (currentState < 0)
        {
            return;
        }

        if (currentState >= 8)
        {
            SpreadTo(block, ctx.World, ctx.X, ctx.Y - 1, ctx.Z, currentState);
        }
        else
        {
            SpreadTo(block, ctx.World, ctx.X, ctx.Y - 1, ctx.Z, currentState + 8);
        }

        if (currentState == 0 || IsLiquidBreaking(ctx.World, ctx.X, ctx.Y - 1, ctx.Z))
        {
            newLevel = currentState + spreadRate;
            if (currentState >= 8)
            {
                newLevel = 1;
            }

            bool[] spreadArray = GetSpread(ctx.World, ctx.X, ctx.Y, ctx.Z, block.Material);

            if (newLevel < 8)
            {
                if (spreadArray[0]) SpreadTo(block, ctx.World, ctx.X - 1, ctx.Y, ctx.Z, newLevel);

                if (spreadArray[1]) SpreadTo(block, ctx.World, ctx.X + 1, ctx.Y, ctx.Z, newLevel);

                if (spreadArray[2]) SpreadTo(block, ctx.World, ctx.X, ctx.Y, ctx.Z - 1, newLevel);

                if (spreadArray[3]) SpreadTo(block, ctx.World, ctx.X, ctx.Y, ctx.Z + 1, newLevel);
            }
        }

        if (currentState == 0 && ctx.World.Reader.GetBlockId(ctx.X, ctx.Y, ctx.Z) == block.Id)
        {
            ConvertToSource(block, ctx.World, ctx.X, ctx.Y, ctx.Z);
        }
    }

    public int GetTexture(Block block, Side side, int defaultTexture) => FluidMath.GetTexture(side, still, flowing);

    public bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility)
        => FluidMath.IsSideVisible(block, reader, x, y, z, side, defaultVisibility);

    public float GetLuminance(Block block, ILightProvider lighting, int x, int y, int z, float defaultLuminance) => FluidMath.GetLuminance(lighting, x, y, z);

    public LightLevels GetLightLevels(Block block, ILightProvider lighting, int x, int y, int z, LightLevels defaultLevels) =>
        FluidMath.GetLightLevels(lighting, x, y, z, Block.BlocksLightLuminance[block.Id]);

    private static void ConvertToSource(Block block, IWorldContext world, int x, int y, int z)
    {
        int meta = world.Reader.GetBlockMeta(x, y, z);
        world.Writer.SetBlockWithoutNotifyingNeighbors(x, y, z, block.Id + 1, meta, false);
    }

    private void SpreadTo(Block block, IWorldContext world, int x, int y, int z, int depth)
    {
        if (!CanSpreadTo(world, x, y, z, block.Material)) return;

        int currentId = world.Reader.GetBlockId(x, y, z);
        if (currentId > 0)
        {
            if (block.Material == Material.Lava)
            {
                FluidMath.Fizz(world.Broadcaster, x, y, z);
            }
            else
            {
                Block.Blocks[currentId].DropStacks(new OnDropEvent(world, x, y, z, world.Reader.GetBlockMeta(x, y, z)));
            }
        }

        world.Writer.SetBlock(x, y, z, block.Id, depth);
    }

    private int GetDistanceToGap(IWorldContext world, int x, int y, int z, int distance, int fromDirection, Material material)
    {
        int minDistance = 1000;

        for (int direction = 0; direction < 4; ++direction)
        {
            if ((direction == 0 && fromDirection == 1) ||
                (direction == 1 && fromDirection == 0) ||
                (direction == 2 && fromDirection == 3) ||
                (direction == 3 && fromDirection == 2))
            {
                continue;
            }

            int neighborX = x;
            int neighborZ = z;
            switch (direction)
            {
                case 0:
                    neighborX = x - 1;
                    break;
                case 1:
                    ++neighborX;
                    break;
                case 2:
                    neighborZ = z - 1;
                    break;
                case 3:
                    ++neighborZ;
                    break;
            }

            if (IsLiquidBreaking(world, neighborX, y, neighborZ) || (world.Reader.GetMaterial(neighborX, y, neighborZ) == material && world.Reader.GetBlockMeta(neighborX, y, neighborZ) == 0))
            {
                continue;
            }

            if (!IsLiquidBreaking(world, neighborX, y - 1, neighborZ))
            {
                return distance;
            }

            if (distance >= 4)
            {
                continue;
            }

            int childDistance = GetDistanceToGap(world, neighborX, y, neighborZ, distance + 1, direction, material);
            if (childDistance < minDistance)
            {
                minDistance = childDistance;
            }
        }

        return minDistance;
    }

    private bool[] GetSpread(IWorldContext world, int x, int y, int z, Material material)
    {
        int direction;
        int neighborX;
        int[] distanceToGap = _distanceToGap.Value!;
        for (direction = 0; direction < 4; ++direction)
        {
            distanceToGap[direction] = 1000;
            neighborX = x;
            int neighborZ = z;
            switch (direction)
            {
                case 0:
                    neighborX = x - 1;
                    break;
                case 1:
                    ++neighborX;
                    break;
                case 2:
                    neighborZ = z - 1;
                    break;
                case 3:
                    ++neighborZ;
                    break;
            }

            if (IsLiquidBreaking(world, neighborX, y, neighborZ) || (world.Reader.GetMaterial(neighborX, y, neighborZ) == material && world.Reader.GetBlockMeta(neighborX, y, neighborZ) == 0))
            {
                continue;
            }

            if (!IsLiquidBreaking(world, neighborX, y - 1, neighborZ))
            {
                distanceToGap[direction] = 0;
            }
            else
            {
                distanceToGap[direction] = GetDistanceToGap(world, neighborX, y, neighborZ, 1, direction, material);
            }
        }

        direction = distanceToGap[0];

        for (neighborX = 1; neighborX < 4; ++neighborX)
        {
            if (distanceToGap[neighborX] < direction)
            {
                direction = distanceToGap[neighborX];
            }
        }

        bool[] spread = _spread.Value!;
        for (neighborX = 0; neighborX < 4; ++neighborX)
        {
            spread[neighborX] = distanceToGap[neighborX] == direction;
        }

        return spread;
    }

    private bool IsLiquidBreaking(IWorldContext world, int x, int y, int z)
    {
        if (x < -32000000 || z < -32000000 || x >= 32000000 || z > 32000000 || y < 0 || y >= ChuckFormat.WorldHeight) return false;

        if (!world.Reader.IsPosLoaded(x, y, z)) return true;

        int blockId = world.Reader.GetBlockId(x, y, z);
        foreach (Block obstacle in passable)
        {
            if (blockId == obstacle.Id) return true;
        }

        if (blockId == 0) return false;

        Material mat = Block.Blocks[blockId].Material;
        return mat.BlocksMovement;
    }

    private int GetLowestDepth(IBlockReader reader, int x, int y, int z, int depth, Material material)
    {
        int liquidState = GetLiquidState(reader, x, y, z, material);
        switch (liquidState)
        {
            case < 0:
                return depth;
            case 0:
                _adjacentSources.Value++;
                break;
            case >= 8:
                liquidState = 0;
                break;
        }

        return depth >= 0 && liquidState >= depth ? depth : liquidState;
    }

    private bool CanSpreadTo(IWorldContext world, int x, int y, int z, Material material)
    {
        if (x < -32000000 || z < -32000000 || x >= 32000000 || z > 32000000 || y < 0 || y >= ChuckFormat.WorldHeight) return false;

        if (!world.Reader.IsPosLoaded(x, y, z)) return false;

        int blockId = world.Reader.GetBlockId(x, y, z);
        if (blockId == 0) return true;

        Material mat = world.Reader.GetMaterial(x, y, z);
        return mat != material && mat != Material.Lava && !IsLiquidBreaking(world, x, y, z);
    }

    private static int GetLiquidState(IBlockReader reader, int x, int y, int z, Material material) => reader.GetMaterial(x, y, z) != material ? -1 : reader.GetBlockMeta(x, y, z);
}
