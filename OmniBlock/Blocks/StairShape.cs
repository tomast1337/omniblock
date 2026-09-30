using OmniBlock.Util.Maths;

namespace OmniBlock.Blocks;

/// <summary>Two non-overlapping boxes for the straight stair shape used by physics and rendering.</summary>
public static class StairShape
{
    /// <summary>Legacy four-facing/two-half metadata; corner shape is derived from neighbors later.</summary>
    public static int PlacementMetadata(float yaw, Side side, float hitY)
    {
        var facing = MathHelper.Floor(yaw * 4.0F / 360.0F + 0.5D) & 3;
        var horizontal = facing switch { 0 => 2, 1 => 1, 2 => 3, _ => 0 };
        return horizontal | (side == Side.Down || (side != Side.Up && hitY > 0.5F) ? 4 : 0);
    }

    public static (Box Base, Box Step) GetBounds(int metadata)
    {
        var inverted = (metadata & 4) != 0;
        var baseBox = new Box(0, inverted ? .5 : 0, 0, 1, inverted ? 1 : .5, 1);
        var y0 = inverted ? 0 : .5;
        var y1 = inverted ? .5 : 1;
        var step = (metadata & 3) switch
        {
            0 => new Box(.5, y0, 0, 1, y1, 1),
            1 => new Box(0, y0, 0, .5, y1, 1),
            2 => new Box(0, y0, .5, 1, y1, 1),
            _ => new Box(0, y0, 0, 1, y1, .5)
        };
        return (baseBox, step);
    }
}
