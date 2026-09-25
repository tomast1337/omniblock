namespace OmniBlock.Worlds.Lod;

/// <summary>
/// Builds one spatial tile from saved chunks without installing its leaves in the live hierarchy.
/// Morton source order makes each group of four siblings contiguous; only three unfinished
/// siblings per level need to remain resident. Leaf and parent reduction are identical to the
/// ordinary hierarchy, so this is a memory policy, not a different visual sampling policy.
/// </summary>
public sealed class TerrainLodTransientTileBuilder
{
    private readonly TerrainLodTileKey _key;
    private readonly TerrainLodSpatialPolicy _policy;
    private readonly TerrainLodMaterialCatalog _materials;
    private readonly List<TerrainLodColumnTile>[] _frontier;
    private readonly int _totalSources;
    private int _nextSource;
    private TerrainLodColumnTile? _result;

    public TerrainLodTransientTileBuilder(
        TerrainLodTileKey key,
        TerrainLodSpatialPolicy policy,
        TerrainLodMaterialCatalog materials)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(materials);
        if (key.Level is < 1 or > 10 || key.Level > policy.MaximumSpatialLevel)
            throw new ArgumentOutOfRangeException(nameof(key));
        _key = key;
        _policy = policy;
        _materials = materials;
        _totalSources = checked(key.ChunkWidth * key.ChunkWidth);
        _frontier = Enumerable.Range(0, key.Level)
            .Select(static _ => new List<TerrainLodColumnTile>(4))
            .ToArray();
    }

    public bool IsComplete => _result is not null;
    public int SourcesAccepted => _nextSource;
    public int TotalSources => _totalSources;
    public int RetainedTiles => _frontier.Sum(static level => level.Count);
    public TerrainLodColumnTile? Result => _result;

    public (int X, int Z) NextChunkCoordinates()
    {
        if (IsComplete) throw new InvalidOperationException("The transient tile is complete.");
        var localX = 0;
        var localZ = 0;
        for (var bit = 0; bit < _key.Level; bit++)
        {
            localX |= ((_nextSource >> (bit * 2)) & 1) << bit;
            localZ |= ((_nextSource >> (bit * 2 + 1)) & 1) << bit;
        }
        return (checked((int)_key.MinChunkX + localX),
            checked((int)_key.MinChunkZ + localZ));
    }

    public void AddSource(TerrainLodSourceSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (expectedX, expectedZ) = NextChunkCoordinates();
        if (source.ChunkX != expectedX || source.ChunkZ != expectedZ)
            throw new ArgumentException(
                $"Expected saved terrain {expectedX},{expectedZ}, got " +
                $"{source.ChunkX},{source.ChunkZ}.", nameof(source));

        var tile = TerrainLodColumnTile.BuildLeaf(source, _materials);
        _nextSource++;
        for (var level = 0; level < _key.Level; level++)
        {
            var siblings = _frontier[level];
            siblings.Add(tile);
            if (siblings.Count < 4) return;
            var parent = siblings[0].Key.Parent();
            tile = TerrainLodColumnTile.BuildParent(
                parent, siblings, _policy.HorizontalSampleLevelForSpatialLevel(parent.Level));
            siblings.Clear();
        }
        if (_nextSource != _totalSources || tile.Key != _key)
            throw new InvalidOperationException("Transient terrain reduction completed out of order.");
        _result = tile;
    }
}
