using System.Runtime.CompilerServices;

namespace OmniBlock.Worlds.Lod;

/// <summary>
///     Creates disposable presentation columns that seal deep, unlit air while retaining a
///     bounded neighborhood around lit cave mouths. Canonical columns remain unchanged so
///     ceiling dimensions, underground cameras, and future quality modes can recover every cave.
/// </summary>
public static class TerrainLodCaveCuller
{
    // Only the first, block-scale spatial tier pays for cave-mouth connectivity. Four horizontal
    // samples keep the opening visible without retaining an entire linked underground cave.
    public const int CaveMouthReach = 4;
    private static readonly ConditionalWeakTable<TerrainLodColumnTile, TerrainLodCaveExposure>
        s_defaultExposure = new();

    public static TerrainLodCaveExposure GetDefaultExposure(
        TerrainLodColumnTile tile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tile);
        cancellationToken.ThrowIfCancellationRequested();
        // Immutable tiles may be compiled for both bodies and several seam segments. Cache by
        // tile identity; a weak key releases this derived presentation decision with its source.
        return s_defaultExposure.GetValue(tile,
            key => FindExposedAir(key, cancellationToken: cancellationToken));
    }

    public static TerrainLodCaveExposure FindExposedAir(
        TerrainLodColumnTile tile,
        int maximumHorizontalSteps = CaveMouthReach,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (maximumHorizontalSteps < 0 || maximumHorizontalSteps > byte.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(maximumHorizontalSteps));

        var width = tile.Width;
        var offsets = new int[checked(width * width + 1)];
        for (var x = 0; x < width; x++)
        for (var z = 0; z < width; z++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var columnIndex = x * width + z;
            offsets[columnIndex + 1] = checked(offsets[columnIndex] + tile[x, z].Spans.Count);
        }

        var distances = new byte[offsets[^1]];
        Array.Fill(distances, byte.MaxValue);
        var queue = new Queue<(int X, int Z, int Span, byte Distance)>();
        for (var x = 0; x < width; x++)
        for (var z = 0; z < width; z++)
        {
            var spans = tile[x, z].Spans;
            var start = offsets[x * width + z];
            for (var index = 0; index < spans.Count; index++)
            {
                var span = spans[index];
                if (!span.IsAir || (span.SkyLight == 0 && span.BlockLight == 0)) continue;
                distances[start + index] = 0;
                queue.Enqueue((x, z, index, 0));
            }
        }

        var visited = 0;
        while (queue.TryDequeue(out var current))
        {
            if ((++visited & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var columnIndex = current.X * width + current.Z;
            if (distances[offsets[columnIndex] + current.Span] != current.Distance) continue;
            var spans = tile[current.X, current.Z].Spans;
            var source = spans[current.Span];

            // Adjacent air intervals in one column can differ only in appearance/light. Passing
            // through their shared horizontal plane does not consume horizontal reach.
            Visit(current.X, current.Z, current.Span - 1, current.Distance, source);
            Visit(current.X, current.Z, current.Span + 1, current.Distance, source);
            if (current.Distance >= maximumHorizontalSteps) continue;
            var nextDistance = checked((byte)(current.Distance + 1));
            VisitNeighbor(current.X - 1, current.Z);
            VisitNeighbor(current.X + 1, current.Z);
            VisitNeighbor(current.X, current.Z - 1);
            VisitNeighbor(current.X, current.Z + 1);

            void VisitNeighbor(int x, int z)
            {
                if ((uint)x >= (uint)width || (uint)z >= (uint)width) return;
                var neighbor = tile[x, z].Spans;
                for (var index = 0; index < neighbor.Count; index++)
                {
                    var candidate = neighbor[index];
                    if (candidate.BottomY >= source.TopY) break;
                    if (candidate.TopY > source.BottomY)
                        Visit(x, z, index, nextDistance, source);
                }
            }

            void Visit(int x, int z, int spanIndex, byte distance, TerrainLodColumnSpan from)
            {
                var adjacent = tile[x, z].Spans;
                if ((uint)spanIndex >= (uint)adjacent.Count) return;
                var target = adjacent[spanIndex];
                if (!target.IsAir || target.TopY < from.BottomY ||
                    target.BottomY > from.TopY) return;
                var key = offsets[x * width + z] + spanIndex;
                if (distances[key] <= distance) return;
                distances[key] = distance;
                queue.Enqueue((x, z, spanIndex, distance));
            }
        }

        var preserved = new bool[distances.Length];
        var count = 0;
        for (var index = 0; index < distances.Length; index++)
            if (distances[index] != byte.MaxValue)
            {
                preserved[index] = true;
                count++;
            }
        return new TerrainLodCaveExposure(width, offsets, preserved, count);
    }

    public static TerrainLodColumn SealUndergroundAir(
        TerrainLodColumn source,
        int ceilingY) => SealUndergroundAir(source, ceilingY, []);

    public static TerrainLodColumn SealUndergroundAir(
        TerrainLodColumn source,
        int ceilingY,
        ReadOnlySpan<bool> preserveAirSpans)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!preserveAirSpans.IsEmpty && preserveAirSpans.Length != source.Spans.Count)
            throw new ArgumentException("Cave exposure must match the source spans.",
                nameof(preserveAirSpans));
        if (ceilingY <= 0) return source;
        ceilingY = Math.Min(ceilingY, source.WorldHeight);

        var sourceSpans = source.Spans;
        List<TerrainLodColumnSpan> output = [];
        var changed = false;
        for (var index = 0; index < sourceSpans.Count; index++)
        {
            var span = sourceSpans[index];
            if (!span.IsAir || span.SkyLight != 0 || span.BlockLight != 0 ||
                (!preserveAirSpans.IsEmpty && preserveAirSpans[index]) ||
                span.TopY > ceilingY ||
                !TryFindFill(index, out var fill))
            {
                Append(output, span);
                continue;
            }

            changed = true;
            Append(output, new TerrainLodColumnSpan(
                span.BottomY,
                span.Height,
                fill.Material,
                fill.BlockLight,
                fill.SkyLight));
        }

        return changed ? TerrainLodColumn.Create(source.WorldHeight, output) : source;

        bool TryFindFill(int airIndex, out TerrainLodColumnSpan fill)
        {
            // Prefer the supporting material below a cave, then its ceiling. Searching past
            // translucent/non-volumetric layers avoids filling a cavern with water or foliage.
            for (var index = airIndex - 1; index >= 0; index--)
                if (sourceSpans[index].Material.OccludesFaces)
                {
                    fill = sourceSpans[index];
                    return true;
                }
            for (var index = airIndex + 1; index < sourceSpans.Count; index++)
                if (sourceSpans[index].Material.OccludesFaces)
                {
                    fill = sourceSpans[index];
                    return true;
                }
            fill = default;
            return false;
        }
    }

    private static void Append(
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
///     A disposable presentation decision indexed by canonical span, not a change to the saved
///     terrain tile. Bodies and seams can independently reconstruct the same bounded cave mouths.
/// </summary>
public sealed class TerrainLodCaveExposure(
    int width, int[] offsets, bool[] preserved, int preservedSpanCount)
{
    public int PreservedSpanCount { get; } = preservedSpanCount;

    public ReadOnlySpan<bool> ForColumn(int x, int z)
    {
        if ((uint)x >= (uint)width)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)z >= (uint)width)
            throw new ArgumentOutOfRangeException(nameof(z));
        var index = x * width + z;
        return preserved.AsSpan(offsets[index], offsets[index + 1] - offsets[index]);
    }
}
