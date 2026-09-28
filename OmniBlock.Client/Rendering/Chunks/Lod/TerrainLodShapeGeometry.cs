using OmniBlock.Blocks;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal static class TerrainLodShapeGeometry
{
    internal readonly record struct Face(Side Side, float Shade,
        (float X, float Y, float Z) A, (float X, float Y, float Z) B,
        (float X, float Y, float Z) C, (float X, float Y, float Z) D);

    // Block-relative UVs preserve texture alignment across sub-shapes and boundary-owned rails.
    internal static (float U, float V) Uv(Side side, (float X, float Y, float Z) p) => side switch
    {
        Side.Down => (1 - p.X, 1 - p.Z), Side.Up => (p.X, 1 - p.Z),
        Side.West => (1 - p.Z, 1 - p.Y), Side.East => (p.Z, 1 - p.Y),
        Side.North => (p.X, 1 - p.Y), _ => (1 - p.X, 1 - p.Y)
    };

    internal static Face[] Boxes(ReadOnlySpan<Box> boxes)
    {
        List<Face> faces = [];
        foreach (var box in boxes)
        {
            var x0 = (float)box.MinX; var y0 = (float)box.MinY; var z0 = (float)box.MinZ;
            var x1 = (float)box.MaxX; var y1 = (float)box.MaxY; var z1 = (float)box.MaxZ;
            faces.Add(new(Side.Down, .5f, (x0,y0,z1), (x0,y0,z0), (x1,y0,z0), (x1,y0,z1)));
            faces.Add(new(Side.Up, 1, (x1,y1,z1), (x1,y1,z0), (x0,y1,z0), (x0,y1,z1)));
            faces.Add(new(Side.West, .6f, (x0,y1,z0), (x0,y0,z0), (x0,y0,z1), (x0,y1,z1)));
            faces.Add(new(Side.East, .6f, (x1,y1,z1), (x1,y0,z1), (x1,y0,z0), (x1,y1,z0)));
            faces.Add(new(Side.North, .8f, (x1,y1,z0), (x1,y0,z0), (x0,y0,z0), (x0,y1,z0)));
            faces.Add(new(Side.South, .8f, (x0,y1,z1), (x0,y0,z1), (x1,y0,z1), (x1,y1,z1)));
        }
        return [.. faces];
    }
}
