using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks;

/// <summary>Legacy stair metadata and neighbor-derived collision/render shape.</summary>
public static class StairShape
{
    public readonly record struct Resolved(Box Base, Box Step, Box? Extra);

    /// <summary>Legacy four-facing/two-half metadata; corners are never persisted.</summary>
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

    public static Resolved Resolve(IBlockReader reader, IBlockRuntimeView blocks, int x, int y, int z, int metadata)
    {
        return Resolve(metadata,
            Neighbor(x + 1, z), Neighbor(x - 1, z), Neighbor(x, z + 1), Neighbor(x, z - 1));

        int? Neighbor(int nx, int nz)
        {
            if (!reader.IsPosLoaded(nx, y, nz) ||
                !blocks.TryGetByProtocolId(reader.GetBlockId(nx, y, nz), out var block) ||
                block.RenderType != BlockRendererType.Stairs) return null;
            return reader.GetBlockMeta(nx, y, nz) & 7;
        }
    }

    /// <summary>Pure selector for snapshots and 1:1 LOD cells; null means no stair at that side.</summary>
    public static Resolved Resolve(int metadata, int? east, int? west, int? south, int? north)
    {
        var straight = GetBounds(metadata);
        var facing = metadata & 3;
        var half = metadata & 4;
        var (dx, dz) = Direction(facing);
        var front = Neighbor(dx, dz);
        if (front is { } frontMeta && (frontMeta & 4) == half && Perpendicular(facing, frontMeta & 3))
        {
            var (sideX, sideZ) = Direction(frontMeta & 3);
            if (Neighbor(sideX, sideZ) != (metadata & 7))
                return new Resolved(straight.Base, Intersection(straight.Step, GetBounds(frontMeta).Step), null);
        }
        var back = Neighbor(-dx, -dz);
        if (back is { } backMeta && (backMeta & 4) == half && Perpendicular(facing, backMeta & 3))
        {
            var (sideX, sideZ) = Direction(backMeta & 3);
            if (Neighbor(sideX, sideZ) != (metadata & 7))
            {
                var opposite = GetBounds((facing ^ 1) | half).Step;
                return new Resolved(straight.Base, straight.Step,
                    Intersection(opposite, GetBounds(backMeta).Step));
            }
        }
        return new Resolved(straight.Base, straight.Step, null);

        int? Neighbor(int nx, int nz) => nx switch
        {
            1 => east, -1 => west, _ => nz > 0 ? south : north
        };
    }

    private static bool Perpendicular(int a, int b) => (a < 2) != (b < 2);
    private static (int X, int Z) Direction(int facing) => facing switch
    {
        0 => (1, 0), 1 => (-1, 0), 2 => (0, 1), _ => (0, -1)
    };

    private static Box Intersection(Box a, Box b) => new(
        Math.Max(a.MinX, b.MinX), Math.Max(a.MinY, b.MinY), Math.Max(a.MinZ, b.MinZ),
        Math.Min(a.MaxX, b.MaxX), Math.Min(a.MaxY, b.MaxY), Math.Min(a.MaxZ, b.MaxZ));
}
