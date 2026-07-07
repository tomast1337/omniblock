using BetaSharp.Entities;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Physics, lifecycle, and visuals for stair blocks. Takes a lazy <c>baseBlock</c>
/// provider so the behavior can be instantiated before the material block static is
/// initialized. The provider is only resolved at texture-sampling time (render thread).
/// </summary>
internal sealed class StairsBehavior : IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    private readonly Func<Block> _baseBlock;

    public StairsBehavior(Func<Block> baseBlock) => _baseBlock = baseBlock;

    // ── IBlockLifecycle ───────────────────────────────────────────

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        int meta = 0;
        if (@event.Placer != null)
        {
            int facing = MathHelper.Floor(@event.Placer.Yaw * 4.0F / 360.0F + 0.5D) & 3;

            // Upside-down stair: player clicked the bottom face of a block above.
            if (@event.Side == Side.Down)
                meta |= 4;

            if (facing == 0) meta |= 2;
            if (facing == 1) meta |= 1;
            if (facing == 2) meta |= 3;
            // facing == 3 → meta 0 (south-facing), already the default
        }

        @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z, block.id);
    }

    // ── IBlockPhysics ─────────────────────────────────────────────

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
        => block.setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);

    public void AddCollisionBoxes(Block block, IBlockReader reader, int x, int y, int z, Box queryBox, List<Box> results)
    {
        int meta = reader.GetBlockMeta(x, y, z);
        int facing = meta & 3;
        bool upsideDown = (meta & 4) != 0;

        Box lower = facing switch
        {
            0 => upsideDown
                ? new Box(0.0F, 0.5F, 0.0F, 0.5F, 1.0F, 1.0F)
                : new Box(0.0F, 0.0F, 0.0F, 0.5F, 0.5F, 1.0F),
            1 => upsideDown
                ? new Box(0.5F, 0.5F, 0.0F, 1.0F, 1.0F, 1.0F)
                : new Box(0.5F, 0.0F, 0.0F, 1.0F, 0.5F, 1.0F),
            2 => upsideDown
                ? new Box(0.0F, 0.5F, 0.0F, 1.0F, 1.0F, 0.5F)
                : new Box(0.0F, 0.0F, 0.0F, 1.0F, 0.5F, 0.5F),
            _ => upsideDown
                ? new Box(0.0F, 0.5F, 0.5F, 1.0F, 1.0F, 1.0F)
                : new Box(0.0F, 0.0F, 0.5F, 1.0F, 0.5F, 1.0F)
        };

        Box upper = facing switch
        {
            0 => upsideDown
                ? new Box(0.5F, 0.0F, 0.0F, 1.0F, 0.5F, 1.0F)
                : new Box(0.5F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F),
            1 => upsideDown
                ? new Box(0.0F, 0.0F, 0.0F, 0.5F, 0.5F, 1.0F)
                : new Box(0.0F, 0.0F, 0.0F, 0.5F, 1.0F, 1.0F),
            2 => upsideDown
                ? new Box(0.0F, 0.0F, 0.5F, 1.0F, 0.5F, 1.0F)
                : new Box(0.0F, 0.0F, 0.5F, 1.0F, 1.0F, 1.0F),
            _ => upsideDown
                ? new Box(0.0F, 0.0F, 0.0F, 1.0F, 0.5F, 0.5F)
                : new Box(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 0.5F)
        };

        Box lowerOffset = lower.Offset(x, y, z);
        if (queryBox.Intersects(lowerOffset)) results.Add(lowerOffset);

        Box upperOffset = upper.Offset(x, y, z);
        if (queryBox.Intersects(upperOffset)) results.Add(upperOffset);
    }

    // ── IBlockVisuals ─────────────────────────────────────────────

    public int GetTexture(Block block, Side side, int defaultTexture)
        => _baseBlock().GetTexture(side);

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
        => _baseBlock().GetTexture(side, meta);

    public int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side side, int defaultTexture)
        => _baseBlock().GetTextureId(reader, x, y, z, side);
}
