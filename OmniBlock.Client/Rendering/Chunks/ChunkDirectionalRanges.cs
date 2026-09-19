using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Chunks.Occlusion;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks;

internal readonly record struct ChunkQuadRange(int FirstQuad, int QuadCount)
{
    public bool IsEmpty => QuadCount == 0;
}

/// <summary>
///     Seven immutable ranges in the packed page mesh: six known outward faces followed by geometry
///     whose culling semantics are unknown. Unknown geometry is always selected.
/// </summary>
internal readonly record struct ChunkDirectionalRanges(
    ChunkQuadRange Down,
    ChunkQuadRange Up,
    ChunkQuadRange North,
    ChunkQuadRange South,
    ChunkQuadRange West,
    ChunkQuadRange East,
    ChunkQuadRange Unassigned)
{
    public static ChunkDirectionalRanges Empty => default;

    public int AvailableQuadCount =>
        Down.QuadCount + Up.QuadCount + North.QuadCount + South.QuadCount +
        West.QuadCount + East.QuadCount + Unassigned.QuadCount;

    public int UnassignedQuadCount => Unassigned.QuadCount;

    public ChunkQuadRange Get(Side side) => side switch
    {
        Side.Down => Down,
        Side.Up => Up,
        Side.North => North,
        Side.South => South,
        Side.West => West,
        Side.East => East,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };

    /// <summary>
    ///     Writes selected, non-empty ranges and joins physically adjacent buckets. The output span
    ///     needs room for at least seven entries.
    /// </summary>
    public int Select(ChunkDirectionMask mask, Span<ChunkQuadRange> output)
    {
        if (output.Length < 7) throw new ArgumentException("Directional selection needs seven range slots.", nameof(output));

        var count = 0;
        for (var direction = 0; direction < ChunkDirectionExtensions.Count; direction++)
        {
            if ((mask & (ChunkDirectionMask)(1 << direction)) == 0) continue;
            Append(output, ref count, Get((Side)direction));
        }

        Append(output, ref count, Unassigned);
        return count;
    }

    private static void Append(Span<ChunkQuadRange> output, ref int count, ChunkQuadRange next)
    {
        if (next.IsEmpty) return;
        if (count > 0)
        {
            var previous = output[count - 1];
            if (previous.FirstQuad + previous.QuadCount == next.FirstQuad)
            {
                output[count - 1] = previous with { QuadCount = previous.QuadCount + next.QuadCount };
                return;
            }
        }

        output[count++] = next;
    }
}

internal static class DirectionalFaceVisibility
{
    /// <summary>
    ///     Selects only the outward direction on an axis when the camera is outside the page bounds.
    ///     A camera within an axis slab keeps both directions, making the test conservative for
    ///     caves, partial blocks, and section crossings.
    /// </summary>
    public static ChunkDirectionMask ForPage(
        Vector3D<int> sectionPosition,
        int page,
        Vector3D<double> viewPosition)
    {
        var mask = ChunkDirectionMask.None;
        AddAxis(viewPosition.X, sectionPosition.X, sectionPosition.X + SubChunkRenderer.Size,
            ChunkDirectionMask.West, ChunkDirectionMask.East, ref mask);

        var minY = sectionPosition.Y + page * SectionMeshRebuildPlan.PageHeight;
        AddAxis(viewPosition.Y, minY, minY + SectionMeshRebuildPlan.PageHeight,
            ChunkDirectionMask.Down, ChunkDirectionMask.Up, ref mask);

        AddAxis(viewPosition.Z, sectionPosition.Z, sectionPosition.Z + SubChunkRenderer.Size,
            ChunkDirectionMask.North, ChunkDirectionMask.South, ref mask);
        return mask;
    }

    /// <summary>Conservative directional selection for an arbitrary axis-aligned mesh page.</summary>
    public static ChunkDirectionMask ForBounds(
        Vector3D<int> minimum,
        int size,
        Vector3D<double> viewPosition)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        var mask = ChunkDirectionMask.None;
        AddAxis(viewPosition.X, minimum.X, checked(minimum.X + size),
            ChunkDirectionMask.West, ChunkDirectionMask.East, ref mask);
        AddAxis(viewPosition.Y, minimum.Y, checked(minimum.Y + size),
            ChunkDirectionMask.Down, ChunkDirectionMask.Up, ref mask);
        AddAxis(viewPosition.Z, minimum.Z, checked(minimum.Z + size),
            ChunkDirectionMask.North, ChunkDirectionMask.South, ref mask);
        return mask;
    }

    private static void AddAxis(
        double camera,
        double minimum,
        double maximum,
        ChunkDirectionMask negative,
        ChunkDirectionMask positive,
        ref ChunkDirectionMask mask)
    {
        if (camera < minimum) mask |= negative;
        else if (camera > maximum) mask |= positive;
        else mask |= negative | positive;
    }
}
