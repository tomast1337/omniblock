using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Physics, lifecycle, and visuals for slab blocks. A single instance handles both
/// single and double slabs via the <c>_isDoubleSlab</c> constructor parameter.
/// </summary>
internal sealed class SlabBehavior : IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    public static readonly string[] Names = ["stone", "sand", "wood", "cobble"];

    private readonly bool _isDoubleSlab;

    public SlabBehavior(bool isDoubleSlab) => _isDoubleSlab = isDoubleSlab;

    // ── IBlockLifecycle ───────────────────────────────────────────

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (!_isDoubleSlab)
        {
            // Top-slab placement: Side.Down means the player clicked the bottom face of a
            // block above, which places a slab in the upper half of this block space.
            if (@event.Side == Side.Down)
            {
                int existingMeta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
                @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, existingMeta | 8);
            }

            // Double-slab merge: if the block below is a single slab with matching meta,
            // convert both into a double slab.
            int blockBelowId = @event.World.Reader.GetBlockId(@event.X, @event.Y - 1, @event.Z);
            int slabMeta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
            int blockBelowMeta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y - 1, @event.Z);
            if (slabMeta != blockBelowMeta) return;
            if (blockBelowId != Block.Slab.id) return;
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
            @event.World.Writer.SetBlock(@event.X, @event.Y - 1, @event.Z, Block.DoubleSlab.id, slabMeta);
        }
    }

    // ── IBlockPhysics ─────────────────────────────────────────────

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        if (_isDoubleSlab)
        {
            block.setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
        }
        else
        {
            int meta = reader.GetBlockMeta(x, y, z);
            bool isTop = (meta & 8) != 0;
            if (isTop)
                block.setBoundingBox(0.0F, 0.5F, 0.0F, 1.0F, 1.0F, 1.0F);
            else
                block.setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 0.5F, 1.0F);
        }
    }

    // ── IBlockVisuals ─────────────────────────────────────────────

    public int GetTexture(Block block, Side side, int defaultTexture)
        => GetTexture(block, side, 0, defaultTexture);

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        return meta switch
        {
            0 => side <= Side.Up ? BlockTextures.StoneSlabTop : BlockTextures.StoneSlabSide,
            1 => side switch
            {
                Side.Down => BlockTextures.SandstoneBottom,
                Side.Up => BlockTextures.SandstoneTop,
                _ => BlockTextures.SandstoneSide
            },
            2 => BlockTextures.OakPlanks,
            3 => BlockTextures.Cobblestone,
            _ => BlockTextures.StoneSlabSide
        };
    }

    public bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility)
    {
        return side == Side.Up
            || (defaultVisibility && (side == Side.Down || reader.GetBlockId(x, y, z) != block.id));
    }
}
