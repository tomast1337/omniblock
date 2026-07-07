using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Sign support physics: the standing variant needs solid ground below and keeps its fixed
/// post bounding box; the wall variant hangs off the face its metadata points away from and
/// recomputes its slab-shaped box from that facing. Assign to the Physics slot.
/// </summary>
public sealed class SignBehavior(bool isStanding) : IBlockPhysics
{
    private const float TopOffset = 9.0F / 32.0F;
    private const float BottomOffset = 25.0F / 32.0F;
    private const float MinExtent = 0.0F;
    private const float MaxExtent = 1.0F;
    private const float Thickness = 2.0F / 16.0F;

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        if (isStanding) return;

        Side facing = reader.GetBlockMeta(x, y, z).ToSide();

        block.setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
        switch (facing)
        {
            case Side.North:
                block.setBoundingBox(MinExtent, TopOffset, 1.0F - Thickness, MaxExtent, BottomOffset, 1.0F);
                break;
            case Side.South:
                block.setBoundingBox(MinExtent, TopOffset, 0.0F, MaxExtent, BottomOffset, Thickness);
                break;
            case Side.West:
                block.setBoundingBox(1.0F - Thickness, TopOffset, MinExtent, 1.0F, BottomOffset, MaxExtent);
                break;
            case Side.East:
                block.setBoundingBox(0.0F, TopOffset, MinExtent, Thickness, BottomOffset, MaxExtent);
                break;
        }
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        bool shouldBreak = false;
        if (isStanding)
        {
            if (!@event.World.Reader.GetMaterial(@event.X, @event.Y - 1, @event.Z).IsSolid)
            {
                shouldBreak = true;
            }
        }
        else
        {
            Side facing = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z).ToSide();
            shouldBreak = true;
            switch (facing)
            {
                case Side.North when @event.World.Reader.GetMaterial(@event.X, @event.Y, @event.Z + 1).IsSolid:
                case Side.South when @event.World.Reader.GetMaterial(@event.X, @event.Y, @event.Z - 1).IsSolid:
                case Side.West when @event.World.Reader.GetMaterial(@event.X + 1, @event.Y, @event.Z).IsSolid:
                case Side.East when @event.World.Reader.GetMaterial(@event.X - 1, @event.Y, @event.Z).IsSolid:
                    shouldBreak = false;
                    break;
            }
        }

        if (shouldBreak)
        {
            block.dropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }
    }
}
