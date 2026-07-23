using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Nether portal: frame validation/creation, the thin rotating collision plane, edge-only face
///     visibility, and the ambient particle/sound tick. <see cref="Create" /> is public and static
///     since it's called externally (fire ignition next to obsidian) with no subclass left to hold it.
/// </summary>
internal sealed class PortalBehavior : IBlockPhysics, IBlockVisuals, IBlockInteractable, IBlockTicker
{
    private const float Thickness = 2.0F / 16.0F;
    private const float HalfExtent = 0.5F;

    public void OnEntityCollision(Block block, OnEntityCollisionEvent @event)
    {
        if (@event.Entity.Vehicle == null && @event.Entity.Passenger == null)
        {
            @event.Entity.TickPortalCooldown();
        }
    }

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape) => null;

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        if (reader.GetBlockId(x - 1, y, z) != block.id && reader.GetBlockId(x + 1, y, z) != block.id)
        {
            block.SetBoundingBox(0.5F - Thickness, 0.0F, 0.5F - HalfExtent, 0.5F + Thickness, 1.0F, 0.5F + HalfExtent);
        }
        else
        {
            block.SetBoundingBox(0.5F - HalfExtent, 0.0F, 0.5F - Thickness, 0.5F + HalfExtent, 1.0F, 0.5F + Thickness);
        }
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        sbyte offsetX = 0;
        sbyte offsetZ = 1;
        if (@event.World.Reader.GetBlockId(@event.X - 1, @event.Y, @event.Z) == block.id || @event.World.Reader.GetBlockId(@event.X + 1, @event.Y, @event.Z) == block.id)
        {
            offsetX = 1;
            offsetZ = 0;
        }

        int portalBottomY;
        for (portalBottomY = @event.Y; @event.World.Reader.GetBlockId(@event.X, portalBottomY - 1, @event.Z) == block.id; --portalBottomY)
        {
        }

        if (@event.World.Reader.GetBlockId(@event.X, portalBottomY - 1, @event.Z) != BlockRegistry.Get("obsidian").id)
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }
        else
        {
            int blocksAbove;
            for (blocksAbove = 1; blocksAbove < 4 && @event.World.Reader.GetBlockId(@event.X, portalBottomY + blocksAbove, @event.Z) == block.id; ++blocksAbove)
            {
            }

            if (blocksAbove == 3 && @event.World.Reader.GetBlockId(@event.X, portalBottomY + blocksAbove, @event.Z) == BlockRegistry.Get("obsidian").id)
            {
                bool hasXNeighbors = @event.World.Reader.GetBlockId(@event.X - 1, @event.Y, @event.Z) == block.id || @event.World.Reader.GetBlockId(@event.X + 1, @event.Y, @event.Z) == block.id;
                bool hasZNeighbors = @event.World.Reader.GetBlockId(@event.X, @event.Y, @event.Z - 1) == block.id || @event.World.Reader.GetBlockId(@event.X, @event.Y, @event.Z + 1) == block.id;
                if (hasXNeighbors && hasZNeighbors)
                {
                    @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
                }
                else if ((@event.World.Reader.GetBlockId(@event.X + offsetX, @event.Y, @event.Z + offsetZ) != BlockRegistry.Get("obsidian").id || @event.World.Reader.GetBlockId(@event.X - offsetX, @event.Y, @event.Z - offsetZ) != block.id) &&
                         (@event.World.Reader.GetBlockId(@event.X - offsetX, @event.Y, @event.Z - offsetZ) != BlockRegistry.Get("obsidian").id || @event.World.Reader.GetBlockId(@event.X + offsetX, @event.Y, @event.Z + offsetZ) != block.id))
                {
                    @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
                }
            }
            else
            {
                @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
            }
        }
    }

    public void RandomDisplayTick(Block block, OnTickEvent @event)
    {
        if (Random.Shared.Next(100) == 0)
        {
            @event.World.Broadcaster.PlaySoundAtPos(@event.X + 0.5D, @event.Y + 0.5D, @event.Z + 0.5D, "portal.portal", 1.0F, Random.Shared.NextSingle() * 0.4F + 0.8F);
        }

        for (int particleIndex = 0; particleIndex < 4; ++particleIndex)
        {
            double particleX = @event.X + Random.Shared.NextSingle();
            double particleY = @event.Y + Random.Shared.NextSingle();
            double particleZ = @event.Z + Random.Shared.NextSingle();
            int direction = Random.Shared.Next(2) * 2 - 1;
            double velocityX = (Random.Shared.NextSingle() - 0.5D) * 0.5D;
            double velocityY = (Random.Shared.NextSingle() - 0.5D) * 0.5D;
            double velocityZ = (Random.Shared.NextSingle() - 0.5D) * 0.5D;
            if (@event.World.Reader.GetBlockId(@event.X - 1, @event.Y, @event.Z) != block.id && @event.World.Reader.GetBlockId(@event.X + 1, @event.Y, @event.Z) != block.id)
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
        if (reader.GetBlockId(x, y, z) == block.id)
        {
            return false;
        }

        bool edgeWest = reader.GetBlockId(x - 1, y, z) == block.id && reader.GetBlockId(x - 2, y, z) != block.id;
        bool edgeEast = reader.GetBlockId(x + 1, y, z) == block.id && reader.GetBlockId(x + 2, y, z) != block.id;
        bool edgeNorth = reader.GetBlockId(x, y, z - 1) == block.id && reader.GetBlockId(x, y, z - 2) != block.id;
        bool edgeSouth = reader.GetBlockId(x, y, z + 1) == block.id && reader.GetBlockId(x, y, z + 2) != block.id;
        bool extendsInX = edgeWest || edgeEast;
        bool extendsInZ = edgeNorth || edgeSouth;
        return (extendsInX && side == Side.West) ||
               (extendsInX && side == Side.East) ||
               (extendsInZ && side == Side.North) ||
               (extendsInZ && side == Side.South);
    }

    public static bool Create(IBlockReader reader, IBlockWriter writer, int x, int y, int z)
    {
        sbyte extendsInZ = 0;
        sbyte extendsInX = 0;
        if (reader.GetBlockId(x - 1, y, z) == BlockRegistry.Get("obsidian").id || reader.GetBlockId(x + 1, y, z) == BlockRegistry.Get("obsidian").id)
        {
            extendsInZ = 1;
        }

        if (reader.GetBlockId(x, y, z - 1) == BlockRegistry.Get("obsidian").id || reader.GetBlockId(x, y, z + 1) == BlockRegistry.Get("obsidian").id)
        {
            extendsInX = 1;
        }

        if (extendsInZ == extendsInX)
        {
            return false;
        }

        if (reader.GetBlockId(x - extendsInZ, y, z - extendsInX) == 0)
        {
            x -= extendsInZ;
            z -= extendsInX;
        }

        int horizontalOffset;
        int verticalOffset;
        for (horizontalOffset = -1; horizontalOffset <= 2; ++horizontalOffset)
        {
            for (verticalOffset = -1; verticalOffset <= 3; ++verticalOffset)
            {
                bool isFrame = horizontalOffset == -1 || horizontalOffset == 2 || verticalOffset == -1 || verticalOffset == 3;
                if (horizontalOffset is -1 or 2 && verticalOffset is -1 or 3)
                {
                    continue;
                }

                int blockId = reader.GetBlockId(x + extendsInZ * horizontalOffset, y + verticalOffset, z + extendsInX * horizontalOffset);
                if (isFrame)
                {
                    if (blockId != BlockRegistry.Get("obsidian").id)
                    {
                        return false;
                    }
                }
                else if (blockId != 0 && blockId != BlockRegistry.Get("fire").id)
                {
                    return false;
                }
            }
        }

        for (horizontalOffset = 0; horizontalOffset < 2; ++horizontalOffset)
        {
            for (verticalOffset = 0; verticalOffset < 3; ++verticalOffset)
            {
                writer.SetBlockInternal(x + extendsInZ * horizontalOffset, y + verticalOffset, z + extendsInX * horizontalOffset, BlockRegistry.Get("nether_portal").id);
            }
        }

        return true;
    }
}
