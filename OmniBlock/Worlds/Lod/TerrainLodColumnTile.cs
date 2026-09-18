using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
namespace OmniBlock.Worlds.Lod;

/// <summary>One canonical vertical material/light interval in a distant-terrain column.</summary>
public readonly record struct TerrainLodColumnSpan
{
    public TerrainLodColumnSpan(
        int bottomY,
        int height,
        TerrainLodMaterial material,
        byte blockLight,
        byte skyLight)
    {
        if (bottomY < 0) throw new ArgumentOutOfRangeException(nameof(bottomY));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (blockLight > 15) throw new ArgumentOutOfRangeException(nameof(blockLight));
        if (skyLight > 15) throw new ArgumentOutOfRangeException(nameof(skyLight));
        BottomY = bottomY;
        Height = height;
        Material = material;
        BlockLight = blockLight;
        SkyLight = skyLight;
    }

    public int BottomY { get; }
    public int Height { get; }
    public int TopY => checked(BottomY + Height);
    public TerrainLodMaterial Material { get; }
    public byte BlockLight { get; }
    public byte SkyLight { get; }
    public bool IsAir => Material.IsAir;
}

/// <summary>
///     Immutable bottom-to-top partition of one X/Z sample. Air is explicit so cave openings,
///     overhangs, and their lighting survive canonical parent construction.
/// </summary>
public sealed class TerrainLodColumn
{
    private readonly TerrainLodColumnSpan[] _spans;

    private TerrainLodColumn(int worldHeight, TerrainLodColumnSpan[] spans)
    {
        WorldHeight = worldHeight;
        _spans = spans;
        Spans = Array.AsReadOnly(spans);
    }

    public int WorldHeight { get; }
    public ReadOnlyCollection<TerrainLodColumnSpan> Spans { get; }

    public static TerrainLodColumn Create(
        int worldHeight,
        IEnumerable<TerrainLodColumnSpan> spans)
    {
        if (worldHeight <= 0) throw new ArgumentOutOfRangeException(nameof(worldHeight));
        ArgumentNullException.ThrowIfNull(spans);
        var values = spans.ToArray();
        if (values.Length == 0)
            throw new ArgumentException("A terrain LOD column must cover the world height.",
                nameof(spans));
        var expectedBottom = 0;
        TerrainLodColumnSpan? previous = null;
        foreach (var span in values)
        {
            if (span.BottomY != expectedBottom)
                throw new ArgumentException(
                    $"Terrain LOD column has a gap or overlap at Y={expectedBottom}.",
                    nameof(spans));
            if (span.TopY > worldHeight)
                throw new ArgumentException(
                    $"Terrain LOD column span ends at {span.TopY}, above {worldHeight}.",
                    nameof(spans));
            if (previous is { } prior && SameAppearance(prior, span))
                throw new ArgumentException(
                    "Adjacent equivalent terrain LOD spans must be canonicalized.",
                    nameof(spans));
            expectedBottom = span.TopY;
            previous = span;
        }
        if (expectedBottom != worldHeight)
            throw new ArgumentException(
                $"Terrain LOD column ends at Y={expectedBottom}; expected {worldHeight}.",
                nameof(spans));
        return new TerrainLodColumn(worldHeight, values);
    }

    public TerrainLodColumnSpan At(int y)
    {
        if ((uint)y >= (uint)WorldHeight) throw new ArgumentOutOfRangeException(nameof(y));
        // Canonical columns normally contain only a handful of spans. Binary search keeps the
        // pathological modded/cave case bounded without adding an interval index to every column.
        var low = 0;
        var high = _spans.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var span = _spans[middle];
            if (y < span.BottomY) high = middle - 1;
            else if (y >= span.TopY) low = middle + 1;
            else return span;
        }
        throw new InvalidOperationException($"Canonical terrain LOD column does not cover Y={y}.");
    }

    internal static bool SameAppearance(
        in TerrainLodColumnSpan first,
        in TerrainLodColumnSpan second) =>
        first.Material == second.Material &&
        first.BlockLight == second.BlockLight &&
        first.SkyLight == second.SkyLight;
}

