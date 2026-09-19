namespace OmniBlock.Worlds.Lod;

/// <summary>The horizontal face of a selected spatial tile that owns a seam segment.</summary>
public enum TerrainLodSpatialBoundarySide : byte
{
    West,
    East,
    North,
    South
}

/// <summary>
///     One half-open chunk interval along a selected-tile boundary. Internal seams are owned by
///     exactly one tile (west before east, north before south); exterior seams have no neighbor.
/// </summary>
public readonly record struct TerrainLodSpatialSeamSegment(
    TerrainLodTileSelection Owner,
    TerrainLodTileSelection? Neighbor,
    TerrainLodSpatialBoundarySide OwnerSide,
    long FixedChunkCoordinate,
    long AlongStartChunk,
    long AlongEndChunk)
{
    public bool IsExterior => Neighbor is null;
    public long LengthChunks => AlongEndChunk - AlongStartChunk;
}

/// <summary>
///     Produces a deterministic, non-overlapping partition of every boundary in a selected
///     spatial tile set. A large tile touching several refined tiles is split at the refined
///     boundaries, so seam geometry can sample the two presentations that are actually drawn.
/// </summary>
public static class TerrainLodSpatialSeamPlanner
{
    public static IReadOnlyList<TerrainLodSpatialSeamSegment> Plan(
        IEnumerable<TerrainLodTileSelection> selections,
        bool includeExterior = true)
    {
        ArgumentNullException.ThrowIfNull(selections);
        var nodes = selections.ToArray();
        Validate(nodes);

        var edges = nodes
            .SelectMany(static node => Edges(node))
            .Select(static (edge, index) => edge with { Id = index })
            .ToArray();
        List<TerrainLodSpatialSeamSegment> seams = [];
        HashSet<(int Edge, long Start, long End)> covered = [];

        Match(
            edges.Where(static edge => edge.Side == TerrainLodSpatialBoundarySide.East),
            edges.Where(static edge => edge.Side == TerrainLodSpatialBoundarySide.West),
            vertical: true);
        Match(
            edges.Where(static edge => edge.Side == TerrainLodSpatialBoundarySide.South),
            edges.Where(static edge => edge.Side == TerrainLodSpatialBoundarySide.North),
            vertical: false);

        if (includeExterior)
        {
            foreach (var edge in edges)
            {
                var intervals = covered
                    .Where(value => value.Edge == edge.Id)
                    .Select(static value => (value.Start, value.End))
                    .OrderBy(static value => value.Start)
                    .ToArray();
                var cursor = edge.Start;
                foreach (var interval in intervals)
                {
                    if (cursor < interval.Start) AddExterior(cursor, interval.Start);
                    cursor = Math.Max(cursor, interval.End);
                }
                if (cursor < edge.End) AddExterior(cursor, edge.End);

                void AddExterior(long start, long end) => seams.Add(new(
                    edge.Selection, null, edge.Side, edge.Fixed, start, end));
            }
        }

        return seams
            .OrderBy(static seam => seam.OwnerSide is TerrainLodSpatialBoundarySide.West or
                TerrainLodSpatialBoundarySide.East ? 0 : 1)
            .ThenBy(static seam => seam.FixedChunkCoordinate)
            .ThenBy(static seam => seam.AlongStartChunk)
            .ThenBy(static seam => seam.AlongEndChunk)
            .ThenBy(static seam => seam.Owner.Tile.Level)
            .ThenBy(static seam => seam.Owner.Tile.X)
            .ThenBy(static seam => seam.Owner.Tile.Z)
            .ToArray();

        void Match(IEnumerable<Edge> first, IEnumerable<Edge> second, bool vertical)
        {
            var secondByLine = second
                .GroupBy(static edge => edge.Fixed)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.OrderBy(static edge => edge.Start).ToArray());
            foreach (var ownerLine in first.GroupBy(static edge => edge.Fixed))
            {
                if (!secondByLine.TryGetValue(ownerLine.Key, out var neighbors)) continue;
                var owners = ownerLine.OrderBy(static edge => edge.Start).ToArray();
                var ownerIndex = 0;
                var neighborIndex = 0;
                while (ownerIndex < owners.Length && neighborIndex < neighbors.Length)
                {
                    var owner = owners[ownerIndex];
                    var neighbor = neighbors[neighborIndex];
                    var start = Math.Max(owner.Start, neighbor.Start);
                    var end = Math.Min(owner.End, neighbor.End);
                    if (start < end)
                    {
                        covered.Add((owner.Id, start, end));
                        covered.Add((neighbor.Id, start, end));
                        seams.Add(new TerrainLodSpatialSeamSegment(
                            owner.Selection,
                            neighbor.Selection,
                            vertical
                                ? TerrainLodSpatialBoundarySide.East
                                : TerrainLodSpatialBoundarySide.South,
                            owner.Fixed,
                            start,
                            end));
                    }
                    if (owner.End <= neighbor.End) ownerIndex++;
                    if (neighbor.End <= owner.End) neighborIndex++;
                }
            }
        }
    }

    private static IEnumerable<Edge> Edges(TerrainLodTileSelection selection)
    {
        var tile = selection.Tile;
        var west = tile.MinChunkX;
        var east = tile.MaxChunkX + 1;
        var north = tile.MinChunkZ;
        var south = tile.MaxChunkZ + 1;
        yield return new Edge(0, selection, TerrainLodSpatialBoundarySide.West,
            west, north, south);
        yield return new Edge(0, selection, TerrainLodSpatialBoundarySide.East,
            east, north, south);
        yield return new Edge(0, selection, TerrainLodSpatialBoundarySide.North,
            north, west, east);
        yield return new Edge(0, selection, TerrainLodSpatialBoundarySide.South,
            south, west, east);
    }

    private static void Validate(TerrainLodTileSelection[] nodes)
    {
        foreach (var node in nodes)
        {
            if (node.HorizontalSampleLevel < 0)
                throw new ArgumentException("Spatial seam sample levels cannot be negative.",
                    nameof(nodes));
            if (node.MaximumVerticalSlices <= 0)
                throw new ArgumentException("Spatial seam slice budgets must be positive.",
                    nameof(nodes));
        }

        // Sweep the X axis so ordinary quadtree partitions validate in O(n log n) instead of
        // comparing every selected tile with every other tile each render frame.
        List<TerrainLodTileSelection> active = [];
        foreach (var node in nodes
                     .OrderBy(static value => value.Tile.MinChunkX)
                     .ThenBy(static value => value.Tile.MinChunkZ))
        {
            active.RemoveAll(other => other.Tile.MaxChunkX < node.Tile.MinChunkX);
            foreach (var other in active)
            {
                if (node.Tile.MinChunkX <= other.Tile.MaxChunkX &&
                    node.Tile.MaxChunkX >= other.Tile.MinChunkX &&
                    node.Tile.MinChunkZ <= other.Tile.MaxChunkZ &&
                    node.Tile.MaxChunkZ >= other.Tile.MinChunkZ)
                    throw new ArgumentException(
                        $"Spatial seam selections overlap: {node.Tile} and {other.Tile}.",
                        nameof(nodes));
            }
            active.Add(node);
        }
    }

    private readonly record struct Edge(
        int Id,
        TerrainLodTileSelection Selection,
        TerrainLodSpatialBoundarySide Side,
        long Fixed,
        long Start,
        long End);
}
