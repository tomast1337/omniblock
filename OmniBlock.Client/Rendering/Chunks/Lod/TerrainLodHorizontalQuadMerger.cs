using OmniBlock.Client.Rendering.Core;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>
///     Conservatively joins compatible coplanar LOD quads into rectangles. LOD water used to emit
///     one top and bottom quad per reduced column; even with internal walls culled, alpha blending
///     made that tessellation visible as a three-dimensional grid.
/// </summary>
internal static class TerrainLodHorizontalQuadMerger
{
    public static MergedQuads Merge(
        ChunkVertex[] vertices,
        ChunkLightVertex[] lights)
    {
        if (vertices.Length != lights.Length || vertices.Length % 4 != 0)
            throw new ArgumentException("LOD geometry and light streams must contain matching quads.");
        if (vertices.Length == 0) return new MergedQuads([], []);

        var quadCount = vertices.Length / 4;
        var candidates = new Candidate?[quadCount];
        Dictionary<CellKey, int> cells = [];
        for (var quad = 0; quad < quadCount; quad++)
        {
            var vertexOffset = quad * 4;
            if (!TryCandidate(
                    vertices.AsSpan(vertexOffset, 4),
                    lights.AsSpan(vertexOffset, 4),
                    out var candidate))
                continue;

            var cell = new CellKey(candidate.Style, candidate.MinX, candidate.MinZ);
            // Overlapping quads are presentation layers, not one continuous surface. Leave both
            // untouched rather than picking one arbitrarily.
            if (!cells.TryAdd(cell, quad))
            {
                candidates[quad] = null;
                candidates[cells[cell]] = null;
                cells.Remove(cell);
                continue;
            }
            candidates[quad] = candidate;
        }

        List<ChunkVertex> mergedVertices = new(vertices.Length);
        List<ChunkLightVertex> mergedLights = new(lights.Length);
        var consumed = new bool[quadCount];
        for (var quad = 0; quad < quadCount; quad++)
        {
            if (consumed[quad]) continue;
            if (candidates[quad] is not { } candidate)
            {
                CopyQuad(quad);
                continue;
            }

            var maxColumns = ushort.MaxValue / candidate.Style.TileU;
            var maxRows = ushort.MaxValue / candidate.Style.TileV;
            var columns = 1;
            while (columns < maxColumns &&
                   TryCell(candidate, columns, 0, out var next) &&
                   !consumed[next])
                columns++;

            var rows = 1;
            while (rows < maxRows)
            {
                var complete = true;
                for (var column = 0; column < columns; column++)
                {
                    if (!TryCell(candidate, column, rows, out var next) || consumed[next])
                    {
                        complete = false;
                        break;
                    }
                }
                if (!complete) break;
                rows++;
            }

            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                _ = TryCell(candidate, column, row, out var merged);
                consumed[merged] = true;
            }

            AddMerged(candidate, columns, rows);
        }

        return new MergedQuads([.. mergedVertices], [.. mergedLights]);

        bool TryCell(Candidate origin, int column, int row, out int quad)
        {
            var x = checked((short)(origin.MinX + column * origin.Style.Width));
            var z = checked((short)(origin.MinZ + row * origin.Style.Depth));
            return cells.TryGetValue(new CellKey(origin.Style, x, z), out quad) &&
                   candidates[quad] is not null;
        }

        void CopyQuad(int quad)
        {
            var offset = quad * 4;
            for (var corner = 0; corner < 4; corner++)
            {
                mergedVertices.Add(vertices[offset + corner]);
                mergedLights.Add(lights[offset + corner]);
            }
        }

