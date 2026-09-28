using OmniBlock.Util.Maths;

namespace OmniBlock.Blocks;

/// <summary>Visual post and half-rails. Collision remains the existing raised fence box.</summary>
public static class FenceShape
{
    public const int West = 1, East = 2, North = 4, South = 8;
    private static readonly Box[][] Shapes = Enumerable.Range(0, 16).Select(Build).ToArray();
    private static readonly Box[][] Rails = [BuildRails(West), BuildRails(East), BuildRails(North), BuildRails(South)];

    public static ReadOnlySpan<Box> GetBounds(int connections) => Shapes[connections & 15];
    public static int Bit(Side side) => side switch
    {
        Side.West => West, Side.East => East, Side.North => North, Side.South => South,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };
    public static ReadOnlySpan<Box> GetRails(Side side) => Rails[side switch
    {
        Side.West => 0, Side.East => 1, Side.North => 2, Side.South => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    }];

    private static Box[] Build(int connections)
    {
        List<Box> boxes = [new(6d / 16, 0, 6d / 16, 10d / 16, 1, 10d / 16)];
        foreach (var bit in new[] { West, East, North, South })
            if ((connections & bit) != 0) boxes.AddRange(BuildRails(bit));
        return [.. boxes];
    }

    private static Box[] BuildRails(int side)
    {
        var x0 = side == West ? 0 : side == East ? 10d / 16 : 7d / 16;
        var x1 = side == East ? 1 : side == West ? 6d / 16 : 9d / 16;
        var z0 = side == North ? 0 : side == South ? 10d / 16 : 7d / 16;
        var z1 = side == South ? 1 : side == North ? 6d / 16 : 9d / 16;
        return [new(x0, 6d / 16, z0, x1, 9d / 16, z1),
                new(x0, 12d / 16, z0, x1, 15d / 16, z1)];
    }
}
