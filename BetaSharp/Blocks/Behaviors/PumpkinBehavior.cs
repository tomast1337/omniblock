using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Pumpkin / jack-o'-lantern: facing is stamped from the placer's yaw at placement time, and the
/// face texture is swapped for the lit variant when carved. <paramref name="lit"/> distinguishes
/// the two block instances (shared logic, one flag).
/// </summary>
internal sealed class PumpkinBehavior(bool lit) : IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        if (side is Side.Up or Side.Down) return block.TextureId;

        int faceTexture = block.TextureId + 1 + 16;
        if (lit)
        {
            ++faceTexture;
        }

        return meta == 2 && side == Side.North ? faceTexture :
            meta == 3 && side == Side.East ? faceTexture :
            meta == 0 && side == Side.South ? faceTexture :
            meta == 1 && side == Side.West ? faceTexture : block.TextureId + 16;
    }

    public int GetTexture(Block block, Side side, int defaultTexture) => side switch
    {
        Side.Up or Side.Down => block.TextureId,
        Side.South => block.TextureId + 1 + 16,
        _ => block.TextureId + 16
    };

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event) => @event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z);

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (@event.Placer == null) return;

        int direction = MathHelper.Floor(@event.Placer.Yaw * 4.0F / 360.0F + 2.5D) & 3;
        @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, direction);
    }
}
