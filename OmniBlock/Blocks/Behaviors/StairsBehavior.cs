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
        var meta = @event.Placer is null ? 0 : StairShape.PlacementMetadata(@event.Placer.Yaw, @event.Side, @event.HitY);

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
