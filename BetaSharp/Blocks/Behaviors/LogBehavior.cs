namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Log rendering and leaf decay: bark texture varies by species metadata, and breaking a log
///     marks all leaves within a 4-block radius for decay. Assign to the Visuals and Lifecycle slots.
///     <para>
///         Which block counts as "attached canopy" is required, (see <c>BehaviorRegistry</c>'s <c>"log"</c> entry).
///     </para>
///     <para>
///         Leaf-decay search radius (<paramref name="searchRadius" />) is a required.
///     </para>
/// </summary>
public sealed class LogBehavior(Block canopy, int searchRadius, int top, int[] sides) : IBlockVisuals, IBlockLifecycle
{
    public void OnBreak(Block block, OnBreakEvent @event)
    {
        int regionExtent = searchRadius + 1;
        if (!@event.World.ChunkHost.IsRegionLoaded(@event.X - regionExtent, @event.Y - regionExtent, @event.Z - regionExtent, @event.X + regionExtent, @event.Y + regionExtent, @event.Z + regionExtent))
        {
            return;
        }

        for (int offsetX = -searchRadius; offsetX <= searchRadius; ++offsetX)
        {
            for (int offsetY = -searchRadius; offsetY <= searchRadius; ++offsetY)
            {
                for (int offsetZ = -searchRadius; offsetZ <= searchRadius; ++offsetZ)
                {
                    int neighborBlockId = @event.World.Reader.GetBlockId(@event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ);
                    if (neighborBlockId != canopy.Id) continue;

                    int leavesMeta = @event.World.Reader.GetBlockMeta(@event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ);
                    if ((leavesMeta & 8) == 0)
                    {
                        @event.World.Writer.SetBlockMetaWithoutNotifyingNeighbors(@event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ, leavesMeta | 8);
                    }
                }
            }
        }
    }

    // Four sides for two metadata bits: the species field can hold a value no tree grows, and the
    // list has to answer for it rather than fall back to a species chosen in C#.
    public int GetTexture(Block block, Side side, int meta, int defaultTexture) =>
        side is Side.Up or Side.Down ? top : sides[meta & 3];
}
