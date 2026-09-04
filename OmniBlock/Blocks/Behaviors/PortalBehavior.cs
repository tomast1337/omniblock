using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Nether portal: frame validation/creation, the thin rotating collision plane, edge-only face
///     visibility, and the ambient particle/sound tick. <see cref="Create" /> is public and static
///     since it's called externally (fire ignition next to the frame material) with no subclass
///     left to hold it, the caller (<see cref="FireBehavior" />) passes its own configured
///     frame/portal blocks through rather than <see cref="Create" /> resolving them itself.
///     <para>
///         Frame material (<paramref name="portalBase" />) is a required, (see <c>BehaviorRegistry</c>'s <c>"portal"</c>
///         entry).
///     </para>
/// </summary>
internal sealed class PortalBehavior(Block portalBase) : IBlockPhysics, IBlockVisuals, IBlockInteractable, IBlockTicker
{
    private const float Thickness = 2.0F / 16.0F;
    private const float HalfExtent = 0.5F;

    public void OnEntityCollision(Block block, OnEntityCollisionEvent @event)
    {
        if (@event.Entity.Vehicle == null && @event.Entity.Passenger == null) @event.Entity.TickPortalCooldown();
    }

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape) => null;

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        if (reader.GetBlockId(x - 1, y, z) != block.Id && reader.GetBlockId(x + 1, y, z) != block.Id)
            block.SetRuntimeBoundingBox(0.5F - Thickness, 0.0F, 0.5F - HalfExtent, 0.5F + Thickness, 1.0F, 0.5F + HalfExtent);
        else
            block.SetRuntimeBoundingBox(0.5F - HalfExtent, 0.0F, 0.5F - Thickness, 0.5F + HalfExtent, 1.0F, 0.5F + Thickness);
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        sbyte offsetX = 0;
        sbyte offsetZ = 1;
        if (@event.World.Reader.GetBlockId(@event.X - 1, @event.Y, @event.Z) == block.Id || @event.World.Reader.GetBlockId(@event.X + 1, @event.Y, @event.Z) == block.Id)
        {
            offsetX = 1;
            offsetZ = 0;
        }

        int portalBottomY;
        for (portalBottomY = @event.Y; @event.World.Reader.GetBlockId(@event.X, portalBottomY - 1, @event.Z) == block.Id; --portalBottomY)
        {
        }

        if (@event.World.Reader.GetBlockId(@event.X, portalBottomY - 1, @event.Z) != portalBase.Id)
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }
        else
        {
            int blocksAbove;
            for (blocksAbove = 1; blocksAbove < 4 && @event.World.Reader.GetBlockId(@event.X, portalBottomY + blocksAbove, @event.Z) == block.Id; ++blocksAbove)
            {
            }

            if (blocksAbove == 3 && @event.World.Reader.GetBlockId(@event.X, portalBottomY + blocksAbove, @event.Z) == portalBase.Id)
            {
                var hasXNeighbors = @event.World.Reader.GetBlockId(@event.X - 1, @event.Y, @event.Z) == block.Id || @event.World.Reader.GetBlockId(@event.X + 1, @event.Y, @event.Z) == block.Id;
                var hasZNeighbors = @event.World.Reader.GetBlockId(@event.X, @event.Y, @event.Z - 1) == block.Id || @event.World.Reader.GetBlockId(@event.X, @event.Y, @event.Z + 1) == block.Id;
                if (hasXNeighbors && hasZNeighbors)
                    @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
                else if ((@event.World.Reader.GetBlockId(@event.X + offsetX, @event.Y, @event.Z + offsetZ) != portalBase.Id || @event.World.Reader.GetBlockId(@event.X - offsetX, @event.Y, @event.Z - offsetZ) != block.Id) &&
                         (@event.World.Reader.GetBlockId(@event.X - offsetX, @event.Y, @event.Z - offsetZ) != portalBase.Id || @event.World.Reader.GetBlockId(@event.X + offsetX, @event.Y, @event.Z + offsetZ) != block.Id))
                    @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
            }
            else
            {
                @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
            }
        }
    }

    public void RandomDisplayTick(Block block, OnTickEvent @event)
    {
        if (Random.Shared.Next(100) == 0) @event.World.Broadcaster.PlaySoundAtPos(@event.X + 0.5D, @event.Y + 0.5D, @event.Z + 0.5D, "portal.portal", 1.0F, Random.Shared.NextSingle() * 0.4F + 0.8F);

        for (var particleIndex = 0; particleIndex < 4; ++particleIndex)
        {
            double particleX = @event.X + Random.Shared.NextSingle();
            double particleY = @event.Y + Random.Shared.NextSingle();
            double particleZ = @event.Z + Random.Shared.NextSingle();
            var direction = Random.Shared.Next(2) * 2 - 1;
            var velocityX = (Random.Shared.NextSingle() - 0.5D) * 0.5D;
            var velocityY = (Random.Shared.NextSingle() - 0.5D) * 0.5D;
            var velocityZ = (Random.Shared.NextSingle() - 0.5D) * 0.5D;
            if (@event.World.Reader.GetBlockId(@event.X - 1, @event.Y, @event.Z) != block.Id && @event.World.Reader.GetBlockId(@event.X + 1, @event.Y, @event.Z) != block.Id)
            {
                particleX = @event.X + 0.5D + 0.25D * direction;
                velocityX = Random.Shared.NextSingle() * 2.0F * direction;
            }
            else
            {
                particleZ = @event.Z + 0.5D + 0.25D * direction;
                velocityZ = Random.Shared.NextSingle() * 2.0F * direction;
            }

            @event.World.Broadcaster.AddParticle("portal", particleX, particleY, particleZ, velocityX, velocityY, velocityZ);
        }
    }

    public bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility)
    {
        if (reader.GetBlockId(x, y, z) == block.Id) return false;

        var edgeWest = reader.GetBlockId(x - 1, y, z) == block.Id && reader.GetBlockId(x - 2, y, z) != block.Id;
        var edgeEast = reader.GetBlockId(x + 1, y, z) == block.Id && reader.GetBlockId(x + 2, y, z) != block.Id;
        var edgeNorth = reader.GetBlockId(x, y, z - 1) == block.Id && reader.GetBlockId(x, y, z - 2) != block.Id;
        var edgeSouth = reader.GetBlockId(x, y, z + 1) == block.Id && reader.GetBlockId(x, y, z + 2) != block.Id;
        var extendsInX = edgeWest || edgeEast;
        var extendsInZ = edgeNorth || edgeSouth;
        return (extendsInX && side == Side.West) ||
               (extendsInX && side == Side.East) ||
               (extendsInZ && side == Side.North) ||
               (extendsInZ && side == Side.South);
    }

    public static bool Create(IBlockReader reader, IBlockWriter writer, int x, int y, int z, Block portalBase, Block ignitionSource, Block portalFill)
    {
        sbyte extendsInZ = 0;
        sbyte extendsInX = 0;
        if (reader.GetBlockId(x - 1, y, z) == portalBase.Id || reader.GetBlockId(x + 1, y, z) == portalBase.Id) extendsInZ = 1;

        if (reader.GetBlockId(x, y, z - 1) == portalBase.Id || reader.GetBlockId(x, y, z + 1) == portalBase.Id) extendsInX = 1;

        if (extendsInZ == extendsInX) return false;

        if (reader.GetBlockId(x - extendsInZ, y, z - extendsInX) == 0)
        {
            x -= extendsInZ;
            z -= extendsInX;
        }

        int horizontalOffset;
        int verticalOffset;
        for (horizontalOffset = -1; horizontalOffset <= 2; ++horizontalOffset)
        for (verticalOffset = -1; verticalOffset <= 3; ++verticalOffset)
        {
            var isFrame = horizontalOffset == -1 || horizontalOffset == 2 || verticalOffset == -1 || verticalOffset == 3;
            if (horizontalOffset is -1 or 2 && verticalOffset is -1 or 3) continue;

            var blockId = reader.GetBlockId(x + extendsInZ * horizontalOffset, y + verticalOffset, z + extendsInX * horizontalOffset);
            if (isFrame)
            {
                if (blockId != portalBase.Id) return false;
            }
            else if (blockId != 0 && blockId != ignitionSource.Id)
            {
                return false;
            }
        }

        for (horizontalOffset = 0; horizontalOffset < 2; ++horizontalOffset)
        for (verticalOffset = 0; verticalOffset < 3; ++verticalOffset)
            writer.SetBlockInternal(x + extendsInZ * horizontalOffset, y + verticalOffset, z + extendsInX * horizontalOffset, portalFill.Id);

        return true;
    }
}
