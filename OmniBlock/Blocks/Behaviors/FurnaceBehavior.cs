using OmniBlock.Blocks.Entities;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

internal sealed class FurnaceBehavior(bool lit, int top, int frontOff, int frontOn)
    : BlockRuntimeBehavior, IBlockInteractable, IBlockLifecycle, IBlockPhysics, IBlockTicker, IBlockVisuals
{
    private const float FlameParticleOffset = 0.52F;

    public bool OnUse(Block block, OnUseEvent @event)
    {
        if (@event.World.IsRemote) return true;

        var furnace = @event.World.Entities.GetBlockEntity<BlockEntityFurnace>(@event.X, @event.Y, @event.Z);
        if (furnace == null) return false;

        @event.Player.openFurnaceScreen(furnace);
        return true;
    }

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (@event.Placer != null)
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
        else
        {
            UpdateDirection(@event);
        }

        InventoryUtility.OnPlaced(block, @event);
    }

    public void OnBreak(Block block, OnBreakEvent @event) => InventoryUtility.OnBreak(block, @event);

    public void RandomDisplayTick(Block block, OnTickEvent @event)
    {
        if (!lit) return;

        var rotation = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z).ToSide();
        var particleX = @event.X + 0.5F;
        var particleY = @event.Y + 0.0F + Random.Shared.NextSingle() * 6.0F / 16.0F;
        var particleZ = @event.Z + 0.5F;
        var randomOffset = Random.Shared.NextSingle() * 0.6F - 0.3F;

        switch (rotation)
        {
            case Side.West:
                @event.World.Broadcaster.AddParticle("smoke", particleX - FlameParticleOffset, particleY, particleZ + randomOffset, 0.0D, 0.0D, 0.0D);
                @event.World.Broadcaster.AddParticle("flame", particleX - FlameParticleOffset, particleY, particleZ + randomOffset, 0.0D, 0.0D, 0.0D);
                break;
            case Side.East:
                @event.World.Broadcaster.AddParticle("smoke", particleX + FlameParticleOffset, particleY, particleZ + randomOffset, 0.0D, 0.0D, 0.0D);
                @event.World.Broadcaster.AddParticle("flame", particleX + FlameParticleOffset, particleY, particleZ + randomOffset, 0.0D, 0.0D, 0.0D);
                break;
            case Side.North:
                @event.World.Broadcaster.AddParticle("smoke", particleX + randomOffset, particleY, particleZ - FlameParticleOffset, 0.0D, 0.0D, 0.0D);
                @event.World.Broadcaster.AddParticle("flame", particleX + randomOffset, particleY, particleZ - FlameParticleOffset, 0.0D, 0.0D, 0.0D);
                break;
            case Side.South:
                @event.World.Broadcaster.AddParticle("smoke", particleX + randomOffset, particleY, particleZ + FlameParticleOffset, 0.0D, 0.0D, 0.0D);
                @event.World.Broadcaster.AddParticle("flame", particleX + randomOffset, particleY, particleZ + FlameParticleOffset, 0.0D, 0.0D, 0.0D);
                break;
        }
    }

    public int GetTexture(Block block, Side side, int defaultTexture)
    {
        return side switch
        {
            Side.Up or Side.Down => top,
            Side.South => frontOff,
            _ => defaultTexture
        };
    }

    public int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side side, int defaultTexture)
    {
        if (side is Side.Up or Side.Down) return top;

        var facing = reader.GetBlockMeta(x, y, z).ToSide();
        if (side != facing) return block.TextureId;

        return lit ? frontOn : frontOff;
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
        if (isNorthOpaque && !isSouthOpaque)
            direction = 3;
        else if (isSouthOpaque && !isNorthOpaque) direction = 2;

        if (isWestOpaque && !isEastOpaque)
            direction = 5;
        else if (isEastOpaque && !isWestOpaque) direction = 4;

        @event.World.Writer.SetBlockMeta(x, y, z, direction);
    }

    public void UpdateLitState(bool lit, IWorldContext world, int x, int y, int z)
    {
        var meta = world.Reader.GetBlockMeta(x, y, z);
        var furnace = world.Entities.GetBlockEntity<BlockEntity>(x, y, z);
        InventoryUtility.IgnoreBlockRemoval.Value = true;
        world.Writer.SetBlock(x, y, z, lit ? Blocks.Get("lit_furnace").Id : Blocks.Get("furnace").Id);
        InventoryUtility.IgnoreBlockRemoval.Value = false;
        world.Writer.SetBlockMeta(x, y, z, meta);
        furnace?.CancelRemoval();
        world.Entities.SetBlockEntity(x, y, z, furnace!);
    }
}
