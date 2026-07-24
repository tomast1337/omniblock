using BetaSharp.Blocks.Materials;
using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Physics, interaction, lifecycle, and visuals for door blocks. Wood doors respond to
///     right-click and redstone; iron doors only respond to redstone. Manages the multi-block
///     dependency between the top and bottom halves.
/// </summary>
internal sealed class DoorBehavior : IBlockPhysics, IBlockInteractable, IBlockLifecycle, IBlockVisuals
{
    private const float Thickness = 3.0F / 16.0F;

    private readonly Material _material;

    public DoorBehavior(Material material) => _material = material;

    public bool OnUse(Block block, OnUseEvent @event)
        => ToggleDoor(block, @event.Player, @event.World, @event.X, @event.Y, @event.Z);

    public void OnBlockBreakStart(Block block, OnBlockBreakStartEvent @event)
        => ToggleDoor(block, @event.Player, @event.World, @event.X, @event.Y, @event.Z);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        int doorId = block.id;

        if ((meta & 8) != 0) // Top half
        {
            if (@event.World.Reader.GetBlockId(@event.X, @event.Y - 1, @event.Z) != doorId)
            {
                @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
            }
            else if (@event.BlockId > 0 && Block.Blocks[@event.BlockId].canEmitRedstonePower())
            {
                int bottomMeta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y - 1, @event.Z);
                NeighborUpdate(block, new OnTickEvent(@event.World, @event.X, @event.Y - 1, @event.Z, bottomMeta, @event.BlockId));
            }
        }
        else // Bottom half
        {
            bool wasBroken = false;

            if (@event.World.Reader.GetBlockId(@event.X, @event.Y + 1, @event.Z) != doorId)
            {
                @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
                wasBroken = true;
            }

            if (!@event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z))
            {
                @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
                wasBroken = true;
                if (@event.World.Reader.GetBlockId(@event.X, @event.Y + 1, @event.Z) == doorId)
                {
                    @event.World.Writer.SetBlock(@event.X, @event.Y + 1, @event.Z, 0);
                }
            }

            if (wasBroken)
            {
                if (!@event.World.IsRemote)
                {
                    block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, meta));
                }
            }
            else if (@event.BlockId > 0 && Block.Blocks[@event.BlockId].canEmitRedstonePower())
            {
                bool isPowered = @event.World.Redstone.IsPowered(@event.X, @event.Y, @event.Z) ||
                                 @event.World.Redstone.IsPowered(@event.X, @event.Y + 1, @event.Z);

                SetOpen(block, @event.World, @event.X, @event.Y, @event.Z, isPowered);
            }
        }
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
        => ApplyBoundingBox(block, SetOpen(reader.GetBlockMeta(x, y, z)));

    public bool CanPlaceAt(Block block, CanPlaceAtContext ctx)
        => ctx.Y < 127 && ctx.World.Reader.ShouldSuffocate(ctx.X, ctx.Y - 1, ctx.Z);

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        if (side is Side.Up or Side.Down) return block.TextureId;

        int facing = SetOpen(meta);
        if (facing is 0 or 2 ^ (side <= Side.South)) return block.TextureId;

        int textureIndex = facing / 2 + ((side.ToInt() & 1) ^ facing);
        textureIndex += (meta & 4) / 4;
        int texture = block.TextureId - (meta & 8) * 2;
        if ((textureIndex & 1) != 0)
        {
            texture = -texture;
        }

        return texture;
    }

    private bool ToggleDoor(Block block, EntityPlayer player, IWorldContext world, int x, int y, int z)
    {
        if (_material == Material.Metal) return true;

        int meta = world.Reader.GetBlockMeta(x, y, z);
        int doorId = block.id;

        if ((meta & 8) != 0)
        {
            if (world.Reader.GetBlockId(x, y - 1, z) == doorId)
            {
                y--;
                meta = world.Reader.GetBlockMeta(x, y, z);
            }
            else
            {
                return true;
            }
        }

        if (world.Reader.GetBlockId(x, y + 1, z) == doorId)
        {
            world.Writer.SetBlockMeta(x, y + 1, z, (meta ^ 4) + 8);
        }

        world.Writer.SetBlockMeta(x, y, z, meta ^ 4);

        world.Broadcaster.SetBlocksDirty(x, y - 1, z, x, y + 1, z);
        world.Broadcaster.WorldEvent(player, 1003, x, y, z, 0);
        return true;
    }

    /// <summary>
    ///     Computes the visual facing (0-3) from door metadata, accounting for the
    ///     open/closed swing hinge.
    /// </summary>
    private static int SetOpen(int meta) => (meta & 4) == 0 ? (meta - 1) & 3 : meta & 3;

    /// <summary>Returns true when the door is in the open position.</summary>
    public static bool IsOpen(int meta) => (meta & 4) != 0;

    private static void SetOpen(Block block, IWorldContext world, int x, int y, int z, bool open)
    {
        if (world.IsRemote) return;

        int meta = world.Reader.GetBlockMeta(x, y, z);
        int doorId = block.id;

        if ((meta & 8) != 0)
        {
            if (world.Reader.GetBlockId(x, y - 1, z) == doorId)
            {
                y -= 1;
                meta = world.Reader.GetBlockMeta(x, y, z);
            }
            else
            {
                return;
            }
        }

        if (IsOpen(meta) == open) return;

        if (world.Reader.GetBlockId(x, y + 1, z) == doorId)
        {
            world.Writer.SetBlockMeta(x, y + 1, z, (meta ^ 4) + 8);
        }

        world.Writer.SetBlockMeta(x, y, z, meta ^ 4);
        world.Broadcaster.SetBlocksDirty(x, y - 1, z, x, y + 1, z);
        world.Broadcaster.WorldEvent(1003, x, y, z, 0);
    }

    private static void ApplyBoundingBox(Block block, int facing)
    {
        block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F, 1.0F);
        switch (facing)
        {
            case 0: block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, Thickness); break;
            case 1: block.SetBoundingBox(1.0F - Thickness, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F); break;
            case 2: block.SetBoundingBox(0.0F, 0.0F, 1.0F - Thickness, 1.0F, 1.0F, 1.0F); break;
            case 3: block.SetBoundingBox(0.0F, 0.0F, 0.0F, Thickness, 1.0F, 1.0F); break;
        }
    }
}
