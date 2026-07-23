using BetaSharp.Blocks.Entities;
using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

internal sealed class DispenserBehavior : IBlockInteractable, IBlockLifecycle, IBlockPhysics, IBlockTicker, IBlockVisuals
{
    private static readonly int s_arrowId = Item.ByName("arrow").Id;
    private static readonly int s_eggId = Item.ByName("egg").Id;
    private static readonly int s_snowballId = Item.ByName("snowball").Id;

    public bool OnUse(Block block, OnUseEvent @event)
    {
        if (@event.World.IsRemote) return true;
        BlockEntityDispenser? dispenser = @event.World.Entities.GetBlockEntity<BlockEntityDispenser>(@event.X, @event.Y, @event.Z);
        if (dispenser != null) @event.Player.openDispenserScreen(dispenser);
        return true;
    }

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (@event.Placer == null)
        {
            UpdateDirection(@event);
        }
        else
        {
            int direction = MathHelper.Floor(@event.Placer.Yaw * 4.0F / 360.0F + 0.5D) & 3;
            int meta = direction switch
            {
                0 => 2,
                1 => 5,
                2 => 3,
                3 => 4,
                _ => 2
            };
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta);
            if (!@event.World.IsRemote)
            {
                @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta);
            }
        }

        InventoryUtility.OnPlaced(block, @event);
    }

    public void OnBreak(Block block, OnBreakEvent @event) => InventoryUtility.OnBreak(block, @event);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        bool emits = @event.BlockId > 0 && Block.Blocks[@event.BlockId].canEmitRedstonePower();
        bool isPowered = @event.World.Redstone.IsPowered(@event.X, @event.Y, @event.Z) ||
                         @event.World.Redstone.IsPowered(@event.X, @event.Y + 1, @event.Z);
        if (@event.BlockId <= 0 || !Block.Blocks[@event.BlockId].canEmitRedstonePower()) return;
        if (isPowered) @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.Id, block.TickRate);
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (@event.World.Redstone.IsPowered(@event.X, @event.Y, @event.Z) || @event.World.Redstone.IsPowered(@event.X, @event.Y + 1, @event.Z))
        {
            Dispense(@event);
        }
    }

    public int GetTexture(Block block, Side side, int defaultTexture) =>
        side switch
        {
            Side.Up or Side.Down => block.TextureId + 17,
            Side.South => block.TextureId + 1,
            _ => defaultTexture
        };

    public int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side side, int defaultTexture)
    {
        if (side is Side.Up or Side.Down) return block.TextureId + 17;
        Side facing = reader.GetBlockMeta(x, y, z).ToSide();
        return side != facing ? block.TextureId : block.TextureId + 1;
    }

    private static void UpdateDirection(OnPlacedEvent @event)
    {
        if (@event.World.IsRemote) return;

        IBlockReader reader = @event.World.Reader;
        int x = @event.X, y = @event.Y, z = @event.Z;

        bool isNorthOpaque = Block.BlocksOpaque[reader.GetBlockId(x, y, z - 1)];
        bool isSouthOpaque = Block.BlocksOpaque[reader.GetBlockId(x, y, z + 1)];
        bool isWestOpaque = Block.BlocksOpaque[reader.GetBlockId(x - 1, y, z)];
        bool isEastOpaque = Block.BlocksOpaque[reader.GetBlockId(x + 1, y, z)];

        byte direction = 3;
        if (isNorthOpaque && !isSouthOpaque) direction = 3;
        else if (isSouthOpaque && !isNorthOpaque) direction = 2;
        if (isWestOpaque && !isEastOpaque) direction = 5;
        else if (isEastOpaque && !isWestOpaque) direction = 4;

        @event.World.Writer.SetBlockMeta(x, y, z, direction);
    }

    private static void Dispense(OnTickEvent @event)
    {
        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        int dirX = 0;
        int dirZ = 0;

        switch (meta)
        {
            case 3:
                dirZ = 1;
                break;
            case 2:
                dirZ = -1;
                break;
            case 5:
                dirX = 1;
                break;
            default:
                dirX = -1;
                break;
        }

        BlockEntityDispenser? dispenser = @event.World.Entities.GetBlockEntity<BlockEntityDispenser>(@event.X, @event.Y, @event.Z);
        if (dispenser == null) return;

        ItemStack? itemStack = dispenser.GetItemToDispose();
        double spawnX = @event.X + dirX * 0.6D + 0.5D;
        double spawnY = @event.Y + 0.5D;
        double spawnZ = @event.Z + dirZ * 0.6D + 0.5D;

        if (itemStack == null)
        {
            @event.World.Broadcaster.WorldEvent(1001, @event.X, @event.Y, @event.Z, 0);
            return;
        }

        if (itemStack.ItemId == s_arrowId)
        {
            EntityArrow arrow = new(@event.World, spawnX, spawnY, spawnZ);
            arrow.SetArrowHeading(dirX, 0.1F, dirZ, 1.1F, 6.0F);
            arrow.DoesArrowBelongToPlayer = true;
            @event.World.Entities.SpawnEntity(arrow);
            @event.World.Broadcaster.WorldEvent(1002, @event.X, @event.Y, @event.Z, 0);
        }
        else if (itemStack.ItemId == s_eggId)
        {
            EntityEgg egg = new(@event.World, spawnX, spawnY, spawnZ);
            egg.setHeading(dirX, 0.1F, dirZ, 1.1F, 6.0F);
            @event.World.Entities.SpawnEntity(egg);
            @event.World.Broadcaster.WorldEvent(1002, @event.X, @event.Y, @event.Z, 0);
        }
        else if (itemStack.ItemId == s_snowballId)
        {
            EntitySnowball snowball = new(@event.World, spawnX, spawnY, spawnZ);
            snowball.SetHeading(dirX, 0.1F, dirZ, 1.1F, 6.0F);
            @event.World.Entities.SpawnEntity(snowball);
            @event.World.Broadcaster.WorldEvent(1002, @event.X, @event.Y, @event.Z, 0);
        }
        else
        {
            EntityItem item = new(@event.World, spawnX, spawnY - 0.3D, spawnZ, itemStack);
            double randomVelocity = Random.Shared.NextDouble() * 0.1D + 0.2D;
            item.VelocityX = dirX * randomVelocity;
            item.VelocityY = 0.2F;
            item.VelocityZ = dirZ * randomVelocity;

            item.VelocityX += @event.World.Random.NextGaussian() * 0.0075D * 6.0D;
            item.VelocityY += @event.World.Random.NextGaussian() * 0.0075D * 6.0D;
            item.VelocityZ += @event.World.Random.NextGaussian() * 0.0075D * 6.0D;

            @event.World.Entities.SpawnEntity(item);
            @event.World.Broadcaster.WorldEvent(1000, @event.X, @event.Y, @event.Z, 0);
        }

        @event.World.Broadcaster.WorldEvent(2000, @event.X, @event.Y, @event.Z, dirX + 1 + (dirZ + 1) * 3);
    }
}
