namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Log rendering and leaf decay: bark texture varies by species metadata, and breaking a log
/// marks all leaves within a 4-block radius for decay. Assign to the Visuals and Lifecycle slots.
/// </summary>
public sealed class LogBehavior : IBlockVisuals, IBlockLifecycle
{
    private const sbyte SearchRadius = 4;
    private const int RegionExtent = SearchRadius + 1;

    public int GetTexture(Block block, Side side, int meta, int defaultTexture) => side switch
    {
        Side.Up or Side.Down => BlockTextures.LogTop,
        _ => meta == 1 ? BlockTextures.LogPineSide : meta == 2 ? BlockTextures.LogBirchSide : BlockTextures.LogOakSide
    };

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        if (!@event.World.ChunkHost.IsRegionLoaded(@event.X - RegionExtent, @event.Y - RegionExtent, @event.Z - RegionExtent, @event.X + RegionExtent, @event.Y + RegionExtent, @event.Z + RegionExtent))
        {
            return;
        }

        for (int offsetX = -SearchRadius; offsetX <= SearchRadius; ++offsetX)
        {
            for (int offsetY = -SearchRadius; offsetY <= SearchRadius; ++offsetY)
            {
                for (int offsetZ = -SearchRadius; offsetZ <= SearchRadius; ++offsetZ)
                {
                    int neighborBlockId = @event.World.Reader.GetBlockId(@event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ);
                    if (neighborBlockId != Block.Leaves.id) continue;

                    int leavesMeta = @event.World.Reader.GetBlockMeta(@event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ);
                    if ((leavesMeta & 8) == 0)
                    {
                        @event.World.Writer.SetBlockMetaWithoutNotifyingNeighbors(@event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ, leavesMeta | 8);
                    }
                }
            }
        }
    }
}
