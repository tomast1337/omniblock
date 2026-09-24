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
    int TranslucentQuads,
    int CanonicalSpans,
    int CaveCulledColumns,
    int VerticalReducedColumns,
    int RenderedSpans,
    int? CaveCullBelowY)
{
    public static TerrainLodSpatialMeshQuality FromMesh(TerrainLodSpatialMeshData mesh) => new(
        1 << mesh.HorizontalSampleLevel, mesh.VerticalSliceBudget, mesh.MaximumRenderedSpans,
        mesh.Profile.SourceColumns, mesh.Profile.SourceSpans, mesh.SolidQuadCount,
        mesh.TranslucentQuadCount, mesh.Profile.CanonicalSpans,
        mesh.Profile.CaveCulledColumns, mesh.Profile.VerticalReducedColumns,
        mesh.Profile.RenderedSpans, mesh.CaveCullBelowY);
}

internal static class TerrainLodQualityDiagnostics
{
    // An observation, not a guessed root cause: child readiness and source hashes accompany it.
    public static string SelectionRelation(int selected, int desired, int minimum) =>
        selected > Math.Max(desired, minimum) ? "coarser-than-distance-target" :
        selected == minimum && desired < minimum ? "minimum-spatial-level" :
        selected < desired ? "finer-than-distance-target" : "distance-target";

    public static TerrainLodColumnSpan? Sample(TerrainLodColumnTile tile, int x, int y, int z)
    {
        var sampleSize = 1 << tile.HorizontalSampleLevel;
        var localX = (long)x - tile.Key.MinChunkX * 16;
        var localZ = (long)z - tile.Key.MinChunkZ * 16;
        if (localX < 0 || localZ < 0 || localX >= (long)tile.Width * sampleSize ||
            localZ >= (long)tile.Width * sampleSize || (uint)y >= (uint)tile.WorldHeight)
            return null;
        return tile[(int)(localX / sampleSize), (int)(localZ / sampleSize)].At(y);
    }
}

internal sealed partial class ClientTerrainLodRenderer
{
    private Vector3D<double> _qualityCamera;
    private int _qualityNearDistance;
    private int _qualityHorizonDistance;
    private double _qualityFov;
    private int _qualityHeight;
    private double _qualityDropoff;
    // Bounded provenance evidence for dumps, not a second source cache. Older remote receipts
    // may age out, so a false match means unknown provenance rather than locally generated.
    private readonly Queue<(TerrainLodTileKey Tile, string Hash)> _recentRemoteSources = new(64);

    /// <summary>
    /// E2E-only observation of the source behind an installed local or spatial LOD presentation.
    /// A null result is deliberately different from air: no matching publication was found.
    /// Local columns require the same terrain revision; spatial tiles require a canonical hash
    /// match. Neither condition proves pixel-perfect raster output.
    /// </summary>
    internal (string? Material, int SampleSize, string? Owner) PresentedSpatialMaterialAt(int x, int y, int z)
    {
        var chunkX = x >> 4;
        var chunkZ = z >> 4;
        var columnKey = (chunkX, chunkZ);
        if (!IsAuthoritativeSpatialChunk(columnKey) &&
            _resident.TryGetValue(columnKey, out var local) && local.HasLevel(0) &&
            _spatialHierarchy.TryGetCoverage(new TerrainLodTileKey(0, chunkX, chunkZ),
                out var leaf, out _) &&
            leaf?.LeafTerrainRevision == local.TerrainRevision &&
            TerrainLodQualityDiagnostics.Sample(leaf, x, y, z) is { } localSpan)
        {
            var block = _world.Content.Blocks.Get(localSpan.Material.BlockId);
            var translucent = block.RenderLayer != 0;
            var selected = translucent ? _selectedTranslucentLevels : _selectedSolidLevels;
            if (selected.TryGetValue(columnKey, out var level) && level == 0 &&
                local.TryGetLevel(0, translucent, out _))
                return (localSpan.Material.BlockId.ToString(), 1, "local");
        }
        if (!IsAuthoritativeSpatialChunk((chunkX, chunkZ)) || _spatialFrame is not { } frame)
            return (null, -1, null);
        foreach (var draw in frame.Draws)
        {
            var key = draw.Selection.Tile;
            if (!key.ContainsChunk(chunkX, chunkZ) || !_authoritativeSpatialTiles.Contains(key) ||
                !_spatialHierarchy.TryGetCoverage(key, out var source, out var current) ||
                !current || source is null ||
                source.CanonicalHash != draw.Presentation.CanonicalHash)
                continue;
            var span = TerrainLodQualityDiagnostics.Sample(source, x, y, z);
            if (span is { } value)
                return (value.Material.BlockId.ToString(), draw.Presentation.Quality.HorizontalSampleBlocks,
                    "spatial");
        }
        return (null, -1, null);
    }