/// <summary>Deterministic horizontal reduction for four equally sized column samples.</summary>
public static class TerrainLodColumnReducer
{
    public static TerrainLodColumn MergeFour(
        TerrainLodColumn northWest,
        TerrainLodColumn northEast,
        TerrainLodColumn southWest,
        TerrainLodColumn southEast)
    {
        ArgumentNullException.ThrowIfNull(northWest);
        ArgumentNullException.ThrowIfNull(northEast);
        ArgumentNullException.ThrowIfNull(southWest);
        ArgumentNullException.ThrowIfNull(southEast);
        TerrainLodColumn[] inputs = [northWest, northEast, southWest, southEast];
        var worldHeight = northWest.WorldHeight;
        if (inputs.Any(column => column.WorldHeight != worldHeight))
            throw new ArgumentException("Terrain LOD columns must share a world height.");

        SortedSet<int> transitions = [0, worldHeight];
        foreach (var column in inputs)
        foreach (var span in column.Spans)
        {
            transitions.Add(span.BottomY);
            transitions.Add(span.TopY);
        }

        var levels = transitions.ToArray();
        List<TerrainLodColumnSpan> output = [];
        for (var transition = 0; transition < levels.Length - 1; transition++)
        {
            var bottom = levels[transition];
            var top = levels[transition + 1];
            TerrainLodColumnSpan[] samples =
            [
                northWest.At(bottom), northEast.At(bottom),
                southWest.At(bottom), southEast.At(bottom)
            ];
            var material = SelectMaterial(samples);
            var supporters = samples.Where(sample => sample.Material == material).ToArray();
            // Air is ignored while selecting a visible material, matching DH's conservative rule
            // that prevents tiny cave samples from punching holes through distant solid terrain.
            // If every sample is air, all four support the air interval and preserve a broad cave.
            if (supporters.Length == 0) supporters = samples;
            var blockLight = checked((byte)(supporters.Sum(static sample => sample.BlockLight) /
                                                supporters.Length));
            var skyLight = checked((byte)(supporters.Sum(static sample => sample.SkyLight) /
                                              supporters.Length));
            AppendCanonical(output, new TerrainLodColumnSpan(
                bottom, top - bottom, material, blockLight, skyLight));
        }

        return TerrainLodColumn.Create(worldHeight, output);
    }

    private static TerrainLodMaterial SelectMaterial(ReadOnlySpan<TerrainLodColumnSpan> samples)
    {
        Dictionary<TerrainLodMaterial, int> counts = [];
        var hasVisible = false;
        foreach (ref readonly var sample in samples)
        {
            if (!sample.IsAir) hasVisible = true;
        }
        foreach (ref readonly var sample in samples)
        {
            if (hasVisible && sample.IsAir) continue;
            counts[sample.Material] = counts.GetValueOrDefault(sample.Material) + 1;
        }
        return counts
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key.BlockId)
            .ThenBy(static pair => pair.Key.Metadata)
            .ThenBy(static pair => pair.Key.Geometry)
            .First().Key;
    }

    private static void AppendCanonical(
        List<TerrainLodColumnSpan> output,
        TerrainLodColumnSpan span)
    {
        if (output.Count > 0 &&
            output[^1].TopY == span.BottomY &&
            TerrainLodColumn.SameAppearance(output[^1], span))
        {
            var previous = output[^1];
            output[^1] = new TerrainLodColumnSpan(
                previous.BottomY,
                checked(previous.Height + span.Height),
                previous.Material,
                previous.BlockLight,
                previous.SkyLight);
            return;
        }
        output.Add(span);
    }
}

