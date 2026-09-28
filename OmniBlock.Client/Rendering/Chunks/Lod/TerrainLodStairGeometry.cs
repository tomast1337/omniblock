using OmniBlock.Blocks;
using OmniBlock.Util.Maths;
using Face = OmniBlock.Client.Rendering.Chunks.Lod.TerrainLodShapeGeometry.Face;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>
/// Immutable unit-block stair surfaces shared by local and streamed LODs. No live world or
/// mutable block bounds are needed. Internal half-cell faces are removed once, not per mesh.
/// Metadata bits 0..1 select facing; bit 2 selects the inverted collision shape.
/// </summary>
internal static class TerrainLodStairGeometry
{
    private static readonly Face[][] Shapes = Enumerable.Range(0, 8).Select(Build).ToArray();

    internal static ReadOnlySpan<Face> Get(byte metadata) => Shapes[metadata & 7];

    private static Face[] Build(int metadata)
    {
        var shape = StairShape.GetBounds(metadata);
        List<Face> faces = [];
        for (var x = 0; x < 2; x++)
        for (var y = 0; y < 2; y++)
        for (var z = 0; z < 2; z++)
        {
            if (!Occupied(x, y, z)) continue;
            float x0 = x * .5f, y0 = y * .5f, z0 = z * .5f;
            var x1 = x0 + .5f;
            var y1 = y0 + .5f;
            var z1 = z0 + .5f;
            if (!Occupied(x, y - 1, z))
                faces.Add(new(Side.Down, .5f, (x0,y0,z1), (x0,y0,z0), (x1,y0,z0), (x1,y0,z1)));
            if (!Occupied(x, y + 1, z))
                faces.Add(new(Side.Up, 1, (x1,y1,z1), (x1,y1,z0), (x0,y1,z0), (x0,y1,z1)));
            if (!Occupied(x - 1, y, z))
                faces.Add(new(Side.West, .6f, (x0,y1,z0), (x0,y0,z0), (x0,y0,z1), (x0,y1,z1)));
            if (!Occupied(x + 1, y, z))
                faces.Add(new(Side.East, .6f, (x1,y1,z1), (x1,y0,z1), (x1,y0,z0), (x1,y1,z0)));
            if (!Occupied(x, y, z - 1))
                faces.Add(new(Side.North, .8f, (x1,y1,z0), (x1,y0,z0), (x0,y0,z0), (x0,y1,z0)));
            if (!Occupied(x, y, z + 1))
                faces.Add(new(Side.South, .8f, (x0,y1,z1), (x0,y0,z1), (x1,y0,z1), (x1,y1,z1)));
        }
        return [.. faces];

        bool Occupied(int x, int y, int z)
        {
            if ((uint)x >= 2 || (uint)y >= 2 || (uint)z >= 2) return false;
            var px = x * .5 + .25;
            var py = y * .5 + .25;
            var pz = z * .5 + .25;
            return Inside(shape.Base) || Inside(shape.Step);

            bool Inside(Box box) =>
                px > box.MinX && px < box.MaxX && py > box.MinY && py < box.MaxY &&
                pz > box.MinZ && pz < box.MaxZ;
        }
    }
}