    private void RecordRemoteSource(TerrainLodColumnTile tile)
    {
        if (_recentRemoteSources.Count == 64) _recentRemoteSources.Dequeue();
        _recentRemoteSources.Enqueue((tile.Key, tile.CanonicalHash));
    }

    /// <summary>
    /// Render-thread, on-demand diagnostic; no GPU readback or per-frame list construction.
    /// Spatial rows are published ownership candidates, NOT a claim every tile/pixel was drawn.
    /// Local rows are selected layers, not every resident cache entry. Exact geometry is separate.
    /// </summary>
    public object CaptureQualitySnapshot()
    {
        var visibleSolidPages = _visibleSpatialSolid.Select(page => page.Page).ToHashSet();
        var visibleTranslucentPages = _visibleSpatialTranslucent.Select(page => page.Page).ToHashSet();
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
                    MatchesRecentRemoteSource = _recentRemoteSources.Contains((key, draw.Presentation.CanonicalHash)),
                    SelectedSolidPages = draw.Presentation.Pages.Count(visibleSolidPages.Contains),
                    SelectedTranslucentPages = draw.Presentation.Pages.Count(visibleTranslucentPages.Contains),
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
        var selectedSolidSeams = _visibleSeams.Select(static seam => seam.Key).ToHashSet();
        var localSolidSeams = _desiredSolidSeams
            .OrderBy(static pair => pair.Key.Owner.X)
            .ThenBy(static pair => pair.Key.Owner.Z)
            .ThenBy(static pair => pair.Key.BoundarySide)
            .ThenBy(static pair => pair.Key.MaterialSide)
            .Select(pair =>
            {
                var installed = _solidSeams.TryGetValue(pair.Key, out var seam);
                return new
                {
                    OwnerX = pair.Key.Owner.X,
                    OwnerZ = pair.Key.Owner.Z,
                    NeighborX = pair.Key.Neighbor.X,
                    NeighborZ = pair.Key.Neighbor.Z,
                    BoundarySide = pair.Key.BoundarySide.ToString(),
                    MaterialSide = pair.Key.MaterialSide.ToString(),
                    pair.Value.OwnerLevel,
                    pair.Value.NeighborLevel,
                    InstalledMatchesDesired = installed && seam!.Selection == pair.Value,
                    InstalledVertices = installed ? seam!.Mesh?.VertexCount ?? 0u : 0u,
                    SelectedForDraw = selectedSolidSeams.Contains(pair.Key)
                };
            }).ToArray();
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
            RetainedConversionVisuals = _conversionVisuals.Count,
            RetainedConversionVisualArrayBytes = _conversionVisuals.RetainedArrayBytes,
            RetainedFineSources = _refinementSources.Count,
            RetainedFineSourceBytes = _refinementSources.RetainedBytes,
            UnloadedConversionsPreserved = _unloadedConversionsPreserved,
            PresentationRevision = _spatialPresentations.Revision,
            CachedForestRevision = _spatialForestCacheKey?.PresentationRevision,
            ReadySpatialTiles = _spatialPresentations.ReadyKeys.OrderBy(key => key.Level)
                .ThenBy(key => key.X).ThenBy(key => key.Z).ToArray(),
            Spatial = spatial,
            Local = local,
            LocalSolidSeams = localSolidSeams
        };

        object LocalRow((int X, int Z) key, int level, string layer)
        {
            var presentation = _resident[key];
            var distance = Math.Sqrt(DistanceSquared(key, _qualityCamera));
            var hasGeometry = presentation.TryGetLevel(level, layer == "translucent", out var gpu);
            return new
            {
                ChunkX = key.X, ChunkZ = key.Z, Layer = layer,
                DistanceToCenterBlocks = distance,
                SelectedLevel = level,
                HasSelectedLayerGeometry = hasGeometry,
                // On-demand selected-body cost proxy. This is not actual submitted vertices while
                // a fade also draws the predecessor or when a seam-only selection is retained.
                SelectedLayerVertices = hasGeometry
                    ? (layer == "translucent" ? gpu.TranslucentMesh : gpu.SolidMesh)!.VertexCount
                    : 0u,
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
