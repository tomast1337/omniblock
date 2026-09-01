using OmniBlock.Blocks.Entities;
using OmniBlock.Inventories;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

internal sealed class ChestBehavior(int top, int side, int front, int doubleFrontLeft, int doubleFrontRight, int doubleBackLeft, int doubleBackRight)
    : BlockRuntimeBehavior, IBlockInteractable, IBlockLifecycle, IBlockPhysics, IBlockVisuals
{
    public bool OnUse(Block block, OnUseEvent @event)
    {
        IInventory? chestInventory = @event.World.Entities.GetBlockEntity<BlockEntityChest>(@event.X, @event.Y, @event.Z);
        if (@event.World.Reader.ShouldSuffocate(@event.X, @event.Y + 1, @event.Z))
            return true;

        var chestId = block.Id;

        if (@event.World.Reader.GetBlockId(@event.X - 1, @event.Y, @event.Z) == chestId && @event.World.Reader.ShouldSuffocate(@event.X - 1, @event.Y + 1, @event.Z))
            return true;

        if (@event.World.Reader.GetBlockId(@event.X + 1, @event.Y, @event.Z) == chestId && @event.World.Reader.ShouldSuffocate(@event.X + 1, @event.Y + 1, @event.Z))
            return true;

        if (@event.World.Reader.GetBlockId(@event.X, @event.Y, @event.Z - 1) == chestId && @event.World.Reader.ShouldSuffocate(@event.X, @event.Y + 1, @event.Z - 1))
            return true;

        if (@event.World.Reader.GetBlockId(@event.X, @event.Y, @event.Z + 1) == chestId && @event.World.Reader.ShouldSuffocate(@event.X, @event.Y + 1, @event.Z + 1))
            return true;

        // The preceding GetBlockId == chestId check guarantees a BlockEntityChest exists at that neighbor position, chest blocks always carry a tile entity via TileEntityLifecycleBehavior.
        if (@event.World.Reader.GetBlockId(@event.X - 1, @event.Y, @event.Z) == chestId)
            chestInventory = new InventoryLargeChest("Large chest", @event.World.Entities.GetBlockEntity<BlockEntityChest>(@event.X - 1, @event.Y, @event.Z)!, chestInventory!);

        if (@event.World.Reader.GetBlockId(@event.X + 1, @event.Y, @event.Z) == chestId)
            chestInventory = new InventoryLargeChest("Large chest", chestInventory!, @event.World.Entities.GetBlockEntity<BlockEntityChest>(@event.X + 1, @event.Y, @event.Z)!);

        if (@event.World.Reader.GetBlockId(@event.X, @event.Y, @event.Z - 1) == chestId)
            chestInventory = new InventoryLargeChest("Large chest", @event.World.Entities.GetBlockEntity<BlockEntityChest>(@event.X, @event.Y, @event.Z - 1)!, chestInventory!);

        if (@event.World.Reader.GetBlockId(@event.X, @event.Y, @event.Z + 1) == chestId)
            chestInventory = new InventoryLargeChest("Large chest", chestInventory!, @event.World.Entities.GetBlockEntity<BlockEntityChest>(@event.X, @event.Y, @event.Z + 1)!);

        if (@event.World.IsRemote) return true;

        @event.Player.openChestScreen(chestInventory!);
        return true;
    }

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        InventoryUtility.OnPlaced(block, @event);
    }

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        InventoryUtility.OnBreak(block, @event);
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext context)
    {
        var chestId = block.Id;
        var adjacentChestCount = 0;
        if (context.World.Reader.GetBlockId(context.X - 1, context.Y, context.Z) == chestId) ++adjacentChestCount;

        if (context.World.Reader.GetBlockId(context.X + 1, context.Y, context.Z) == chestId) ++adjacentChestCount;

        if (context.World.Reader.GetBlockId(context.X, context.Y, context.Z - 1) == chestId) ++adjacentChestCount;

        if (context.World.Reader.GetBlockId(context.X, context.Y, context.Z + 1) == chestId) ++adjacentChestCount;

        return adjacentChestCount <= 1 && !HasNeighbor(chestId, context);
    }

    public int GetTexture(Block block, Side renderSide, int defaultTexture)
    {
        return renderSide switch
        {
            Side.Up or Side.Down => top,
            Side.South => front,
            _ => side
        };
    }

    public int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side renderSide, int defaultTexture)
    {
        if (renderSide is Side.Up or Side.Down) return top;

        var chestId = block.Id;
        var blockNorth = reader.GetBlockId(x, y, z - 1);
        var blockSouth = reader.GetBlockId(x, y, z + 1);
        var blockWest = reader.GetBlockId(x - 1, y, z);
        var blockEast = reader.GetBlockId(x + 1, y, z);

        var isDoubleEw = blockWest == chestId || blockEast == chestId;
        var isDoubleNs = blockNorth == chestId || blockSouth == chestId;

        if (!isDoubleNs && !isDoubleEw)
        {
            var facing = Side.South;
            if (Blocks.IsOpaque(blockNorth) && !Blocks.IsOpaque(blockSouth)) facing = Side.South;
            if (Blocks.IsOpaque(blockSouth) && !Blocks.IsOpaque(blockNorth)) facing = Side.North;
            if (Blocks.IsOpaque(blockWest) && !Blocks.IsOpaque(blockEast)) facing = Side.East;
            if (Blocks.IsOpaque(blockEast) && !Blocks.IsOpaque(blockWest)) facing = Side.West;
            return renderSide == facing ? front : side;
        }

        if (isDoubleEw)
        {
            if (renderSide is Side.West or Side.East) return side;

            var isWestPartner = blockWest == chestId;
            var corner1 = reader.GetBlockId(isWestPartner ? x - 1 : x + 1, y, z - 1);
            var corner2 = reader.GetBlockId(isWestPartner ? x - 1 : x + 1, y, z + 1);

            var facing = Side.South;
            if ((Blocks.IsOpaque(blockNorth) || Blocks.IsOpaque(corner1)) && !Blocks.IsOpaque(blockSouth) && !Blocks.IsOpaque(corner2)) facing = Side.South;
            if ((Blocks.IsOpaque(blockSouth) || Blocks.IsOpaque(corner2)) && !Blocks.IsOpaque(blockNorth) && !Blocks.IsOpaque(corner1)) facing = Side.North;

            var isRightHalf = facing == Side.South ? isWestPartner : !isWestPartner;

            return GetDoubleChestTexture(renderSide, facing, isRightHalf);
        }

        if (isDoubleNs)
        {
            if (renderSide is Side.North or Side.South) return side;

            var isNorthPartner = blockNorth == chestId;
            var corner1 = reader.GetBlockId(x - 1, y, isNorthPartner ? z - 1 : z + 1);
            var corner2 = reader.GetBlockId(x + 1, y, isNorthPartner ? z - 1 : z + 1);

            var facing = Side.East;
            if ((Blocks.IsOpaque(blockWest) || Blocks.IsOpaque(corner1)) && !Blocks.IsOpaque(blockEast) && !Blocks.IsOpaque(corner2)) facing = Side.East;
            if ((Blocks.IsOpaque(blockEast) || Blocks.IsOpaque(corner2)) && !Blocks.IsOpaque(blockWest) && !Blocks.IsOpaque(corner1)) facing = Side.West;

            var isRightHalf = facing == Side.East ? !isNorthPartner : isNorthPartner;

            return GetDoubleChestTexture(renderSide, facing, isRightHalf);
        }

        return side;
    }

    private static bool HasNeighbor(int chestId, CanPlaceAtContext ctx)
    {
        return ctx.World.Reader.GetBlockId(ctx.X, ctx.Y, ctx.Z) == chestId &&
               (
                   ctx.World.Reader.GetBlockId(ctx.X - 1, ctx.Y, ctx.Z) == chestId ||
                   ctx.World.Reader.GetBlockId(ctx.X + 1, ctx.Y, ctx.Z) == chestId ||
                   ctx.World.Reader.GetBlockId(ctx.X, ctx.Y, ctx.Z - 1) == chestId ||
                   ctx.World.Reader.GetBlockId(ctx.X, ctx.Y, ctx.Z + 1) == chestId
               );
    }

    private int GetDoubleChestTexture(Side renderSide, Side frontFacing, bool isRightHalf)
    {
        var isFront = renderSide == frontFacing;
        if (isFront) return isRightHalf ? doubleFrontRight : doubleFrontLeft;
        return isRightHalf ? doubleBackLeft : doubleBackRight;
    }
}