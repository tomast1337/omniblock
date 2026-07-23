using BetaSharp.Blocks.Materials;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Physics, interaction, and lifecycle for trapdoor blocks. Wood trapdoors respond to
///     right-click and redstone; iron trapdoors only respond to redstone.
/// </summary>
internal sealed class TrapDoorBehavior : IBlockPhysics, IBlockInteractable, IBlockLifecycle
{
    private const float Thickness = 3.0F / 16.0F;

    private readonly Material _material;

    public TrapDoorBehavior(Material material) => _material = material;

    public bool OnUse(Block block, OnUseEvent ctx) => ToggleState(block, ctx.World, ctx.World.Broadcaster, ctx.X, ctx.Y, ctx.Z);

    public void OnBlockBreakStart(Block block, OnBlockBreakStartEvent ctx)
        => ToggleState(block, ctx.World, ctx.World.Broadcaster, ctx.X, ctx.Y, ctx.Z);

    public void OnPlaced(Block block, OnPlacedEvent ctx)
    {
        sbyte meta = ctx.Direction switch
        {
            Side.North => 0,
            Side.South => 1,
            Side.West => 2,
            Side.East => 3,
            _ => 0
        };

        ctx.World.Writer.SetBlockMeta(ctx.X, ctx.Y, ctx.Z, meta);
    }

    public void NeighborUpdate(Block block, OnTickEvent ctx)
    {
        if (ctx.World.IsRemote) return;

        int meta = ctx.World.Reader.GetBlockMeta(ctx.X, ctx.Y, ctx.Z);
        int xPos = ctx.X;
        int zPos = ctx.Z;

        switch (meta & 3)
        {
            case 0: zPos = ctx.Z + 1; break;
            case 1: --zPos; break;
            case 2: xPos = ctx.X + 1; break;
            case 3: --xPos; break;
        }

        if (!ctx.World.Reader.ShouldSuffocate(xPos, ctx.Y, zPos))
        {
            ctx.World.Writer.SetBlock(ctx.X, ctx.Y, ctx.Z, 0);
            block.DropStacks(new OnDropEvent(ctx.World, ctx.X, ctx.Y, ctx.Z, meta));
        }
        else
        {
            bool isPowered = ctx.World.Redstone.IsPowered(ctx.X, ctx.Y, ctx.Z);
            SetOpen(ctx, isPowered);
        }
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
        => ApplyBoundingBox(block, reader.GetBlockMeta(x, y, z));

    public void SetupRenderBoundingBox(Block block)
    {
        const float height = 3.0F / 16.0F;
        block.SetBoundingBox(0.0F, 0.5F - height / 2.0F, 0.0F, 1.0F, 0.5F + height / 2.0F, 1.0F);
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext ctx)
    {
        int x = ctx.X;
        int y = ctx.Y;
        int z = ctx.Z;

        switch (ctx.Direction)
        {
            case 0:
            case Side.Up:
                return false;
            case Side.North: ++z; break;
            case Side.South: --z; break;
            case Side.West: ++x; break;
            case Side.East: --x; break;
        }

        return ctx.World.Reader.ShouldSuffocate(x, y, z);
    }

    private bool ToggleState(Block block, IWorldContext world, WorldEventBroadcaster broadcaster, int x, int y, int z)
    {
        if (world.IsRemote) return true;
        if (_material == Material.Metal) return true;
        int meta = world.Reader.GetBlockMeta(x, y, z);
        world.Writer.SetBlockMeta(x, y, z, meta ^ 4);
        broadcaster.WorldEvent(1003, x, y, z, 0);
        return true;
    }

    private static void ApplyBoundingBox(Block block, int meta)
    {
        block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, Thickness, 1.0F);

        if (!IsOpen(meta)) return;

        switch (meta & 3)
        {
            case 0: block.SetBoundingBox(0.0F, 0.0F, 1.0F - Thickness, 1.0F, 1.0F, 1.0F); break;
            case 1: block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, Thickness); break;
            case 2: block.SetBoundingBox(1.0F - Thickness, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F); break;
            case 3: block.SetBoundingBox(0.0F, 0.0F, 0.0F, Thickness, 1.0F, 1.0F); break;
        }
    }

    private static void SetOpen(OnTickEvent ctx, bool open)
    {
        if (ctx.World.IsRemote) return;

        int x = ctx.X;
        int y = ctx.Y;
        int z = ctx.Z;
        int meta = ctx.World.Reader.GetBlockMeta(x, y, z);

        if (IsOpen(meta) == open) return;

        ctx.World.Writer.SetBlockMeta(x, y, z, meta ^ 4);
        ctx.World.Broadcaster.WorldEvent(1003, x, y, z, 0);
    }

    public static bool IsOpen(int meta) => (meta & 4) != 0;
}