        void AddMerged(Candidate candidate, int columns, int rows)
        {
            var style = candidate.Style;
            var maxX = checked((short)(candidate.MinX + columns * style.Width));
            var maxZ = checked((short)(candidate.MinZ + rows * style.Depth));
            var u = checked((ushort)(style.TileU * columns));
            var v = checked((ushort)(style.TileV * rows));

            if (style.FacingUp)
            {
                AddVertex(maxX, style.Plane, maxZ, u, 0);
                AddVertex(maxX, style.Plane, candidate.MinZ, u, v);
                AddVertex(candidate.MinX, style.Plane, candidate.MinZ, 0, v);
                AddVertex(candidate.MinX, style.Plane, maxZ, 0, 0);
            }
            else
            {
                AddVertex(candidate.MinX, style.Plane, maxZ, u, 0);
                AddVertex(candidate.MinX, style.Plane, candidate.MinZ, u, v);
                AddVertex(maxX, style.Plane, candidate.MinZ, 0, v);
                AddVertex(maxX, style.Plane, maxZ, 0, 0);
            }

            void AddVertex(short x, short y, short z, ushort vertexU, ushort vertexV)
            {
                mergedVertices.Add(new ChunkVertex
                {
                    X = x,
                    Y = y,
                    Z = z,
                    Color = style.Color,
                    U = vertexU,
                    V = vertexV,
                    ArrayLayer = style.ArrayLayer,
                    TextureMipLevel = style.TextureMipLevel
                });
                mergedLights.Add(style.Light);
            }
        }
    }

    private static bool TryCandidate(
        ReadOnlySpan<ChunkVertex> vertices,
        ReadOnlySpan<ChunkLightVertex> lights,
        out Candidate candidate)
    {
        ref readonly var a = ref vertices[0];
        ref readonly var b = ref vertices[1];
        ref readonly var c = ref vertices[2];
        ref readonly var d = ref vertices[3];
        if (a.Y != b.Y || a.Y != c.Y || a.Y != d.Y ||
            a.Color != b.Color || a.Color != c.Color || a.Color != d.Color ||
            a.ArrayLayer != b.ArrayLayer || a.ArrayLayer != c.ArrayLayer ||
            a.ArrayLayer != d.ArrayLayer ||
            a.TextureMipLevel != b.TextureMipLevel ||
            a.TextureMipLevel != c.TextureMipLevel ||
            a.TextureMipLevel != d.TextureMipLevel ||
            lights[0] != lights[1] || lights[0] != lights[2] || lights[0] != lights[3] ||
            a.U == 0 || b.V == 0 ||
            a.U != b.U || b.V != c.V || c.U != 0 || d.U != 0 ||
            a.V != 0 || c.V != b.V || d.V != 0)
        {
            candidate = default;
            return false;
        }

        var minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        var maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        var minZ = Math.Min(Math.Min(a.Z, b.Z), Math.Min(c.Z, d.Z));
        var maxZ = Math.Max(Math.Max(a.Z, b.Z), Math.Max(c.Z, d.Z));
        var width = maxX - minX;
        var depth = maxZ - minZ;
        if (width <= 0 || depth <= 0)
        {
            candidate = default;
            return false;
        }

        var abX = b.X - a.X;
        var abZ = b.Z - a.Z;
        var acX = c.X - a.X;
        var acZ = c.Z - a.Z;
        var normalY = (long)abZ * acX - (long)abX * acZ;
        if (normalY == 0)
        {
            candidate = default;
            return false;
        }

        candidate = new Candidate(
            new Style(
                a.Y,
                normalY > 0,
                checked((short)width),
                checked((short)depth),
                a.Color,
                a.ArrayLayer,
                a.TextureMipLevel,
                a.U,
                b.V,
                lights[0]),
            minX,
            minZ);
        return true;
    }

    private readonly record struct Candidate(Style Style, short MinX, short MinZ);

    private readonly record struct Style(
        short Plane,
        bool FacingUp,
        short Width,
        short Depth,
        int Color,
        byte ArrayLayer,
        byte TextureMipLevel,
        ushort TileU,
        ushort TileV,
        ChunkLightVertex Light);

    private readonly record struct CellKey(Style Style, short X, short Z);

    internal readonly record struct MergedQuads(
        ChunkVertex[] Vertices,
        ChunkLightVertex[] Lights);
}