/// <summary>
///     Produces a presentation/cache tier with a bounded number of vertical material/air spans.
///     Canonical columns are never mutated. Small, visually compatible intervals collapse first;
///     air/solid boundaries and the top surface are deliberately the last choices.
/// </summary>
public static class TerrainLodVerticalSliceReducer
{
    public static TerrainLodColumn Reduce(
        TerrainLodColumn source,
        int maximumSlices)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maximumSlices <= 0) throw new ArgumentOutOfRangeException(nameof(maximumSlices));
        if (source.Spans.Count <= maximumSlices) return source;

        List<TerrainLodColumnSpan> spans = [.. source.Spans];
        while (spans.Count > maximumSlices)
        {
            var bestIndex = 0;
            var bestCost = Cost(spans, 0);
            for (var index = 1; index < spans.Count - 1; index++)
            {
                var cost = Cost(spans, index);
                // Prefer the higher pair on exact ties, matching DH's surface-biased tie break.
                if (cost.CompareTo(bestCost) <= 0)
                {
                    bestIndex = index;
                    bestCost = cost;
                }
            }

            spans[bestIndex] = Merge(spans[bestIndex], spans[bestIndex + 1]);
            spans.RemoveAt(bestIndex + 1);
            CanonicalizeAround(spans, bestIndex);
        }
        return TerrainLodColumn.Create(source.WorldHeight, spans);
    }

    private static ReductionCost Cost(List<TerrainLodColumnSpan> spans, int lowerIndex)
    {
        var lower = spans[lowerIndex];
        var upper = spans[lowerIndex + 1];
        var compatibility = lower.Material == upper.Material
            ? 0
            : lower.IsAir == upper.IsAir && lower.Material.Geometry == upper.Material.Geometry
                ? 1
                : lower.IsAir == upper.IsAir
                    ? 2
                    : 4;
        // Losing the highest distinct surface is much more visible than simplifying an internal
        // layer. Equal-material light bands remain cheap even at the top.
        var topSurfacePenalty = lowerIndex + 1 == spans.Count - 1 &&
                                lower.Material != upper.Material
            ? 2
            : 0;
        return new ReductionCost(
            compatibility + topSurfacePenalty,
            checked(lower.Height + upper.Height));
    }

    private static TerrainLodColumnSpan Merge(
        in TerrainLodColumnSpan lower,
        in TerrainLodColumnSpan upper)
    {
        TerrainLodMaterial selected;
        if (lower.Material == upper.Material) selected = lower.Material;
        else if (lower.IsAir != upper.IsAir) selected = lower.IsAir ? upper.Material : lower.Material;
        else if (lower.Material.OccludesFaces != upper.Material.OccludesFaces)
            selected = lower.Material.OccludesFaces ? lower.Material : upper.Material;
        else if (lower.Height != upper.Height)
            selected = lower.Height > upper.Height ? lower.Material : upper.Material;
        else
            selected = upper.Material;

        var selectedHeight = 0;
        var blockLight = 0;
        var skyLight = 0;
        Accumulate(lower);
        Accumulate(upper);
        if (selectedHeight == 0)
        {
            selectedHeight = checked(lower.Height + upper.Height);
            blockLight = lower.BlockLight * lower.Height + upper.BlockLight * upper.Height;
            skyLight = lower.SkyLight * lower.Height + upper.SkyLight * upper.Height;
        }
        return new TerrainLodColumnSpan(
            lower.BottomY,
            checked(lower.Height + upper.Height),
            selected,
            checked((byte)(blockLight / selectedHeight)),
            checked((byte)(skyLight / selectedHeight)));

        void Accumulate(in TerrainLodColumnSpan span)
        {
            if (span.Material != selected) return;
            selectedHeight += span.Height;
            blockLight += span.BlockLight * span.Height;
            skyLight += span.SkyLight * span.Height;
        }
    }

    private static void CanonicalizeAround(
        List<TerrainLodColumnSpan> spans,
        int index)
    {
        if (index > 0 && TerrainLodColumn.SameAppearance(spans[index - 1], spans[index]))
        {
            spans[index - 1] = CombineEquivalent(spans[index - 1], spans[index]);
            spans.RemoveAt(index);
            index--;
        }
        if (index + 1 < spans.Count &&
            TerrainLodColumn.SameAppearance(spans[index], spans[index + 1]))
        {
            spans[index] = CombineEquivalent(spans[index], spans[index + 1]);
            spans.RemoveAt(index + 1);
        }
    }

    private static TerrainLodColumnSpan CombineEquivalent(
        in TerrainLodColumnSpan lower,
        in TerrainLodColumnSpan upper) => new(
        lower.BottomY,
        checked(lower.Height + upper.Height),
        lower.Material,
        lower.BlockLight,
        lower.SkyLight);

    private readonly record struct ReductionCost(int Compatibility, int CombinedHeight)
        : IComparable<ReductionCost>
    {
        public int CompareTo(ReductionCost other)
        {
            var compatibility = Compatibility.CompareTo(other.Compatibility);
            return compatibility != 0
                ? compatibility
                : CombinedHeight.CompareTo(other.CombinedHeight);
        }
    }
}

