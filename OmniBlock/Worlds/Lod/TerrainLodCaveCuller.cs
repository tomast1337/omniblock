namespace OmniBlock.Worlds.Lod;

/// <summary>
///     Creates a disposable presentation column in which fully underground, unlit air intervals
///     are sealed by neighboring terrain. Canonical columns remain unchanged so ceiling
///     dimensions, underground cameras, and future quality modes can still recover every cave.
/// </summary>
public static class TerrainLodCaveCuller
{
    public static TerrainLodColumn SealUndergroundAir(
        TerrainLodColumn source,
        int ceilingY)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ceilingY <= 0) return source;
        ceilingY = Math.Min(ceilingY, source.WorldHeight);

        var sourceSpans = source.Spans;
        List<TerrainLodColumnSpan> output = [];
        var changed = false;
        for (var index = 0; index < sourceSpans.Count; index++)
        {
            var span = sourceSpans[index];
            if (!span.IsAir || span.SkyLight != 0 || span.TopY > ceilingY ||
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
