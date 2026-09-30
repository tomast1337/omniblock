using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Physics, lifecycle, and visuals for slab blocks. A single instance handles both
///     single and double slabs via the <c>_isDoubleSlab</c> constructor parameter.
/// </summary>
internal sealed class SlabBehavior : BlockRuntimeBehavior, IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    public static readonly string[] Names = ["stone", "sand", "wood", "cobble"];

    private readonly bool _isDoubleSlab;
    private readonly BlockFaceTextures[] _variants;

    public SlabBehavior(bool isDoubleSlab, BlockFaceTextures[] variants)
    {
        _isDoubleSlab = isDoubleSlab;
        _variants = variants;
    }

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        // Placement chooses the half and checks same-cell merges before writing the block.
        // OnPlaced must not move a newly placed slab into another cell after collision checks.
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        if (_isDoubleSlab)
        {
            block.SetRuntimeBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
        }
        else
        {
            var meta = reader.GetBlockMeta(x, y, z);
            var isTop = (meta & 8) != 0;
            if (isTop)
                block.SetRuntimeBoundingBox(0.0F, 0.5F, 0.0F, 1.0F, 1.0F, 1.0F);
            else
                block.SetRuntimeBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 0.5F, 1.0F);
        }
    }

    public int GetTexture(Block block, Side side, int defaultTexture) => GetTexture(block, side, 0, defaultTexture);

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        // Metadata past the last kind is not a slab at all. It showed the first kind's side texture
        // on every face rather than failing, which is still the least surprising thing to draw.
        if (meta < 0 || meta >= _variants.Length) return _variants[0].Side;

        var faces = _variants[meta];
        return side switch
        {
            Side.Down => faces.Bottom,
            Side.Up => faces.Top,
            _ => faces.Side
        };
    }

    public bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility)
    {
        return side == Side.Up
               || (defaultVisibility && (side == Side.Down || reader.GetBlockId(x, y, z) != block.Id));
    }
}