/// <summary>
///     Immutable canonical columns for one spatial tile. GPU meshes and vertical slice reduction
///     are presentation artifacts and are deliberately excluded.
/// </summary>
public sealed class TerrainLodColumnTile
{
    internal const int SchemaVersion = 1;
    private const int MaximumSamplesPerSide = 256;
    private readonly TerrainLodColumn[] _columns;

    private TerrainLodColumnTile(
        TerrainLodTileKey key,
        int horizontalSampleLevel,
        int width,
        int worldHeight,
        TerrainLodColumn[] columns,
        long? leafTerrainRevision,
        string[] inputHashes)
    {
        Key = key;
        HorizontalSampleLevel = horizontalSampleLevel;
        Width = width;
        WorldHeight = worldHeight;
        _columns = columns;
        LeafTerrainRevision = leafTerrainRevision;
        InputHashes = Array.AsReadOnly(inputHashes);
        CanonicalHash = ComputeCanonicalHash();
    }

    public TerrainLodTileKey Key { get; }
    public int HorizontalSampleLevel { get; }
    public int Width { get; }
    public int WorldHeight { get; }
    public long? LeafTerrainRevision { get; }
    public ReadOnlyCollection<string> InputHashes { get; }
    public string CanonicalHash { get; }
    public TerrainLodColumn this[int x, int z] => _columns[Index(x, z)];

    public static TerrainLodColumnTile BuildLeaf(
        TerrainLodSourceSnapshot source,
        TerrainLodMaterialCatalog materials)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(materials);
        if (source.Width != 16 || source.Depth != 16)
            throw new ArgumentException(
                "A terrain LOD leaf tile must describe one 16x16 chunk.", nameof(source));
        var columns = new TerrainLodColumn[source.Width * source.Depth];
        for (var x = 0; x < source.Width; x++)
        for (var z = 0; z < source.Depth; z++)
        {
            List<TerrainLodColumnSpan> spans = [];
            var bottom = 0;
            var material = materials.Resolve(source.GetBlock(x, 0, z), source.GetMetadata(x, 0, z));
            var light = GetLight(source, x, 0, z);
            for (var y = 1; y < source.Height; y++)
            {
                var nextMaterial = materials.Resolve(
                    source.GetBlock(x, y, z), source.GetMetadata(x, y, z));
                var nextLight = GetLight(source, x, y, z);
                if (nextMaterial == material && nextLight == light) continue;
                spans.Add(new TerrainLodColumnSpan(
                    bottom, y - bottom, material, light.Block, light.Sky));
                bottom = y;
                material = nextMaterial;
                light = nextLight;
            }
            spans.Add(new TerrainLodColumnSpan(
                bottom, source.Height - bottom, material, light.Block, light.Sky));
            columns[x * source.Depth + z] = TerrainLodColumn.Create(source.Height, spans);
        }

