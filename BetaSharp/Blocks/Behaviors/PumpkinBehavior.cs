using BetaSharp.Util.Maths;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Pumpkin / jack-o'-lantern: facing is stamped from the placer's yaw at placement time, and the
///     face texture is swapped for the lit variant when carved. <paramref name="lit" /> distinguishes
///     the two block instances (shared logic, one flag).
/// </summary>
internal sealed class PumpkinBehavior(int top, int side, int face, int itemFace) : IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (@event.Placer == null) return;
        int direction = MathHelper.Floor(@event.Placer.Yaw * 4.0F / 360.0F + 2.5D) & 3;
        @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, direction);
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event) => @event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z);

    public int GetTexture(Block block, Side renderSide, int meta, int defaultTexture)
    {
        if (renderSide is Side.Up or Side.Down) return top;
        return meta == 2 && renderSide == Side.North ? face :
            meta == 3 && renderSide == Side.East ? face :
            meta == 0 && renderSide == Side.South ? face :
            meta == 1 && renderSide == Side.West ? face : side;
    }

    // Without a world there is no facing to read, so the south face is carved by convention. A lit
    // pumpkin shows the unlit carving here: the item icon has never lit up, and it is a separate
    // texture rather than the same one, so the two have to be named apart.
    public int GetTexture(Block block, Side renderSide, int defaultTexture) => renderSide switch
    {
        Side.Up or Side.Down => top,
        Side.South => itemFace,
        _ => side
    };
}
