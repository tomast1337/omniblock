using OmniBlock.Worlds.Lod;
using System.Security.Cryptography;
using System.Text;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>
/// Immutable one-cell neighbor strips for a 1:1 spatial tile. The mesh worker retains only
/// edge-column references, never a neighboring tile or a live-world reader. Missing or coarser
/// neighbors intentionally remain unknown until a matching source arrives.
/// </summary>
internal sealed class TerrainLodStairBorder
{
    private readonly TerrainLodColumn?[] _west;
    private readonly TerrainLodColumn?[] _east;
    private readonly TerrainLodColumn?[] _north;
    private readonly TerrainLodColumn?[] _south;

    private TerrainLodStairBorder(string identity, TerrainLodColumn?[] west,
        TerrainLodColumn?[] east, TerrainLodColumn?[] north, TerrainLodColumn?[] south)
    {
        Identity = identity;
        _west = west;
        _east = east;
        _north = north;
        _south = south;
    }

    internal string Identity { get; }

    internal int? StairAt(int x, int z, int y, int width)
    {
        TerrainLodColumn? column = x switch
        {
            -1 when (uint)z < (uint)width => _west[z],
            var edge when edge == width && (uint)z < (uint)width => _east[z],
            _ => z switch
            {
                -1 when (uint)x < (uint)width => _north[x],
                var edge when edge == width && (uint)x < (uint)width => _south[x],
                _ => null
            }
        };
        if (column is null || (uint)y >= (uint)column.WorldHeight) return null;
        var material = column.At(y).Material;
        return material.Geometry == TerrainLodGeometryClass.Stairs ? material.Metadata : null;
    }

    internal static TerrainLodStairBorder Capture(TerrainLodColumnTile owner,
        Func<TerrainLodTileKey, TerrainLodColumnTile?> lookup)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(lookup);
        var width = owner.Width;
        var west = new TerrainLodColumn?[width];
        var east = new TerrainLodColumn?[width];
        var north = new TerrainLodColumn?[width];
        var south = new TerrainLodColumn?[width];
        if (owner.HorizontalSampleLevel != 0)
            return new TerrainLodStairBorder("not-1-to-1", west, east, north, south);

        var hashes = new string[4];
        CaptureSide(-1, 0, west, 0);
        CaptureSide(1, 0, east, 1);
        CaptureSide(0, -1, north, 2);
        CaptureSide(0, 1, south, 3);
        return new TerrainLodStairBorder(string.Join('|', hashes), west, east, north, south);

        void CaptureSide(int dx, int dz, TerrainLodColumn?[] target, int index)
        {
            var nx = (long)owner.Key.X + dx;
            var nz = (long)owner.Key.Z + dz;
            if (nx is < int.MinValue or > int.MaxValue || nz is < int.MinValue or > int.MaxValue)
            {
                hashes[index] = "";
                return;
            }
            var neighbor = lookup(new TerrainLodTileKey(owner.Key.Level, (int)nx, (int)nz));
            if (neighbor is null || neighbor.HorizontalSampleLevel != 0 ||
                neighbor.Width != width || neighbor.WorldHeight != owner.WorldHeight)
            {
                hashes[index] = "";
                return;
            }
            var evidence = new StringBuilder();
            for (var along = 0; along < width; along++)
            {
                target[along] = index switch
                {
                    0 => neighbor[width - 1, along],
                    1 => neighbor[0, along],
                    2 => neighbor[along, width - 1],
                    _ => neighbor[along, 0]
                };
                foreach (var span in target[along]!.Spans)
                    if (span.Material.Geometry == TerrainLodGeometryClass.Stairs)
                        evidence.Append(along).Append(',').Append(span.BottomY).Append(',')
                            .Append(span.TopY).Append(',').Append(span.Material.Metadata).Append(';');
            }
            // Only stair evidence affects corner selection. A light or interior edit in the
            // neighbor must not rebuild this complete tile body.
            hashes[index] = evidence.Length == 0 ? "" :
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence.ToString())));
        }
    }
}