        return new TerrainLodColumnTile(
            new TerrainLodTileKey(0, source.ChunkX, source.ChunkZ),
            horizontalSampleLevel: 0,
            source.Width,
            source.Height,
            columns,
            source.TerrainRevision,
            [$"{source.TerrainRevision}:{source.SourceFingerprint}"]);
    }

    public static TerrainLodColumnTile BuildParent(
        TerrainLodTileKey key,
        IReadOnlyList<TerrainLodColumnTile> children,
        int horizontalSampleLevel)
    {
        if (key.Level == 0)
            throw new ArgumentException("A parent terrain LOD tile must be above level zero.",
                nameof(key));
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count != 4)
            throw new ArgumentException("A parent terrain LOD tile requires four children.",
                nameof(children));
        if (horizontalSampleLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(horizontalSampleLevel));
        var childSampleLevel = children[0].HorizontalSampleLevel;
        var childWidth = children[0].Width;
        var worldHeight = children[0].WorldHeight;
        for (var index = 0; index < 4; index++)
        {
            var child = children[index] ?? throw new ArgumentException(
                $"Terrain LOD child {index} is null.", nameof(children));
            if (child.Key != key.Child(index))
                throw new ArgumentException(
                    $"Terrain LOD child {index} is {child.Key}; expected {key.Child(index)}.",
                    nameof(children));
            if (child.HorizontalSampleLevel != childSampleLevel ||
                child.Width != childWidth || child.WorldHeight != worldHeight)
                throw new ArgumentException(
                    "Terrain LOD siblings must share sample level, dimensions, and world height.",
                    nameof(children));
        }
        if (horizontalSampleLevel < childSampleLevel ||
            horizontalSampleLevel > childSampleLevel + 1)
            throw new ArgumentException(
                "Incremental parent construction may retain horizontal detail or reduce it by one octave.",
                nameof(horizontalSampleLevel));

        var reduce = horizontalSampleLevel == childSampleLevel + 1;
        var mosaicWidth = checked(childWidth * 2);
        var outputWidth = reduce ? mosaicWidth / 2 : mosaicWidth;
        if (outputWidth <= 0 || outputWidth > MaximumSamplesPerSide)
            throw new InvalidOperationException(
                $"Terrain LOD parent would contain {outputWidth} samples per side; " +
                $"the supported maximum is {MaximumSamplesPerSide}.");
        var columns = new TerrainLodColumn[checked(outputWidth * outputWidth)];
        for (var x = 0; x < outputWidth; x++)
        for (var z = 0; z < outputWidth; z++)
        {
            columns[x * outputWidth + z] = reduce
                ? TerrainLodColumnReducer.MergeFour(
                    Mosaic(x * 2, z * 2),
                    Mosaic(x * 2 + 1, z * 2),
                    Mosaic(x * 2, z * 2 + 1),
                    Mosaic(x * 2 + 1, z * 2 + 1))
                : Mosaic(x, z);
        }

        return new TerrainLodColumnTile(
            key,
            horizontalSampleLevel,
            outputWidth,
            worldHeight,
            columns,
            leafTerrainRevision: null,
            children.Select(static child => child.CanonicalHash).ToArray());

        TerrainLodColumn Mosaic(int x, int z)
        {
            var east = x >= childWidth ? 1 : 0;
            var south = z >= childWidth ? 1 : 0;
            var child = children[east | (south << 1)];
            return child[x - east * childWidth, z - south * childWidth];
        }
    }

    internal static TerrainLodColumnTile FromSerialized(
        TerrainLodTileKey key,
        int horizontalSampleLevel,
        int width,
        int worldHeight,
        TerrainLodColumn[] columns,
        long? leafTerrainRevision,
        string[] inputHashes,
        string expectedCanonicalHash)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(inputHashes);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCanonicalHash);
        if (horizontalSampleLevel < 0 || horizontalSampleLevel > key.Level + 4)
            throw new InvalidDataException(
                $"Terrain LOD tile {key} has invalid horizontal sample level " +
                $"{horizontalSampleLevel}.");
        var footprintBlocks = checked(16L * key.ChunkWidth);
        var sampleSize = 1L << horizontalSampleLevel;
        if (footprintBlocks % sampleSize != 0 ||
            footprintBlocks / sampleSize is <= 0 or > MaximumSamplesPerSide ||
            width != footprintBlocks / sampleSize)
            throw new InvalidDataException(
                $"Terrain LOD tile {key} has width {width} at sample level " +
                $"{horizontalSampleLevel}; expected {footprintBlocks / sampleSize}.");
        if (worldHeight <= 0)
            throw new InvalidDataException("Terrain LOD tile world height must be positive.");
        if (columns.Length != checked(width * width))
            throw new InvalidDataException(
                $"Terrain LOD tile declares {columns.Length} columns; expected {width * width}.");
        if (columns.Any(column => column is null || column.WorldHeight != worldHeight))
            throw new InvalidDataException(
                "Terrain LOD tile columns must be non-null and share its world height.");
        var expectedInputs = key.Level == 0 ? 1 : 4;
        if (inputHashes.Length != expectedInputs ||
            inputHashes.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException(
                $"Terrain LOD tile {key} has {inputHashes.Length} inputs; expected " +
                $"{expectedInputs} non-empty identities.");
        if ((key.Level == 0) != leafTerrainRevision.HasValue)
            throw new InvalidDataException(
                "Only level-zero terrain LOD tiles may carry a leaf terrain revision.");

        var tile = new TerrainLodColumnTile(
            key,
            horizontalSampleLevel,
            width,
            worldHeight,
            columns.ToArray(),
            leafTerrainRevision,
            inputHashes.ToArray());
        if (!string.Equals(tile.CanonicalHash, expectedCanonicalHash,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Terrain LOD tile hash {tile.CanonicalHash} does not match " +
                $"{expectedCanonicalHash}.");
        return tile;
    }

    private int Index(int x, int z)
    {
        if ((uint)x >= (uint)Width || (uint)z >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(x),
                $"Terrain LOD column {x},{z} is outside 0..{Width - 1}.");
        return x * Width + z;
    }

    private string ComputeCanonicalHash()
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(SchemaVersion);
            writer.Write(Key.Level);
            writer.Write(Key.X);
            writer.Write(Key.Z);
            writer.Write(HorizontalSampleLevel);
            writer.Write(Width);
            writer.Write(WorldHeight);
            writer.Write(LeafTerrainRevision ?? -1);
            writer.Write(InputHashes.Count);
            foreach (var hash in InputHashes) writer.Write(hash);
            foreach (var column in _columns)
            {
                writer.Write(column.Spans.Count);
                foreach (var span in column.Spans)
                {
                    writer.Write(span.BottomY);
                    writer.Write(span.Height);
                    writer.Write(span.Material.BlockId.ToString());
                    writer.Write(span.Material.Metadata);
                    writer.Write((byte)span.Material.Geometry);
                    writer.Write(span.Material.OccludesFaces);
                    writer.Write(span.Material.MapColor);
                    writer.Write(span.BlockLight);
                    writer.Write(span.SkyLight);
                }
            }
        }
        return Convert.ToHexStringLower(
            SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static (byte Block, byte Sky) GetLight(
        TerrainLodSourceSnapshot source,
        int x,
        int y,
        int z)
    {
        if (source.Lighting is null) return default;
        var levels = source.Lighting.GetLightLevels(
            source.ChunkX * 16 + x,
            y,
            source.ChunkZ * 16 + z,
            0);
        return (levels.Block, levels.Sky);
    }
}
