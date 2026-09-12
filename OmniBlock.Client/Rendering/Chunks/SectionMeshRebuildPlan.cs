using System.Numerics;

namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>
///     Immutable vertical mesh-page selection for one 16-cubed render section. Gameplay dirty
///     bounds already include the renderer's one-cell dependency halo; mapping that inclusive
///     range to four horizontal slabs avoids rebuilding unrelated geometry while keeping AO,
///     connected faces, liquids, and cross-section boundaries conservative.
/// </summary>
internal readonly record struct SectionMeshRebuildPlan(byte PageMask)
{
    public const int PageHeight = 4;
    public const int PageCount = SubChunkRenderer.Size / PageHeight;
    public const byte FullPageMask = (1 << PageCount) - 1;

    public static SectionMeshRebuildPlan Full => new(FullPageMask);
    public bool IsFull => PageMask == FullPageMask;
    public int PageBuildCount => BitOperations.PopCount(PageMask);
    public int BlockVisitCount => PageBuildCount * SubChunkRenderer.Size * SubChunkRenderer.Size * PageHeight;

    public bool Includes(int page) =>
        (uint)page < PageCount && (PageMask & (1 << page)) != 0;

    public SectionMeshRebuildPlan Union(SectionMeshRebuildPlan other) =>
        new((byte)(PageMask | other.PageMask));

    public static SectionMeshRebuildPlan FromWorldYBounds(
        int sectionY,
        int minY,
        int maxY,
        bool dependenciesKnown = true)
    {
        if (!dependenciesKnown || minY > maxY) return Full;

        var localMin = Math.Clamp(minY - sectionY, 0, SubChunkRenderer.Size - 1);
        var localMax = Math.Clamp(maxY - sectionY, 0, SubChunkRenderer.Size - 1);
        var first = localMin / PageHeight;
        var last = localMax / PageHeight;
        byte mask = 0;
        for (var page = first; page <= last; page++) mask |= (byte)(1 << page);
        return new SectionMeshRebuildPlan(mask);
    }
}
