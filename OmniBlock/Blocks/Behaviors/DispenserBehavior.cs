using OmniBlock.Blocks.Entities;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Which items get projectile-spawn behavior (vs. a plain item toss) are required,
///     (see <c>BehaviorRegistry</c>'s <c>"dispenser"</c> entry).
/// </summary>
internal sealed class DispenserBehavior(Item arrow, Item egg, Item snowball, int front, int top, int side) : BlockRuntimeBehavior, IBlockInteractable, IBlockLifecycle, IBlockPhysics, IBlockTicker, IBlockVisuals
{
    public bool OnUse(Block block, OnUseEvent @event)
    {
        if (@event.World.IsRemote) return true;
        var dispenser = @event.World.Entities.GetBlockEntity<BlockEntityDispenser>(@event.X, @event.Y, @event.Z);
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
            var direction = MathHelper.Floor(@event.Placer.Yaw * 4.0F / 360.0F + 0.5D) & 3;
            var meta = direction switch
            {
                0 => 2,
                1 => 5,
                2 => 3,
                3 => 4,
                _ => 2
            };
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta);
            if (!@event.World.IsRemote) @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta);
        }

        InventoryUtility.OnPlaced(block, @event);
    }

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        InventoryUtility.OnBreak(block, @event);
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        var emits = @event.BlockId > 0 && Blocks.GetByProtocolId(@event.BlockId).CanEmitRedstonePower();
        var isPowered = @event.World.Redstone.IsPowered(@event.X, @event.Y, @event.Z) ||
                        @event.World.Redstone.IsPowered(@event.X, @event.Y + 1, @event.Z);
        if (@event.BlockId <= 0 || !Blocks.GetByProtocolId(@event.BlockId).CanEmitRedstonePower()) return;
        if (isPowered) @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.Id, block.TickRate);
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (@event.World.Redstone.IsPowered(@event.X, @event.Y, @event.Z) || @event.World.Redstone.IsPowered(@event.X, @event.Y + 1, @event.Z)) Dispense(@event);
    }

    public int GetTexture(Block block, Side side, int defaultTexture)
    {
        return side switch
        {
            Side.Up or Side.Down => top,
            Side.South => front,
            _ => defaultTexture
        };
    }

    public int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side renderSide, int defaultTexture)
    {
        if (renderSide is Side.Up or Side.Down) return top;
        var facing = reader.GetBlockMeta(x, y, z).ToSide();
        return renderSide != facing ? side : front;
    }

    private void UpdateDirection(OnPlacedEvent @event)
    {
        if (@event.World.IsRemote) return;

        var reader = @event.World.Reader;
        int x = @event.X, y = @event.Y, z = @event.Z;

        var isNorthOpaque = Blocks.IsOpaque(reader.GetBlockId(x, y, z - 1));
        var isSouthOpaque = Blocks.IsOpaque(reader.GetBlockId(x, y, z + 1));
        var isWestOpaque = Blocks.IsOpaque(reader.GetBlockId(x - 1, y, z));
        var isEastOpaque = Blocks.IsOpaque(reader.GetBlockId(x + 1, y, z));

        byte direction = 3;
        if (isNorthOpaque && !isSouthOpaque) direction = 3;
        else if (isSouthOpaque && !isNorthOpaque) direction = 2;
        if (isWestOpaque && !isEastOpaque) direction = 5;
        else if (isEastOpaque && !isWestOpaque) direction = 4;

        @event.World.Writer.SetBlockMeta(x, y, z, direction);
    }

    private void Dispense(OnTickEvent @event)
    {
        var meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        var dirX = 0;
        var dirZ = 0;

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

        var dispenser = @event.World.Entities.GetBlockEntity<BlockEntityDispenser>(@event.X, @event.Y, @event.Z);
        if (dispenser == null) return;

        var itemStack = dispenser.GetItemToDispose();
        var spawnX = @event.X + dirX * 0.6D + 0.5D;
        var spawnY = @event.Y + 0.5D;
        var spawnZ = @event.Z + dirZ * 0.6D + 0.5D;

        if (itemStack == null)
        {
            @event.World.Broadcaster.WorldEvent(1001, @event.X, @event.Y, @event.Z, 0);
            return;
        }

        if (itemStack.ItemId == arrow.Id)
        {
            Entity shot = @event.World.Content.EntityTypes.Create("omniblock:arrow", @event.World);
            shot.SetPositionAndAngles(spawnX, spawnY, spawnZ, 0.0F, 0.0F);
            var flight = shot.Behaviors.Find<ArrowBehavior>()!;
            flight.SetHeading(shot, dirX, 0.1D, dirZ, 1.1F, 6.0F);
            flight.SetBelongsToPlayer(shot, true);
            @event.World.Entities.SpawnEntity(shot);
            @event.World.Broadcaster.WorldEvent(1002, @event.X, @event.Y, @event.Z, 0);
        }
        else if (itemStack.ItemId == egg.Id)
        {
            DispenseProjectile(@event, "egg", spawnX, spawnY, spawnZ, dirX, dirZ);
        }
        else if (itemStack.ItemId == snowball.Id)
        {
            DispenseProjectile(@event, "snowball", spawnX, spawnY, spawnZ, dirX, dirZ);
        }
        else
        {
            var item = DroppedItemBehavior.Create(@event.World, spawnX, spawnY - 0.3D, spawnZ, itemStack);
            var randomVelocity = Random.Shared.NextDouble() * 0.1D + 0.2D;
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

    private static void DispenseProjectile(OnTickEvent @event, string typeName, double spawnX, double spawnY, double spawnZ, int dirX, int dirZ)
    {
        Entity projectile = @event.World.Content.EntityTypes.Create(typeName, @event.World);
        projectile.SetPositionAndAngles(spawnX, spawnY, spawnZ, 0.0F, 0.0F);
        projectile.Behaviors.Find<ThrownProjectileBehavior>()!.SetHeading(projectile, dirX, 0.1D, dirZ, 1.1F, 6.0F);
        @event.World.Entities.SpawnEntity(projectile);
        @event.World.Broadcaster.WorldEvent(1002, @event.X, @event.Y, @event.Z, 0);
    }
}
