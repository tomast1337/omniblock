using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Physics, lifecycle, and visuals for slab blocks. A single instance handles both
///     single and double slabs via the <c>_isDoubleSlab</c> constructor parameter.
/// </summary>
internal sealed class SlabBehavior : IBlockPhysics, IBlockLifecycle, IBlockVisuals
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
        if (_isDoubleSlab) return;

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
        if (blockBelowId != BlockRegistry.Get("slab").Id) return;
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        @event.World.Writer.SetBlock(@event.X, @event.Y - 1, @event.Z, BlockRegistry.Get("double_slab").Id, slabMeta);
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        if (_isDoubleSlab)
        {
            block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
        }
        else
        {
            int meta = reader.GetBlockMeta(x, y, z);
            bool isTop = (meta & 8) != 0;
            if (isTop)
            {
                block.SetBoundingBox(0.0F, 0.5F, 0.0F, 1.0F, 1.0F, 1.0F);
            }
            else
            {
                block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 0.5F, 1.0F);
            }
        }
    }

    public int GetTexture(Block block, Side side, int defaultTexture)
        => GetTexture(block, side, 0, defaultTexture);

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        // Metadata past the last kind is not a slab at all. It showed the first kind's side texture
        // on every face rather than failing, which is still the least surprising thing to draw.
        if (meta < 0 || meta >= _variants.Length) return _variants[0].Side;

        BlockFaceTextures faces = _variants[meta];
        return side switch
        {
            Side.Down => faces.Bottom,
            Side.Up => faces.Top,
            _ => faces.Side
        };
    }

    public bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility) =>
        side == Side.Up
        || (defaultVisibility && (side == Side.Down || reader.GetBlockId(x, y, z) != block.Id));
}
