using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal static class TerrainLodSpatialResidencyPolicy
{
    /// <summary>
    ///     Optional leading-edge cache entries may use only the space below the soft residency
    ///     target. Active snapshots and their replacement already consume the reserved bytes;
    ///     pinning a fixed extra number under pressure can prevent either snapshot from finishing.
    ///     Candidates arrive in deterministic nearest-first order.
    /// </summary>
    public static IEnumerable<TerrainLodTileKey> SelectLeadingEdge(
        IEnumerable<(TerrainLodTileKey Key, long Bytes)> candidates,
        long reservedBytes)
    {
        var available = Math.Max(0, TerrainLodScaleBudget.TargetSpatialGpuBytes - reservedBytes);
        if (available == 0) yield break;
        var count = 0;
        foreach (var (key, bytes) in candidates)
        {
            if (bytes > available) continue;
            yield return key;
            available -= bytes;
            if (++count >= TerrainLodScaleBudget.MaximumLeadingEdgePresentations) yield break;
        }
    }
}
