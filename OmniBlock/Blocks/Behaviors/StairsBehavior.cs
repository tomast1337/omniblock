using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Physics, lifecycle, and visuals for stair blocks. Takes a lazy <c>baseBlock</c>
///     provider so the behavior can be instantiated before the material block static is
///     initialized. The provider is only resolved at texture-sampling time (render thread).
/// </summary>
internal sealed class StairsBehavior(Func<Block> baseBlock) : IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        var meta = 0;
        if (@event.Placer != null)
        {
            var facing = MathHelper.Floor(@event.Placer.Yaw * 4.0F / 360.0F + 0.5D) & 3;

            // A horizontal-face click chooses the half from the actual hit height.
            if (@event.Side == Side.Down || (@event.Side != Side.Up && @event.HitY > 0.5F)) meta |= 4;

            if (facing == 0) meta |= 2;

            if (facing == 1) meta |= 1;

            if (facing == 2) meta |= 3;
            // facing == 3 → meta 0 (south-facing), already the default
        }

        @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z, block.Id);
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z) => block.SetRuntimeBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);

    public void AddCollisionBoxes(Block block, IBlockReader reader, int x, int y, int z, Box queryBox, List<Box> results)
    {
        var shape = StairShape.GetBounds(reader.GetBlockMeta(x, y, z));
        var baseOffset = shape.Base.Offset(x, y, z);
        var stepOffset = shape.Step.Offset(x, y, z);
        if (queryBox.Intersects(baseOffset)) results.Add(baseOffset);
        if (queryBox.Intersects(stepOffset)) results.Add(stepOffset);
    }

    public int GetTexture(Block block, Side side, int defaultTexture) => baseBlock().GetTexture(side);

    public int GetTexture(Block block, Side side, int meta, int defaultTexture) => baseBlock().GetTexture(side, meta);

    public int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side side, int defaultTexture) => baseBlock().GetTextureId(reader, x, y, z, side);
}
