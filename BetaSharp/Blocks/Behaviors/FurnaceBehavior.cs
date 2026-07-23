using BetaSharp.Blocks.Entities;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Blocks.Behaviors;

internal sealed class FurnaceBehavior : IBlockInteractable, IBlockLifecycle, IBlockPhysics, IBlockTicker, IBlockVisuals
{
    private const float FlameParticleOffset = 0.52F;

    private static readonly ILogger<FurnaceBehavior> s_logger = Log.Instance.For<FurnaceBehavior>();
    private readonly bool _lit;

    public FurnaceBehavior(bool lit) => _lit = lit;

    public bool OnUse(Block block, OnUseEvent @event)
    {
        if (@event.World.IsRemote) return true;

        BlockEntityFurnace? furnace = @event.World.Entities.GetBlockEntity<BlockEntityFurnace>(@event.X, @event.Y, @event.Z);
        if (furnace == null) return false;

        @event.Player.openFurnaceScreen(furnace);
        return true;
    }

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (@event.Placer != null)
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
        else
        {
            UpdateDirection(@event);
        }

        InventoryUtility.OnPlaced(block, @event);
    }

    public void OnBreak(Block block, OnBreakEvent @event) => InventoryUtility.OnBreak(block, @event);

    public void RandomDisplayTick(Block block, OnTickEvent @event)
    {
        if (!_lit) return;

        Side rotation = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z).ToSide();
        float particleX = @event.X + 0.5F;
        float particleY = @event.Y + 0.0F + Random.Shared.NextSingle() * 6.0F / 16.0F;
        float particleZ = @event.Z + 0.5F;
        float randomOffset = Random.Shared.NextSingle() * 0.6F - 0.3F;

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

    public int GetTexture(Block block, Side side, int defaultTexture) =>
        side switch
        {
            Side.Up or Side.Down => BlockTextures.FurnaceTop,
            Side.South => BlockTextures.FurnaceFrontUnlit,
            _ => defaultTexture
        };

    public int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side side, int defaultTexture)
    {
        if (side is Side.Up or Side.Down) return BlockTextures.FurnaceTop;

        Side facing = reader.GetBlockMeta(x, y, z).ToSide();
        if (side != facing) return block.TextureId;

        return _lit ? BlockTextures.FurnaceFrontLit : BlockTextures.FurnaceFrontUnlit;
    }

    private static void UpdateDirection(OnPlacedEvent @event)
    {
        if (@event.World.IsRemote)
        {
            return;
        }

        IBlockReader reader = @event.World.Reader;
        int x = @event.X, y = @event.Y, z = @event.Z;

        bool isNorthOpaque = Block.BlocksOpaque[reader.GetBlockId(x, y, z - 1)];
        bool isSouthOpaque = Block.BlocksOpaque[reader.GetBlockId(x, y, z + 1)];
        bool isWestOpaque = Block.BlocksOpaque[reader.GetBlockId(x - 1, y, z)];
        bool isEastOpaque = Block.BlocksOpaque[reader.GetBlockId(x + 1, y, z)];

        byte direction = 3;
        if (isNorthOpaque && !isSouthOpaque)
        {
            direction = 3;
        }
        else if (isSouthOpaque && !isNorthOpaque)
        {
            direction = 2;
        }

        if (isWestOpaque && !isEastOpaque)
        {
            direction = 5;
        }
        else if (isEastOpaque && !isWestOpaque)
        {
            direction = 4;
        }

        @event.World.Writer.SetBlockMeta(x, y, z, direction);
    }

    public static void UpdateLitState(bool lit, IWorldContext world, int x, int y, int z)
    {
        int meta = world.Reader.GetBlockMeta(x, y, z);
        BlockEntity? furnace = world.Entities.GetBlockEntity<BlockEntity>(x, y, z);
        InventoryUtility.IgnoreBlockRemoval.Value = true;
        world.Writer.SetBlock(x, y, z, lit ? BlockRegistry.Get("lit_furnace").id : BlockRegistry.Get("furnace").id);
        InventoryUtility.IgnoreBlockRemoval.Value = false;
        world.Writer.SetBlockMeta(x, y, z, meta);
        furnace?.CancelRemoval();
        world.Entities.SetBlockEntity(x, y, z, furnace!);
    }
}
