using OmniBlock.Worlds.Lod;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

// These scalars travel with the uploaded mesh, not the mutable/current CPU hierarchy. A retained
// predecessor must describe its own geometry even while a different source revision is building.
internal readonly record struct TerrainLodSpatialMeshQuality(
    int HorizontalSampleBlocks,
    int VerticalSliceBudget,
    int MaximumRenderedSpans,
    int SourceColumns,
    int SourceSpansAfterCaveCulling,
    int SolidQuads,
    int TranslucentQuads)
{
    public static TerrainLodSpatialMeshQuality FromMesh(TerrainLodSpatialMeshData mesh) => new(
        1 << mesh.HorizontalSampleLevel, mesh.VerticalSliceBudget, mesh.MaximumRenderedSpans,
        mesh.Profile.SourceColumns, mesh.Profile.SourceSpans, mesh.SolidQuadCount,
        mesh.TranslucentQuadCount);
}

internal static class TerrainLodQualityDiagnostics
{
    // An observation, not a guessed root cause: child readiness and source hashes accompany it.
    public static string SelectionRelation(int selected, int desired, int minimum) =>
        selected > Math.Max(desired, minimum) ? "coarser-than-distance-target" :
        selected == minimum && desired < minimum ? "minimum-spatial-level" :
        selected < desired ? "finer-than-distance-target" : "distance-target";
}

internal sealed partial class ClientTerrainLodRenderer
{
    private Vector3D<double> _qualityCamera;
    private int _qualityNearDistance;
    private int _qualityHorizonDistance;
    private double _qualityFov;
    private int _qualityHeight;
    private double _qualityDropoff;

    /// <summary>
    /// Render-thread, on-demand diagnostic; no GPU readback or per-frame list construction.
    /// Spatial rows are published ownership candidates, NOT a claim every tile/pixel was drawn.
    /// Local rows are selected layers, not every resident cache entry. Exact geometry is separate.
    /// </summary>
    public object CaptureQualitySnapshot()
    {
        var spatial = _spatialFrame?.Draws
            .OrderBy(draw => draw.Selection.Tile.Level)
            .ThenBy(draw => draw.Selection.Tile.X)
            .ThenBy(draw => draw.Selection.Tile.Z)
            .Select(draw =>
            {
                var key = draw.Selection.Tile;
                var distance = key.DistanceTo(_qualityCamera.X / 16, _qualityCamera.Z / 16);
                var desired = _spatialPolicy.DesiredSpatialLevel(distance);
                var available = _spatialHierarchy.TryGetCoverage(key, out var source, out var current);
                var childrenAvailable = 0;
                var childrenUploaded = 0;
                if (key.Level > 0)
                    for (var i = 0; i < 4; i++)
                    {
                        if (_spatialHierarchy.TryGetCoverage(key.Child(i), out _, out var childCurrent)
                            && childCurrent) childrenAvailable++;
                        if (_spatialPresentations.IsReady(key.Child(i))) childrenUploaded++;
                    }
                return new
                {
                    Tile = key,
                    DistanceToBoundsChunks = distance,
                    DesiredSpatialLevel = desired,
                    Relation = TerrainLodQualityDiagnostics.SelectionRelation(
                        key.Level, desired, MinimumSpatialGpuLevel),
                    Authoritative = _authoritativeSpatialTiles.Contains(key),
                    CpuSourceAvailable = available,
                    CpuSourceCurrent = current,
                    CpuSourceMatchesPublishedMesh = available &&
                        source?.CanonicalHash == draw.Presentation.CanonicalHash,
                    ImmediateCurrentChildSources = childrenAvailable,
                    ImmediateUploadedChildren = childrenUploaded,
                    PublishedHash = draw.Presentation.CanonicalHash,
                    Mesh = draw.Presentation.Quality,
                    draw.Fade
                };
            }).ToArray();
        // End-of-frame eviction can retire a selected column before this on-demand dump.
        var local = _selectedSolidLevels.Where(pair => _resident.ContainsKey(pair.Key))
            .Select(pair => LocalRow(pair.Key, pair.Value, "solid"))
            .Concat(_selectedTranslucentLevels.Where(pair => _resident.ContainsKey(pair.Key))
                .Select(pair => LocalRow(pair.Key, pair.Value, "translucent")))
            .ToArray();
        return new
        {
            Schema = 1,
            Camera = new { _qualityCamera.X, _qualityCamera.Y, _qualityCamera.Z },
            ExactRadiusChunks = _qualityNearDistance,
            HorizonRadiusChunks = _qualityHorizonDistance,
            VerticalFov = _qualityFov,
            ViewportHeight = _qualityHeight,
            LocalDropoffScale = _qualityDropoff,
            Shading = "terrain-texture-array",
            MinimumSpatialLevel = MinimumSpatialGpuLevel,
            Spatial = spatial,
            Local = local
        };

        object LocalRow((int X, int Z) key, int level, string layer)
        {
            var presentation = _resident[key];
            var distance = Math.Sqrt(DistanceSquared(key, _qualityCamera));
            return new
            {
                ChunkX = key.X, ChunkZ = key.Z, Layer = layer,
                DistanceToCenterBlocks = distance,
                SelectedLevel = level,
                HasSelectedLayerGeometry = presentation.TryGetLevel(level, layer == "translucent", out _),
                LayerBodyDrawn = (layer == "translucent" ? _translucentSeamStates : _solidSeamStates)
                    .TryGetValue(key, out var seamState) && seamState.Drawn,
                HorizontalSampleBlocks = 1 << level,
                VerticalCellBlocks = 1 << level,
                IdealLevelWithoutHysteresis = TerrainLodDetailSelector.SelectLevel(
                    distance, presentation.MaximumLevel, -1, _qualityFov, _qualityHeight, _qualityDropoff),
                presentation.MinimumLevel,
                presentation.MaximumLevel,
                presentation.TerrainRevision,
                UploadedLevels = presentation.Levels.Keys.Order().ToArray(),
                LevelsWithLayerGeometry = presentation.Levels
                    .Where(pair => layer == "translucent"
                        ? pair.Value.TranslucentMesh is not null
                        : pair.Value.SolidMesh is not null)
                    .Select(pair => pair.Key).Order().ToArray(),
                SpatialOwned = IsAuthoritativeSpatialChunk(key)
            };
        }
    }
}
