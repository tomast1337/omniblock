using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Profiling;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Lod;
using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal readonly record struct ClientTerrainLodSnapshot(
    int PendingColumns,
    int ConversionOwnedColumns,
    int ResidentColumns,
    int ExactVoxelLevelColumns,
    int TransitionLevelColumns,
    int PresentedColumns,
    int PresentedTranslucentColumns,
    int HandoffPreparingColumns,
    int HandoffOverlapColumns,
    int LevelTransitionColumns,
    int BoundaryLinkedColumns,
    int BoundaryPendingColumns,
    int UploadsThisFrame,
    long ResidentGpuBytes,
    long ResidentBoundaryBytes,
    long StaleResults,
    long RejectedAdmissions,
    long Evictions,
    long HandoffsStarted,
    long HandoffReversals,
    long LevelTransitionsStarted,
    long LevelTransitionReversals,
    long BoundaryRefreshes,
    int CacheEntries,
    long CacheBytes,
    long CacheHits,
    long CacheMisses,
    int CacheWritesPending,
    long CacheWrites,
    long CacheWriteDrops,
    long CacheErrors);

/// <summary>
///     Client owner for the first terrain-horizon slice. It compiles immutable chunk snapshots on
///     the LOD worker and publishes solid/translucent GPU presentations atomically on the render
///     thread. Fine Level 1 GPU data is admitted only around the near-renderer transition band.
/// </summary>
/// <remarks>
///     A LOD presentation is coverage, never authority: it is drawn before the ordinary chunk
///     renderer and is hidden only after that renderer has a complete column presentation. An old
///     LOD mesh remains valid coverage while a newer revision is being built.
/// </remarks>
internal sealed class ClientTerrainLodRenderer : IDisposable, ITerrainPresentationHandoff
{
    public const float MaximumDistanceBlocks = 1024.0f;
    private const int ConversionCapacity = 16;
    private const int PendingCapacity = 4096;
    private const int ResidentCapacity = 2048;
    private const long ResidentGpuByteCapacity = 128L * 1024 * 1024;
    private const int SnapshotsPerTick = 4;
    private const int UploadsPerFrame = 2;
    private const int SeamUploadsPerFrame = 2;
    private const int SeamDrawsPerFrame = 256;
    private const int DrawsPerFrame = 768;
    private const int QuietTicks = 2;
    private const int ExactVoxelMeshLevel = 0;
    private const int TransitionMeshLevel = 1;
    private const int MinimumHorizonMeshLevel = 2;
    private const int MaximumMeshLevel = 4;

    private readonly World _world;
    private readonly TerrainLodConversionService _conversion;
    private readonly TerrainLodCacheStore? _cache;
    private readonly TerrainLodAsyncCacheWriter? _cacheWriter;
    private readonly Dictionary<(int X, int Z), PendingColumn> _pending = [];
    private readonly Dictionary<(int X, int Z), ColumnPresentation> _resident = [];
    private readonly Dictionary<(int X, int Z), int> _detailLevelRequests = [];
    private readonly Dictionary<TerrainLodSeamKey, TerrainLodSeamSelection> _desiredSolidSeams = [];
    private readonly Dictionary<TerrainLodSeamKey, GpuSeam> _solidSeams = [];
    private readonly Dictionary<(int X, int Z), int> _selectedSolidLevels = [];
    private readonly Dictionary<(int X, int Z), TerrainLodSeamColumnState> _solidSeamStates = [];
    private readonly Dictionary<TerrainLodSeamKey, TerrainLodSeamFade> _solidSeamFades = [];
    private readonly Dictionary<TerrainLodSeamKey, TerrainLodSeamSelection> _desiredTranslucentSeams = [];
    private readonly Dictionary<TerrainLodSeamKey, GpuSeam> _translucentSeams = [];
    private readonly Dictionary<(int X, int Z), int> _selectedTranslucentLevels = [];
    private readonly Dictionary<(int X, int Z), TerrainLodSeamColumnState> _translucentSeamStates = [];
    private readonly Dictionary<TerrainLodSeamKey, TerrainLodSeamFade> _translucentSeamFades = [];
    private readonly List<VisibleColumn> _visible = [];
    private readonly List<VisibleSeam> _visibleSeams = [];
    private readonly List<VisibleSeam> _visibleTranslucentSeams = [];
    private readonly List<VisibleTranslucentDraw> _visibleTranslucentDraws = [];
    private ChunkUniforms[] _uniforms = [];
    private WgpuPipeline? _opaquePipeline;
    private WgpuPipeline? _translucentPipeline;
    private long _tick;
    private long _staleResults;
    private long _rejectedAdmissions;
    private long _evictions;
    private long _handoffsStarted;
    private long _handoffReversals;
    private long _levelTransitionsStarted;
    private long _levelTransitionReversals;
    private long _boundaryRefreshes;
    private bool _disposed;
    private ClientTerrainLodSnapshot _snapshot;

    public ClientTerrainLodRenderer(World world, TerrainLodCacheStore? cache = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _cache = cache;
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        _conversion = cache is null
            ? new TerrainLodConversionService(
                world.Dimension.Id,
                materials,
                TerrainLodReductionStrategy.SurfacePreserving,
                ConversionCapacity)
            : new TerrainLodConversionService(
                world.Dimension.Id,
                materials,
                cache,
                TerrainLodReductionStrategy.SurfacePreserving,
                ConversionCapacity);
        if (cache is not null) _cacheWriter = new TerrainLodAsyncCacheWriter(cache);
    }

    public ClientTerrainLodSnapshot Snapshot => _snapshot;

    public TerrainNearHandoff GetNearHandoff(int chunkX, int chunkZ, bool translucent)
    {
        if (!_resident.TryGetValue((chunkX, chunkZ), out var presentation) ||
            !presentation.HasLayer(translucent)) return TerrainNearHandoff.Inactive;
        return new TerrainNearHandoff(
            true,
            presentation.HandoffFor(translucent).Progress,
            FadeSeed((chunkX, chunkZ)));
    }

    /// <summary>Coalesces terrain changes by chunk coordinate without retaining source arrays.</summary>
    public void ObserveRegion(int minX, int minZ, int maxX, int maxZ)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var minChunkX = minX >> 4;
        var minChunkZ = minZ >> 4;
        var maxChunkX = maxX >> 4;
        var maxChunkZ = maxZ >> 4;
        for (var chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
        for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
            ObserveColumn(chunkX, chunkZ);
    }

    public void Tick(Vector3D<double> viewPosition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _tick++;

        var due = TerrainLodAdmissionOrder.TakeNearest(
            _pending
            .Where(pair => pair.Value.DueTick <= _tick)
            .Select(pair => pair.Key), viewPosition, SnapshotsPerTick);

        foreach (var key in due)
        {
            if (!_world.BlockHost.HasChunk(key.X, key.Z))
            {
                _pending.Remove(key);
                continue;
            }

            var chunk = _world.BlockHost.GetChunk(key.X, key.Z);
            if (!chunk.Loaded)
            {
                _pending[key] = _pending[key] with { DueTick = _tick + 1 };
                continue;
            }

            var source = TerrainLodSourceSnapshot.Capture(chunk);
            var result = _conversion.Submit(source);
            if (result == TerrainLodAdmissionResult.RejectedAtCapacity)
            {
                _rejectedAdmissions++;
                _pending[key] = _pending[key] with { DueTick = _tick + 1 };
                continue;
            }

            _pending.Remove(key);
        }

        EvictDistant(viewPosition);
        PublishSnapshot(0, 0);
    }

    /// <summary>
    ///     Draws distant opaque coverage before near terrain. The shared depth buffer makes the
    ///     later, more detailed near presentation replace overlapping LOD pixels naturally.
    /// </summary>
    public unsafe void Render(in ChunkRenderParams parameters, ChunkRenderer nearRenderer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(nearRenderer);
        using var _lodRender = Profiler.Begin("TerrainLodRender");
        var uploads = InstallCompleted(UploadsPerFrame, parameters.ViewPos,
            parameters.VerticalFovDegrees, parameters.ViewportHeight,
            parameters.TerrainLodDropoffScale,
            parameters.RenderDistance, nearRenderer);
        EvictDistant(parameters.ViewPos);

        if (RenderSystem.Fog.Curve != FogCurve.Linear ||
            RenderSystem.DrawTargetOrNull is not WebGpuDrawTarget target ||
            target.CurrentPass is null || target.TerrainArray is not { } terrainArray ||
            WebGpuDevice.Current is not { } device)
        {
            PublishSnapshot(uploads, 0);
            return;
        }

        _visible.Clear();
        _visibleSeams.Clear();
        _selectedSolidLevels.Clear();
        _solidSeamStates.Clear();
        _solidSeamFades.Clear();
        var maximumDistance = Math.Min(
            MaximumDistanceBlocks, Math.Max(1, parameters.TerrainHorizonDistance) * 16.0f);
        var maximumDistanceSquared = maximumDistance * maximumDistance;
        foreach (var (key, presentation) in _resident)
        {
            var distanceSquared = DistanceSquared(key, parameters.ViewPos);
            if (distanceSquared > maximumDistanceSquared ||
                !parameters.Camera.IsBoundingBoxInFrustum(new Box(
                    key.X * 16, 0, key.Z * 16,
                    key.X * 16 + 16, ChuckFormat.WorldHeight, key.Z * 16 + 16)))
                continue;

            if (!presentation.HasLayer(translucent: false))
            {
                _solidSeamStates[key] = new TerrainLodSeamColumnState(
                    presentation.Boundaries.NearestLevel(presentation.MinimumLevel),
                    1, FadeSeed(key), false);
                continue;
            }
            var (nearPresent, nearReady) = NearState(
                key, distanceSquared, parameters.RenderDistance, nearRenderer);
            var handoff = UpdateHandoff(presentation,
                translucent: false, nearPresent, nearReady,
                parameters.DeltaTime, parameters.ChunkFade);
            var requestedLevel = TerrainLodDetailSelector.SelectLevel(
                Math.Sqrt(distanceSquared), presentation.MaximumLevel,
                presentation.SelectionLevel(translucent: false),
                parameters.VerticalFovDegrees, parameters.ViewportHeight,
                parameters.TerrainLodDropoffScale);
            requestedLevel = ConstrainLevelToNeighbors(
                key, requestedLevel, translucent: false);
            if (requestedLevel < presentation.MinimumLevel)
                RequestDetailLevel(key, requestedLevel);
            if (handoff.Progress >= 1)
            {
                if (presentation.TryGetNearestLevel(
                        requestedLevel, translucent: false, out var exactLevel, out _))
                    _solidSeamStates[key] = new TerrainLodSeamColumnState(
                        exactLevel, handoff.Progress, FadeSeed(key), Drawn: false);
                continue;
            }
            var presentedLevel = AppendVisibleLevels(
                presentation, key, distanceSquared, requestedLevel,
                translucent: false, handoff.Progress,
                parameters.DeltaTime, parameters.ChunkFade);
            if (presentedLevel >= 0)
            {
                _selectedSolidLevels[key] = presentedLevel;
                _solidSeamStates[key] = new TerrainLodSeamColumnState(
                    presentedLevel, handoff.Progress, FadeSeed(key), Drawn: true);
            }
        }

        _visible.Sort(static (a, b) =>
        {
            var distance = a.DistanceSquared.CompareTo(b.DistanceSquared);
            if (distance != 0) return distance;
            var x = a.Key.X.CompareTo(b.Key.X);
            if (x != 0) return x;
            var z = a.Key.Z.CompareTo(b.Key.Z);
            return z != 0 ? z : a.Level.CompareTo(b.Level);
        });
        if (_visible.Count > DrawsPerFrame)
            _visible.RemoveRange(DrawsPerFrame, _visible.Count - DrawsPerFrame);
        var drawnColumns = _visible.Select(static item => item.Key).ToHashSet();
        foreach (var key in _selectedSolidLevels.Keys
                     .Where(key => !drawnColumns.Contains(key)).ToArray())
        {
            _selectedSolidLevels.Remove(key);
            _solidSeamStates.Remove(key);
        }
        BuildDesiredSeams(
            _solidSeamStates, _desiredSolidSeams, _solidSeamFades);
        uploads += UpdateSeams(device, parameters.ViewPos, SeamUploadsPerFrame,
            translucent: false, _desiredSolidSeams, _solidSeams);
        CollectVisibleSeams(parameters.ViewPos, backToFront: false,
            _desiredSolidSeams, _solidSeams, _solidSeamFades, _visibleSeams);
        var seamDrawBudget = Math.Min(
            SeamDrawsPerFrame, Math.Max(0, DrawsPerFrame - _visible.Count));
        if (_visibleSeams.Count > seamDrawBudget)
            _visibleSeams.RemoveRange(seamDrawBudget, _visibleSeams.Count - seamDrawBudget);
        if (_visible.Count == 0)
        {
            PublishSnapshot(uploads, 0);
            return;
        }

        _opaquePipeline ??= ChunkRenderer.CreateWgpuPipeline(device, RenderState.Opaque);
        _opaquePipeline.Bind(target.CurrentPass);
        WgpuPipeline.BindGroup(target.CurrentPass, 1,
            terrainArray.BindGroupFor(_opaquePipeline.TextureBindGroupLayout), device.Api);

        var drawCount = _visible.Count + _visibleSeams.Count;
        if (_uniforms.Length < drawCount) _uniforms = new ChunkUniforms[drawCount];
        for (var i = 0; i < _visible.Count; i++)
            _uniforms[i] = BuildUniforms(
                parameters, _visible[i].Key, _visible[i].FadeProgress,
                _visible[i].FadeMode, _visible[i].FadeSeed);
        for (var i = 0; i < _visibleSeams.Count; i++)
            _uniforms[_visible.Count + i] = BuildUniforms(
                parameters, _visibleSeams[i].Key.Owner,
                _visibleSeams[i].Fade.Progress,
                _visibleSeams[i].Fade.Mode,
                _visibleSeams[i].Fade.Seed);
        _opaquePipeline.WriteDynamicUniforms(_uniforms.AsSpan(0, drawCount));

        for (var i = 0; i < _visible.Count; i++)
        {
            _opaquePipeline.BindDynamicUniforms(target.CurrentPass, i);
            var gpu = _visible[i].Gpu;
            gpu.SolidMesh!.Draw(target.CurrentPass, lightBuffer: gpu.Lighting!.Solid);
        }

        for (var i = 0; i < _visibleSeams.Count; i++)
        {
            _opaquePipeline.BindDynamicUniforms(target.CurrentPass, _visible.Count + i);
            var seam = _visibleSeams[i].Gpu;
            seam.Mesh!.Draw(target.CurrentPass, lightBuffer: seam.Lighting!.Solid);
        }

        PublishSnapshot(uploads, _visible.Count);
    }

    /// <summary>
    ///     Draws the independently owned liquid/glass layer in back-to-front column order. The
    ///     caller supplies the same blended terrain state used by the full-detail translucent pass.
    /// </summary>
    public unsafe void RenderTransparent(in ChunkRenderParams parameters, ChunkRenderer nearRenderer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(nearRenderer);
        if (RenderSystem.Fog.Curve != FogCurve.Linear ||
            RenderSystem.DrawTargetOrNull is not WebGpuDrawTarget target ||
            target.CurrentPass is null || target.TerrainArray is not { } terrainArray ||
            WebGpuDevice.Current is not { } device)
        {
            _snapshot = _snapshot with { PresentedTranslucentColumns = 0 };
            return;
        }

        _visible.Clear();
        _visibleTranslucentSeams.Clear();
        _selectedTranslucentLevels.Clear();
        _translucentSeamStates.Clear();
        _translucentSeamFades.Clear();
        var maximumDistance = Math.Min(
            MaximumDistanceBlocks, Math.Max(1, parameters.TerrainHorizonDistance) * 16.0f);
        var maximumDistanceSquared = maximumDistance * maximumDistance;
        foreach (var (key, presentation) in _resident)
        {
            var distanceSquared = DistanceSquared(key, parameters.ViewPos);
            if (distanceSquared > maximumDistanceSquared ||
                !parameters.Camera.IsBoundingBoxInFrustum(new Box(
                    key.X * 16, 0, key.Z * 16,
                    key.X * 16 + 16, ChuckFormat.WorldHeight, key.Z * 16 + 16)))
                continue;

            if (!presentation.HasLayer(translucent: true))
            {
                _translucentSeamStates[key] = new TerrainLodSeamColumnState(
                    presentation.Boundaries.NearestLevel(presentation.MinimumLevel),
                    1, FadeSeed(key), false);
                continue;
            }
            var (nearPresent, nearReady) = NearState(
                key, distanceSquared, parameters.RenderDistance, nearRenderer);
            var handoff = UpdateHandoff(presentation,
                translucent: true, nearPresent, nearReady,
                parameters.DeltaTime, parameters.ChunkFade);
            var requestedLevel = TerrainLodDetailSelector.SelectLevel(
                Math.Sqrt(distanceSquared), presentation.MaximumLevel,
                presentation.SelectionLevel(translucent: true),
                parameters.VerticalFovDegrees, parameters.ViewportHeight,
                parameters.TerrainLodDropoffScale);
            requestedLevel = ConstrainLevelToNeighbors(
                key, requestedLevel, translucent: true);
            if (requestedLevel < presentation.MinimumLevel)
                RequestDetailLevel(key, requestedLevel);
            if (handoff.Progress >= 1)
            {
                if (presentation.TryGetNearestLevel(
                        requestedLevel, translucent: true, out var exactLevel, out _))
                    _translucentSeamStates[key] = new TerrainLodSeamColumnState(
                        exactLevel, handoff.Progress, FadeSeed(key), Drawn: false);
                continue;
            }
            var presentedLevel = AppendVisibleLevels(
                presentation, key, distanceSquared, requestedLevel,
                translucent: true, handoff.Progress,
                parameters.DeltaTime, parameters.ChunkFade);
            if (presentedLevel >= 0)
            {
                _selectedTranslucentLevels[key] = presentedLevel;
                _translucentSeamStates[key] = new TerrainLodSeamColumnState(
                    presentedLevel, handoff.Progress, FadeSeed(key), Drawn: true);
            }
        }

        _visible.Sort(static (a, b) =>
        {
            var distance = b.DistanceSquared.CompareTo(a.DistanceSquared);
            if (distance != 0) return distance;
            var x = a.Key.X.CompareTo(b.Key.X);
            if (x != 0) return x;
            var z = a.Key.Z.CompareTo(b.Key.Z);
            return z != 0 ? z : b.Level.CompareTo(a.Level);
        });
        if (_visible.Count > DrawsPerFrame)
            _visible.RemoveRange(DrawsPerFrame, _visible.Count - DrawsPerFrame);
        var drawnColumns = _visible.Select(static item => item.Key).ToHashSet();
        foreach (var key in _selectedTranslucentLevels.Keys
                     .Where(key => !drawnColumns.Contains(key)).ToArray())
        {
            _selectedTranslucentLevels.Remove(key);
            _translucentSeamStates.Remove(key);
        }
        BuildDesiredSeams(
            _translucentSeamStates, _desiredTranslucentSeams, _translucentSeamFades);
        var seamUploads = UpdateSeams(device, parameters.ViewPos, SeamUploadsPerFrame,
            translucent: true, _desiredTranslucentSeams, _translucentSeams);
        CollectVisibleSeams(parameters.ViewPos, backToFront: true,
            _desiredTranslucentSeams, _translucentSeams,
            _translucentSeamFades, _visibleTranslucentSeams);
        var seamDrawBudget = Math.Min(
            SeamDrawsPerFrame, Math.Max(0, DrawsPerFrame - _visible.Count));
        if (_visibleTranslucentSeams.Count > seamDrawBudget)
            _visibleTranslucentSeams.RemoveRange(
                seamDrawBudget, _visibleTranslucentSeams.Count - seamDrawBudget);
        _visibleTranslucentDraws.Clear();
        _visibleTranslucentDraws.AddRange(_visible.Select(static column =>
            new VisibleTranslucentDraw(column, null, column.Key, column.DistanceSquared)));
        _visibleTranslucentDraws.AddRange(_visibleTranslucentSeams.Select(static seam =>
            new VisibleTranslucentDraw(
                null, seam.Gpu, seam.Key.Owner, seam.DistanceSquared, seam.Fade)));
        _visibleTranslucentDraws.Sort(static (a, b) =>
        {
            var distance = b.DistanceSquared.CompareTo(a.DistanceSquared);
            if (distance != 0) return distance;
            var x = a.Key.X.CompareTo(b.Key.X);
            return x != 0 ? x : a.Key.Z.CompareTo(b.Key.Z);
        });
        if (_visible.Count == 0)
        {
            _snapshot = _snapshot with { PresentedTranslucentColumns = 0 };
            return;
        }

        _translucentPipeline ??= ChunkRenderer.CreateWgpuPipeline(device, RenderSystem.State.Current);
        _translucentPipeline.Bind(target.CurrentPass);
        WgpuPipeline.BindGroup(target.CurrentPass, 1,
            terrainArray.BindGroupFor(_translucentPipeline.TextureBindGroupLayout), device.Api);

        var drawCount = _visibleTranslucentDraws.Count;
        if (_uniforms.Length < drawCount) _uniforms = new ChunkUniforms[drawCount];
        for (var i = 0; i < drawCount; i++)
        {
            var draw = _visibleTranslucentDraws[i];
            var column = draw.Column;
            _uniforms[i] = BuildUniforms(
                parameters, draw.Key,
                column?.FadeProgress ?? draw.SeamFade.Progress,
                column?.FadeMode ?? draw.SeamFade.Mode,
                column?.FadeSeed ?? draw.SeamFade.Seed);
        }
        _translucentPipeline.WriteDynamicUniforms(_uniforms.AsSpan(0, drawCount));

        for (var i = 0; i < drawCount; i++)
        {
            _translucentPipeline.BindDynamicUniforms(target.CurrentPass, i);
            var draw = _visibleTranslucentDraws[i];
            if (draw.Column is { } column)
                column.Gpu.TranslucentMesh!.Draw(
                    target.CurrentPass, lightBuffer: column.Gpu.Lighting!.Translucent);
            else
                draw.Seam!.Mesh!.Draw(
                    target.CurrentPass, lightBuffer: draw.Seam.Lighting!.Translucent);
        }

        _snapshot = _snapshot with
        {
            PresentedTranslucentColumns = _visible.Count,
            UploadsThisFrame = _snapshot.UploadsThisFrame + seamUploads
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _conversion.Dispose();
        _cacheWriter?.Dispose();
        foreach (var presentation in _resident.Values) presentation.Dispose();
        foreach (var seam in _solidSeams.Values) seam.Dispose();
        foreach (var seam in _translucentSeams.Values) seam.Dispose();
        _resident.Clear();
        _solidSeams.Clear();
        _translucentSeams.Clear();
        _desiredSolidSeams.Clear();
        _desiredTranslucentSeams.Clear();
        _selectedSolidLevels.Clear();
        _selectedTranslucentLevels.Clear();
        _pending.Clear();
        _detailLevelRequests.Clear();
        _opaquePipeline?.Dispose();
        _opaquePipeline = null;
        _translucentPipeline?.Dispose();
        _translucentPipeline = null;
    }

    private void ObserveColumn(int chunkX, int chunkZ)
    {
        var key = (chunkX, chunkZ);
        if (_world.BlockHost.HasChunk(chunkX, chunkZ))
        {
            var chunk = _world.BlockHost.GetChunk(chunkX, chunkZ);
            if (chunk.Loaded &&
                _resident.TryGetValue(key, out var current) &&
                current.TerrainRevision == chunk.TerrainRevision &&
                !_pending.ContainsKey(key)) return;
        }

        if (_pending.ContainsKey(key))
        {
            _pending[key] = new PendingColumn(_tick + QuietTicks);
            return;
        }

        if (_pending.Count >= PendingCapacity)
        {
            _rejectedAdmissions++;
            return;
        }
        _pending.Add(key, new PendingColumn(_tick + QuietTicks));
    }

    private int InstallCompleted(
        int budget,
        Vector3D<double> viewPosition,
        double verticalFovDegrees,
        int viewportHeight,
        float detailDropoffScale,
        int renderDistance,
        ChunkRenderer nearRenderer)
    {
        var installed = 0;
        while (installed < budget && _conversion.TryTakeCompleted(out var result) && result is not null)
        {
            var key = (result.ChunkX, result.ChunkZ);
            if (_world.BlockHost.HasChunk(key.ChunkX, key.ChunkZ))
            {
                var chunk = _world.BlockHost.GetChunk(key.ChunkX, key.ChunkZ);
                if (chunk.Loaded && chunk.TerrainRevision != result.TerrainRevision)
                {
                    _staleResults++;
                    ObserveColumn(key.ChunkX, key.ChunkZ);
                    continue;
                }
            }

            ColumnPresentation? candidate = null;
            try
            {
                _cacheWriter?.TrySubmit(result);
                _resident.TryGetValue(key, out var previous);
                var selectedLevel = TerrainLodDetailSelector.SelectLevel(
                    Math.Sqrt(DistanceSquared(key, viewPosition)), MaximumMeshLevel,
                    previous?.SelectionLevel(translucent: false) ?? -1,
                    verticalFovDegrees, viewportHeight, detailDropoffScale);
                var minimumLevel = Math.Min(
                    Math.Min(selectedLevel, previous?.MinimumLevel ?? MinimumHorizonMeshLevel),
                    _detailLevelRequests.GetValueOrDefault(key, MinimumHorizonMeshLevel));
                candidate = ColumnPresentation.Create(
                    _world, result, result.Lighting, minimumLevel);
                if (previous is not null)
                {
                    candidate.CopyHandoffsFrom(previous);
                }
                else
                {
                    var distanceSquared = DistanceSquared(key, viewPosition);
                    var (nearPresent, nearReady) = NearState(
                        key, distanceSquared, renderDistance, nearRenderer);
                    if (nearPresent && nearReady) candidate.InitializeNearOnly();
                }
                if (_resident.Remove(key, out var old)) old.Dispose();
                _resident.Add(key, candidate);
                if (_detailLevelRequests.TryGetValue(key, out var requestedMinimum) &&
                    candidate.MinimumLevel <= requestedMinimum)
                    _detailLevelRequests.Remove(key);
                candidate = null;
                installed++;
            }
            finally
            {
                candidate?.Dispose();
            }
        }
        return installed;
    }

    private void RequestDetailLevel((int X, int Z) key, int requestedLevel)
    {
        requestedLevel = Math.Clamp(
            requestedLevel, ExactVoxelMeshLevel, MinimumHorizonMeshLevel);
        if (_detailLevelRequests.TryGetValue(key, out var existingRequest) &&
            existingRequest <= requestedLevel) return;
        if (_pending.ContainsKey(key))
        {
            _detailLevelRequests[key] = requestedLevel;
            return;
        }
        if (!_world.BlockHost.HasChunk(key.X, key.Z)) return;
        var chunk = _world.BlockHost.GetChunk(key.X, key.Z);
        if (!chunk.Loaded) return;
        if (_pending.Count >= PendingCapacity)
        {
            _rejectedAdmissions++;
            return;
        }

        _detailLevelRequests[key] = requestedLevel;
        // This is a presentation-detail upgrade of an already valid LOD, not a terrain edit. It may
        // enter the bounded conversion queue on the next tick without the edit quiet period.
        _pending.Add(key, new PendingColumn(_tick));
    }

    private void BuildDesiredSeams(
        Dictionary<(int X, int Z), TerrainLodSeamColumnState> states,
        Dictionary<TerrainLodSeamKey, TerrainLodSeamSelection> desired,
        Dictionary<TerrainLodSeamKey, TerrainLodSeamFade> fades)
    {
        desired.Clear();
        fades.Clear();
        foreach (var (key, state) in states)
        {
            if (!state.Drawn) continue;
            Add(key, Side.East, (key.X + 1, key.Z));
            Add((key.X - 1, key.Z), Side.East, key);
            Add(key, Side.South, (key.X, key.Z + 1));
            Add((key.X, key.Z - 1), Side.South, key);
        }

        void Add(
            (int X, int Z) ownerKey,
            Side side,
            (int X, int Z) neighborKey)
        {
            if (!states.TryGetValue(ownerKey, out var ownerState) ||
                !states.TryGetValue(neighborKey, out var neighborState) ||
                (!ownerState.Drawn && !neighborState.Drawn) ||
                !_resident.TryGetValue(ownerKey, out var owner) ||
                !_resident.TryGetValue(neighborKey, out var neighbor)) return;
            var ownerLevel = ownerState.Level;
            var neighborLevel = neighborState.Level;
            if (Math.Abs(ownerLevel - neighborLevel) > 1)
            {
                // A NearOnly column may retain an older coarse hierarchy while the visible LOD
                // side has already upgraded. It is evidence, not presentation: select the nearest
                // available adjacent tier rather than dropping the exact/LOD boundary altogether.
                if (!ownerState.Drawn)
                {
                    var candidate = ownerLevel < neighborLevel
                        ? neighborLevel - 1
                        : neighborLevel + 1;
                    if (owner.Boundaries.HasLevel(candidate)) ownerLevel = candidate;
                }
                else if (!neighborState.Drawn)
                {
                    var candidate = neighborLevel < ownerLevel
                        ? ownerLevel - 1
                        : ownerLevel + 1;
                    if (neighbor.Boundaries.HasLevel(candidate)) neighborLevel = candidate;
                }
                if (Math.Abs(ownerLevel - neighborLevel) > 1) return;
            }

            var plan = TerrainLodSeamCoverage.Plan(
                ownerState.Drawn, ownerState.NearProgress,
                neighborState.Drawn, neighborState.NearProgress);
            if (plan.Combined)
            {
                AddSelection(TerrainLodSeamMaterialSide.Either, default);
                return;
            }

            if (plan.Owner)
                AddSelection(TerrainLodSeamMaterialSide.Owner,
                    TerrainLodSeamFade.ForLod(ownerState.NearProgress, ownerState.FadeSeed));
            if (plan.Neighbor)
                AddSelection(TerrainLodSeamMaterialSide.Neighbor,
                    TerrainLodSeamFade.ForLod(neighborState.NearProgress, neighborState.FadeSeed));

            void AddSelection(
                TerrainLodSeamMaterialSide materialSide,
                TerrainLodSeamFade fade)
            {
                var seamKey = new TerrainLodSeamKey(ownerKey, side, materialSide);
                desired[seamKey] = new TerrainLodSeamSelection(
                    owner.Boundaries.Identity,
                    neighbor.Boundaries.Identity,
                    ownerLevel,
                    neighborLevel);
                fades[seamKey] = fade;
            }
        }

    }

    private int UpdateSeams(
        WebGpuDevice device,
        Vector3D<double> viewPosition,
        int uploadBudget,
        bool translucent,
        Dictionary<TerrainLodSeamKey, TerrainLodSeamSelection> desired,
        Dictionary<TerrainLodSeamKey, GpuSeam> installed)
    {
        foreach (var (key, seam) in installed.ToArray())
        {
            if (_resident.ContainsKey(key.Owner) && _resident.ContainsKey(key.Neighbor)) continue;
            installed.Remove(key);
            seam.Dispose();
        }

        var uploads = 0;
        foreach (var group in desired
                     .GroupBy(static pair => (pair.Key.Owner, pair.Key.BoundarySide))
                     .OrderBy(group => DistanceSquared(group.Key.Owner, viewPosition))
                     .ThenBy(static group => group.Key.Owner.X)
                     .ThenBy(static group => group.Key.Owner.Z)
                     .ThenBy(static group => group.Key.BoundarySide))
        {
            var missing = group.Count(pair =>
                !installed.TryGetValue(pair.Key, out var current) ||
                current.Selection != pair.Value);
            if (missing == 0) continue;
            // Owner/neighbor split artifacts are one presentation replacement. Never publish only
            // half because the per-frame seam budget happened to end between them.
            if (uploads + missing > uploadBudget) continue;
            foreach (var (key, selection) in group.OrderBy(static pair => pair.Key.MaterialSide))
            {
                if (installed.TryGetValue(key, out var current) &&
                    current.Selection == selection) continue;
                var owner = _resident[key.Owner];
                var neighbor = _resident[key.Neighbor];
                var data = translucent
                    ? TerrainLodSeamMeshBuilder.BuildTranslucent(
                        owner.Boundaries, selection.OwnerLevel,
                        neighbor.Boundaries, selection.NeighborLevel,
                        key.BoundarySide, _world.Content.Blocks,
                        !_world.Dimension.HasCeiling,
                        lighting: _world.Lighting, visuals: _world.Reader,
                        materialSide: key.MaterialSide)
                    : TerrainLodSeamMeshBuilder.BuildSolid(
                        owner.Boundaries, selection.OwnerLevel,
                        neighbor.Boundaries, selection.NeighborLevel,
                        key.BoundarySide, _world.Content.Blocks,
                        !_world.Dimension.HasCeiling,
                        lighting: _world.Lighting, visuals: _world.Reader,
                        materialSide: key.MaterialSide);
                var candidate = GpuSeam.Create(
                    device, key.Owner, selection, data, translucent);
                if (installed.Remove(key, out var previous)) previous.Dispose();
                installed.Add(key, candidate);
                _boundaryRefreshes++;
                uploads++;
            }
        }
        return uploads;
    }

    private static void CollectVisibleSeams(
        Vector3D<double> viewPosition,
        bool backToFront,
        Dictionary<TerrainLodSeamKey, TerrainLodSeamSelection> desired,
        Dictionary<TerrainLodSeamKey, GpuSeam> installed,
        Dictionary<TerrainLodSeamKey, TerrainLodSeamFade> fades,
        List<VisibleSeam> destination)
    {
        foreach (var group in desired.GroupBy(
                     static pair => (pair.Key.Owner, pair.Key.BoundarySide)))
        {
            var desiredParts = group.ToArray();
            var ready = desiredParts.All(pair =>
                installed.TryGetValue(pair.Key, out var seam) &&
                seam.Selection == pair.Value);
            if (ready)
            {
                foreach (var (key, selection) in desiredParts)
                {
                    var seam = installed[key];
                    if (seam.Selection != selection || seam.Mesh is null) continue;
                    Add(key, seam, fades.GetValueOrDefault(key));
                }
                continue;
            }

            // Keep the previous complete edge until every part of the replacement exists. This
            // may briefly retain a coplanar exact face, but it never turns an upload budget into a
            // visible crack; the split replacement removes that overlap atomically.
            var fallback = installed
                .Where(pair => pair.Key.Owner == group.Key.Owner &&
                               pair.Key.BoundarySide == group.Key.BoundarySide)
                .OrderBy(static pair => pair.Key.MaterialSide ==
                    TerrainLodSeamMaterialSide.Either ? 0 : 1)
                .ThenBy(static pair => pair.Key.MaterialSide)
                .ToArray();
            if (fallback.Any(static pair =>
                    pair.Key.MaterialSide == TerrainLodSeamMaterialSide.Either))
                fallback = fallback.Where(static pair =>
                    pair.Key.MaterialSide == TerrainLodSeamMaterialSide.Either).ToArray();
            foreach (var (key, seam) in fallback)
                if (seam.Mesh is not null) Add(key, seam, default);

            void Add(
                TerrainLodSeamKey key,
                GpuSeam seam,
                TerrainLodSeamFade fade) => destination.Add(new VisibleSeam(
                key, seam, DistanceSquared(key.Owner, viewPosition), fade));
        }
        destination.Sort((a, b) =>
        {
            var distance = backToFront
                ? b.DistanceSquared.CompareTo(a.DistanceSquared)
                : a.DistanceSquared.CompareTo(b.DistanceSquared);
            if (distance != 0) return distance;
            var x = a.Key.Owner.X.CompareTo(b.Key.Owner.X);
            if (x != 0) return x;
            var z = a.Key.Owner.Z.CompareTo(b.Key.Owner.Z);
            if (z != 0) return z;
            var side = a.Key.BoundarySide.CompareTo(b.Key.BoundarySide);
            return side != 0 ? side : a.Key.MaterialSide.CompareTo(b.Key.MaterialSide);
        });
    }

    private int ConstrainLevelToNeighbors(
        (int X, int Z) key,
        int requestedLevel,
        bool translucent)
    {
        Span<int> levels = stackalloc int[4];
        var count = 0;
        count = AppendNeighborLevel((key.X, key.Z - 1), translucent, levels, count);
        count = AppendNeighborLevel((key.X, key.Z + 1), translucent, levels, count);
        count = AppendNeighborLevel((key.X - 1, key.Z), translucent, levels, count);
        count = AppendNeighborLevel((key.X + 1, key.Z), translucent, levels, count);
        return TerrainLodNeighborLevelConstraint.Constrain(
            requestedLevel, levels[..count]);
    }

    private int AppendNeighborLevel(
        (int X, int Z) key,
        bool translucent,
        Span<int> destination,
        int count)
    {
        if (!_resident.TryGetValue(key, out var neighbor) ||
            !neighbor.HasLayer(translucent)) return count;
        var level = neighbor.SelectionLevel(translucent);
        if (level >= 0) destination[count++] = level;
        return count;
    }

    private void EvictDistant(Vector3D<double> viewPosition)
    {
        var residentBytes = _resident.Values.Sum(static value => value.EstimatedBytes) +
                            _solidSeams.Values.Sum(static value => value.EstimatedBytes) +
                            _translucentSeams.Values.Sum(static value => value.EstimatedBytes);
        if (_resident.Count <= ResidentCapacity && residentBytes <= ResidentGpuByteCapacity) return;
        var remove = _resident
            .OrderByDescending(pair => DistanceSquared(pair.Key, viewPosition))
            .ThenBy(pair => pair.Value.LastPresentedTick)
            .ToArray();
        foreach (var candidate in remove)
        {
            if (_resident.Count <= ResidentCapacity && residentBytes <= ResidentGpuByteCapacity) break;
            if (!_resident.Remove(candidate.Key, out var presentation)) continue;
            residentBytes -= presentation.EstimatedBytes;
            residentBytes -= RemoveSolidSeamsForColumn(candidate.Key);
            residentBytes -= RemoveTranslucentSeamsForColumn(candidate.Key);
            presentation.Dispose();
            _evictions++;
        }
    }

    private long RemoveSolidSeamsForColumn((int X, int Z) key)
        => RemoveSeamsForColumn(_solidSeams, key);

    private long RemoveTranslucentSeamsForColumn((int X, int Z) key)
        => RemoveSeamsForColumn(_translucentSeams, key);

    private static long RemoveSeamsForColumn(
        Dictionary<TerrainLodSeamKey, GpuSeam> seams,
        (int X, int Z) key)
    {
        long removedBytes = 0;
        foreach (var (seamKey, seam) in seams
                     .Where(pair => pair.Key.Owner == key || pair.Key.Neighbor == key)
                     .ToArray())
        {
            seams.Remove(seamKey);
            removedBytes += seam.EstimatedBytes;
            seam.Dispose();
        }
        return removedBytes;
    }

    private void PublishSnapshot(int uploads, int draws)
    {
        var conversion = _conversion.Snapshot();
        var preparing = _resident.Values.Count(static value =>
            value.IsInState(TerrainLodHandoffState.NearPreparing));
        var overlap = _resident.Values.Count(static value =>
            value.IsInState(TerrainLodHandoffState.Overlap));
        var levelTransitions = _resident.Values.Count(static value =>
            value.HasActiveLevelTransition);
        var cache = _cache?.Snapshot();
        var cacheWriter = _cacheWriter?.Snapshot();
        _snapshot = new ClientTerrainLodSnapshot(
            _pending.Count,
            conversion.OwnedChunks,
            _resident.Count,
            _resident.Values.Count(static value => value.HasLevel(ExactVoxelMeshLevel)),
            _resident.Values.Count(static value => value.HasLevel(TransitionMeshLevel)),
            draws,
            _snapshot.PresentedTranslucentColumns,
            preparing,
            overlap,
            levelTransitions,
            _solidSeams.Count + _translucentSeams.Count,
            _desiredSolidSeams.Count(pair =>
                !_solidSeams.TryGetValue(pair.Key, out var seam) ||
                seam.Selection != pair.Value) +
            _desiredTranslucentSeams.Count(pair =>
                !_translucentSeams.TryGetValue(pair.Key, out var seam) ||
                seam.Selection != pair.Value),
            uploads,
            _resident.Values.Sum(static value => value.EstimatedBytes) +
            _solidSeams.Values.Sum(static value => value.EstimatedBytes) +
            _translucentSeams.Values.Sum(static value => value.EstimatedBytes),
            _resident.Values.Sum(static value => value.Boundaries.EstimatedBytes),
            _staleResults,
            _rejectedAdmissions,
            _evictions,
            _handoffsStarted,
            _handoffReversals,
            _levelTransitionsStarted,
            _levelTransitionReversals,
            _boundaryRefreshes,
            cache?.EntryCount ?? 0,
            cache?.CurrentBytes ?? 0,
            cache?.ReadHits ?? 0,
            (cache?.ReadMisses ?? 0) + (cache?.StaleReads ?? 0) +
            (cache?.IncompatibleReads ?? 0),
            cacheWriter?.Queued ?? 0,
            cacheWriter?.Written ?? 0,
            cacheWriter?.RejectedAtCapacity ?? 0,
            (cacheWriter?.Failed ?? 0) + (cache?.CorruptReads ?? 0) +
            (cache?.WriteFailures ?? 0));
    }

    private static double DistanceSquared((int X, int Z) key, Vector3D<double> point)
    {
        var dx = key.X * 16 + 8 - point.X;
        var dz = key.Z * 16 + 8 - point.Z;
        return dx * dx + dz * dz;
    }

    private (bool Present, bool Ready) NearState(
        (int X, int Z) key,
        double distanceSquared,
        int renderDistance,
        ChunkRenderer nearRenderer)
    {
        var nearDistance = Math.Max(0, renderDistance) * (double)SubChunkRenderer.Size;
        var nearPresent = distanceSquared < nearDistance * nearDistance &&
                          _world.BlockHost.HasChunk(key.X, key.Z) &&
                          _world.BlockHost.GetChunk(key.X, key.Z).Loaded;
        return (nearPresent,
            nearPresent && nearRenderer.IsMeshColumnReady(key.X, key.Z));
    }

    private TerrainLodHandoffTransition UpdateHandoff(
        ColumnPresentation presentation,
        bool translucent,
        bool nearPresent,
        bool nearReady,
        float deltaTime,
        bool fadeEnabled)
    {
        var before = presentation.HandoffFor(translucent);
        var after = presentation.UpdateHandoff(
            translucent, nearPresent, nearReady, deltaTime, fadeEnabled);
        _handoffsStarted += after.Started - before.Started;
        _handoffReversals += after.Reversals - before.Reversals;
        return after;
    }

    private TerrainLodLevelBlend UpdateLevelTransition(
        ColumnPresentation presentation,
        bool translucent,
        int requestedLevel,
        float deltaTime,
        bool fadeEnabled)
    {
        var before = presentation.LevelTransitionFor(translucent);
        var blend = presentation.UpdateLevelTransition(
            translucent, requestedLevel, deltaTime, fadeEnabled);
        var after = presentation.LevelTransitionFor(translucent);
        _levelTransitionsStarted += after.Started - before.Started;
        _levelTransitionReversals += after.Reversals - before.Reversals;
        return blend;
    }

    private static uint FadeSeed((int X, int Z) key) => unchecked(
        (uint)(key.X * 73_856_093 ^ key.Z * 19_349_663));

    private int AppendVisibleLevels(
        ColumnPresentation presentation,
        (int X, int Z) key,
        double distanceSquared,
        int requestedLevel,
        bool translucent,
        float nearHandoffProgress,
        float deltaTime,
        bool fadeEnabled)
    {
        if (!presentation.TryGetNearestLevel(
                requestedLevel, translucent, out var selectedLevel, out _)) return -1;

        // The exact/LOD handoff owns the single dither mask while it is active. Freeze a hierarchy
        // level transition during that short interval rather than trying to compose two masks.
        var blend = UpdateLevelTransition(presentation,
            translucent, selectedLevel,
            nearHandoffProgress > 0 ? 0 : deltaTime,
            fadeEnabled);
        presentation.LastPresentedTick = _tick;
        var seed = FadeSeed(key);

        if (nearHandoffProgress > 0)
        {
            Add(blend.DominantLevel, fadeMode: 2, nearHandoffProgress);
            return blend.DominantLevel;
        }

        if (blend.Active)
        {
            Add(blend.PrimaryLevel, fadeMode: 2, blend.Progress);
            Add(blend.SecondaryLevel, fadeMode: 1, blend.Progress);
            return blend.DominantLevel;
        }

        Add(blend.PrimaryLevel, fadeMode: 0, fadeProgress: 1);
        return blend.PrimaryLevel;

        void Add(int level, uint fadeMode, float fadeProgress)
        {
            if (!presentation.TryGetLevel(level, translucent, out var gpu)) return;
            _visible.Add(new VisibleColumn(
                key, distanceSquared, level, gpu, fadeProgress, fadeMode, seed));
        }
    }

    private static ChunkUniforms BuildUniforms(
        in ChunkRenderParams parameters,
        (int X, int Z) key,
        float fadeProgress,
        uint fadeMode,
        uint fadeSeed)
    {
        var relative = new Vector3D<float>(
            (float)(key.X * 16 - parameters.ViewPos.X),
            (float)(ChuckFormat.WorldHeight / 2.0 - parameters.ViewPos.Y),
            (float)(key.Z * 16 - parameters.ViewPos.Z));
        var modelView = Matrix4X4.CreateTranslation(relative) * parameters.ModelView;
        var fog = parameters.Fog;
        var light = RenderSystem.WorldLight;
        return new ChunkUniforms
        {
            ModelViewMatrix = modelView,
            ProjectionMatrix = WgpuClip.FromGl(parameters.Projection),
            ChunkPosX = key.X * 16,
            ChunkPosY = key.Z * 16,
            AmbientDarkness = light.AmbientDarkness,
            LuminanceOffset = light.LuminanceOffset,
            FogMode = (uint)fog.Curve,
            FogDensity = fog.Density,
            FogStart = fog.Start,
            FogEnd = fog.End,
            FogColorR = fog.Color.X,
            FogColorG = fog.Color.Y,
            FogColorB = fog.Color.Z,
            FogColorA = fog.Color.W,
            ChunkFadeEnabled = 0,
            FadeProgress = fadeProgress,
            PresentationFadeMode = fadeMode,
            PresentationFadeSeed = fadeSeed
        };
    }

    private readonly record struct PendingColumn(long DueTick);
    private readonly record struct VisibleColumn(
        (int X, int Z) Key,
        double DistanceSquared,
        int Level,
        GpuLevel Gpu,
        float FadeProgress,
        uint FadeMode,
        uint FadeSeed);

    private readonly record struct VisibleSeam(
        TerrainLodSeamKey Key,
        GpuSeam Gpu,
        double DistanceSquared,
        TerrainLodSeamFade Fade);

    private readonly record struct VisibleTranslucentDraw(
        VisibleColumn? Column,
        GpuSeam? Seam,
        (int X, int Z) Key,
        double DistanceSquared,
        TerrainLodSeamFade SeamFade = default);

    private readonly record struct TerrainLodSeamColumnState(
        int Level,
        float NearProgress,
        uint FadeSeed,
        bool Drawn);

    private readonly record struct TerrainLodSeamKey(
        (int X, int Z) Owner,
        Side BoundarySide,
        TerrainLodSeamMaterialSide MaterialSide)
    {
        public (int X, int Z) Neighbor => BoundarySide == global::OmniBlock.Blocks.Side.East
            ? (Owner.X + 1, Owner.Z)
            : (Owner.X, Owner.Z + 1);
    }

    private readonly record struct TerrainLodSeamSelection(
        TerrainLodBoundaryIdentity OwnerIdentity,
        TerrainLodBoundaryIdentity NeighborIdentity,
        int OwnerLevel,
        int NeighborLevel);

    private sealed class ColumnPresentation : IDisposable
    {
        private ColumnPresentation(
            long terrainRevision,
            Dictionary<int, GpuLevel> levels,
            TerrainLodBoundarySummary boundaries)
        {
            TerrainRevision = terrainRevision;
            Levels = levels;
            Boundaries = boundaries;
            MinimumLevel = levels.Count == 0 ? MinimumHorizonMeshLevel : levels.Keys.Min();
            MaximumLevel = levels.Count == 0 ? MinimumHorizonMeshLevel : levels.Keys.Max();
        }

        public long TerrainRevision { get; }
        public Dictionary<int, GpuLevel> Levels { get; }
        public TerrainLodBoundarySummary Boundaries { get; }
        public int MinimumLevel { get; }
        public int MaximumLevel { get; }
        public long LastPresentedTick { get; set; }
        public long EstimatedBytes => Levels.Values.Sum(static level => level.EstimatedBytes);
        private TerrainLodHandoffTransition SolidHandoff;
        private TerrainLodHandoffTransition TranslucentHandoff;
        private TerrainLodLevelTransition SolidLevelTransition;
        private TerrainLodLevelTransition TranslucentLevelTransition;
        public bool HasLevel(int level) => Levels.ContainsKey(level);
        public bool HasLayer(bool translucent) => Levels.Values.Any(level => translucent
            ? level.TranslucentMesh is not null
            : level.SolidMesh is not null);

        public TerrainLodHandoffTransition HandoffFor(bool translucent) =>
            translucent ? TranslucentHandoff : SolidHandoff;

        public int SelectionLevel(bool translucent) => translucent
            ? TranslucentLevelTransition.SelectionLevel
            : SolidLevelTransition.SelectionLevel;
        public bool HasActiveLevelTransition =>
            SolidLevelTransition.Active || TranslucentLevelTransition.Active;
        public TerrainLodLevelTransition LevelTransitionFor(bool translucent) =>
            translucent ? TranslucentLevelTransition : SolidLevelTransition;

        public TerrainLodLevelBlend UpdateLevelTransition(
            bool translucent,
            int requestedLevel,
            float deltaTime,
            bool fadeEnabled)
        {
            return translucent
                ? TranslucentLevelTransition.Update(requestedLevel, deltaTime, fadeEnabled)
                : SolidLevelTransition.Update(requestedLevel, deltaTime, fadeEnabled);
        }

        public TerrainLodHandoffTransition UpdateHandoff(
            bool translucent,
            bool nearPresent,
            bool nearReady,
            float deltaTime,
            bool fadeEnabled)
        {
            if (translucent)
            {
                TranslucentHandoff.Update(nearPresent, nearReady, deltaTime, fadeEnabled);
                return TranslucentHandoff;
            }

            SolidHandoff.Update(nearPresent, nearReady, deltaTime, fadeEnabled);
            return SolidHandoff;
        }

        public bool IsInState(TerrainLodHandoffState state) =>
            SolidHandoff.State == state || TranslucentHandoff.State == state;

        public void InitializeNearOnly()
        {
            SolidHandoff.InitializeNearOnly();
            TranslucentHandoff.InitializeNearOnly();
        }

        public void CopyHandoffsFrom(ColumnPresentation previous)
        {
            SolidHandoff.CopyFrom(previous.SolidHandoff);
            TranslucentHandoff.CopyFrom(previous.TranslucentHandoff);
            SolidLevelTransition.CopyFrom(previous.SolidLevelTransition);
            TranslucentLevelTransition.CopyFrom(previous.TranslucentLevelTransition);
        }

        public bool TryGetLevel(int level, bool translucent, out GpuLevel gpu)
        {
            if (!Levels.TryGetValue(level, out gpu!)) return false;
            return translucent ? gpu.TranslucentMesh is not null : gpu.SolidMesh is not null;
        }

        public bool TryGetNearestLevel(
            int requested,
            bool translucent,
            out int selected,
            out GpuLevel gpu)
        {
            if (Levels.TryGetValue(requested, out gpu!) && HasRequestedLayer(gpu))
            {
                selected = requested;
                return true;
            }

            foreach (var candidate in Levels.Keys
                         .OrderBy(level => Math.Abs(level - requested))
                         .ThenBy(level => level))
            {
                var candidateGpu = Levels[candidate];
                if (!HasRequestedLayer(candidateGpu)) continue;
                selected = candidate;
                gpu = candidateGpu;
                return true;
            }

            selected = -1;
            gpu = null!;
            return false;

            bool HasRequestedLayer(GpuLevel candidate) => translucent
                ? candidate.TranslucentMesh is not null
                : candidate.SolidMesh is not null;
        }

        public static ColumnPresentation Create(
            World world,
            TerrainLodConversionResult result,
            ILightProvider? lighting,
            int minimumLevel)
        {
            var device = WebGpuDevice.Current
                ?? throw new InvalidOperationException("Terrain LOD upload requires a WebGPU device.");
            Dictionary<int, GpuLevel> levels = [];
            try
            {
                var maximum = Math.Min(MaximumMeshLevel, result.Hierarchy.Levels.Count - 1);
                var minimum = Math.Clamp(
                    minimumLevel, ExactVoxelMeshLevel, MinimumHorizonMeshLevel);
                var boundaries = TerrainLodBoundarySummary.Capture(
                    result.Hierarchy, minimum, maximum);
                for (var level = minimum; level <= maximum; level++)
                {
                    var data = TerrainLodMeshBuilder.Build(
                        result.Hierarchy, level, world.Content.Blocks, !world.Dimension.HasCeiling,
                        lighting, world.Reader);
                    levels.Add(level, GpuLevel.Create(device, result.ChunkX, result.ChunkZ, data));
                }
                return new ColumnPresentation(result.TerrainRevision, levels, boundaries);
            }
            catch
            {
                foreach (var level in levels.Values) level.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var level in Levels.Values) level.Dispose();
            Levels.Clear();
        }
    }

    private sealed class GpuLevel : IDisposable
    {
        private GpuLevel(
            WgpuMesh? solidMesh,
            WgpuMesh? translucentMesh,
            SectionLighting? lighting,
            long estimatedBytes)
        {
            SolidMesh = solidMesh;
            TranslucentMesh = translucentMesh;
            Lighting = lighting;
            EstimatedBytes = estimatedBytes;
        }

        public WgpuMesh? SolidMesh { get; }
        public WgpuMesh? TranslucentMesh { get; }
        public SectionLighting? Lighting { get; }
        public long EstimatedBytes { get; }

        public static GpuLevel Create(
            WebGpuDevice device, int chunkX, int chunkZ, TerrainLodMeshData data)
        {
            if (data.Vertices.Length == 0 && data.TranslucentVertices.Length == 0)
                return new GpuLevel(null, null, null, 0);
            WgpuMesh? solidMesh = null;
            WgpuMesh? translucentMesh = null;
            SectionLighting? lighting = null;
            try
            {
                var origin = new Vector3D<int>(
                    chunkX * 16, ChuckFormat.WorldHeight / 2, chunkZ * 16);
                SectionLightModel? solidLight = null;
                SectionLightModel? translucentLight = null;
                if (data.Vertices.Length > 0)
                {
                    solidMesh = WgpuMesh.FromChunkQuads(device, data.Vertices);
                    solidLight = SectionLightModel.Create(origin, data.Vertices, data.Lights);
                }
                if (data.TranslucentVertices.Length > 0)
                {
                    translucentMesh = WgpuMesh.FromChunkQuads(device, data.TranslucentVertices);
                    translucentLight = SectionLightModel.Create(
                        origin, data.TranslucentVertices, data.TranslucentLights);
                }
                lighting = SectionLighting.CreateInitial(device, solidLight, translucentLight);
                return new GpuLevel(
                    solidMesh, translucentMesh, lighting, data.EstimatedBytes);
            }
            catch
            {
                solidMesh?.Dispose();
                translucentMesh?.Dispose();
                lighting?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            SolidMesh?.Dispose();
            TranslucentMesh?.Dispose();
            Lighting?.Dispose();
        }
    }

    private sealed class GpuSeam : IDisposable
    {
        private GpuSeam(
            TerrainLodSeamSelection selection,
            WgpuMesh? mesh,
            SectionLighting? lighting,
            long estimatedBytes)
        {
            Selection = selection;
            Mesh = mesh;
            Lighting = lighting;
            EstimatedBytes = estimatedBytes;
        }

        public TerrainLodSeamSelection Selection { get; }
        public WgpuMesh? Mesh { get; }
        public SectionLighting? Lighting { get; }
        public long EstimatedBytes { get; }

        public static GpuSeam Create(
            WebGpuDevice device,
            (int X, int Z) owner,
            TerrainLodSeamSelection selection,
            TerrainLodSeamMeshData data,
            bool translucent)
        {
            if (data.Vertices.Length == 0)
                return new GpuSeam(selection, null, null, 0);
            WgpuMesh? mesh = null;
            SectionLighting? lighting = null;
            try
            {
                mesh = WgpuMesh.FromChunkQuads(device, data.Vertices);
                var origin = new Vector3D<int>(
                    owner.X * 16, ChuckFormat.WorldHeight / 2, owner.Z * 16);
                var lightModel = SectionLightModel.Create(origin, data.Vertices, data.Lights);
                lighting = SectionLighting.CreateInitial(
                    device,
                    translucent ? null : lightModel,
                    translucent ? lightModel : null);
                return new GpuSeam(selection, mesh, lighting, data.EstimatedBytes);
            }
            catch
            {
                mesh?.Dispose();
                lighting?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Mesh?.Dispose();
            Lighting?.Dispose();
        }
    }
}

/// <summary>
///     Extends ordinary linear terrain fog across the distant horizon. Both exact and reduced
///     terrain consume this resolved state, so their overlap cannot reveal two different fog
///     depths for the same surface.
/// </summary>
internal static class TerrainLodFog
{
    public static FogState Resolve(
        FogState source,
        int renderDistance,
        int terrainHorizonDistance,
        int fogDistance)
    {
        if (source.Curve != FogCurve.Linear) return source;

        var nearDistance = Math.Max(1, renderDistance) * (float)SubChunkRenderer.Size;
        var horizon = Math.Max(renderDistance, terrainHorizonDistance) *
                      (float)SubChunkRenderer.Size;
        var end = Math.Clamp(
            Math.Max(1, fogDistance) * (float)SubChunkRenderer.Size,
            nearDistance, horizon);
        var start = Math.Clamp(source.Start, 0, end - 1);
        return source with { Start = start, End = end };
    }
}

internal static class TerrainLodAdmissionOrder
{
    /// <summary>
    ///     Missing coverage closest to the camera is admitted first. Coordinate tie-breakers make
    ///     equal-distance rings deterministic without introducing a directional preference.
    /// </summary>
    public static (int X, int Z)[] TakeNearest(
        IEnumerable<(int X, int Z)> coordinates,
        Vector3D<double> viewPosition,
        int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return coordinates
            .OrderBy(key => DistanceSquared(key, viewPosition))
            .ThenBy(key => key.X)
            .ThenBy(key => key.Z)
            .Take(count)
            .ToArray();
    }

    private static double DistanceSquared((int X, int Z) key, Vector3D<double> point)
    {
        var dx = key.X * 16 + 8 - point.X;
        var dz = key.Z * 16 + 8 - point.Z;
        return dx * dx + dz * dz;
    }
}

/// <summary>Distance thresholds with hysteresis so neighboring LOD levels do not flicker.</summary>
internal static class TerrainLodDetailSelector
{
    private const double TargetProjectedCellPixels = 5;
    private const double Hysteresis = 0.10;
    // Fine tiers are deliberately bounded even on high-resolution displays. They are transition
    // coverage, not a second full-resolution copy of the entire render distance.
    private const double MaximumExactVoxelDistance = 96;
    private const double MaximumTransitionDistance = 256;

    public static int SelectLevel(
        double distance,
        int maximumLevel,
        int previousLevel = -1,
        double verticalFovDegrees = 70,
        int viewportHeight = 480,
        double detailDropoffScale = 1)
    {
        if (!double.IsFinite(distance) || distance < 0)
            throw new ArgumentOutOfRangeException(nameof(distance));
        var maximum = Math.Clamp(maximumLevel, 0, 4);
        if (!double.IsFinite(verticalFovDegrees) || verticalFovDegrees is <= 1 or >= 179)
            verticalFovDegrees = 70;
        if (viewportHeight <= 0) viewportHeight = 480;
        if (!double.IsFinite(detailDropoffScale) || detailDropoffScale <= 0)
            detailDropoffScale = 1;

        var focalLength = viewportHeight /
                          (2 * Math.Tan(verticalFovDegrees * Math.PI / 360));
        var exactToTransition = Math.Min(
            2 * focalLength / TargetProjectedCellPixels, MaximumExactVoxelDistance) *
            detailDropoffScale;
        var transitionToFine = Math.Min(
            4 * focalLength / TargetProjectedCellPixels, MaximumTransitionDistance) *
            detailDropoffScale;
        var fineToMedium = 8 * focalLength / TargetProjectedCellPixels * detailDropoffScale;
        var mediumToCoarse = 16 * focalLength / TargetProjectedCellPixels * detailDropoffScale;
        var desired = distance < exactToTransition
            ? 0
            : distance < transitionToFine
                ? 1
                : distance < fineToMedium
                    ? 2
                    : distance < mediumToCoarse
                        ? 3
                        : 4;
        desired = Math.Min(desired, maximum);

        if (previousLevel == 0 && desired > 0 &&
            distance < exactToTransition * (1 + Hysteresis)) return 0;
        if (previousLevel == 1)
        {
            if (desired < 1 && distance > exactToTransition * (1 - Hysteresis)) return 1;
            if (desired > 1 && distance < transitionToFine * (1 + Hysteresis)) return 1;
        }
        if (previousLevel == 2)
        {
            if (desired < 2 && distance > transitionToFine * (1 - Hysteresis)) return 2;
            if (desired > 2 && distance < fineToMedium * (1 + Hysteresis)) return 2;
        }
        if (previousLevel == 3)
        {
            if (desired < 3 && distance > fineToMedium * (1 - Hysteresis)) return 3;
            if (desired > 3 && distance < mediumToCoarse * (1 + Hysteresis)) return 3;
        }
        if (previousLevel == 4 && desired < 4 &&
            distance > mediumToCoarse * (1 - Hysteresis)) return Math.Min(4, maximum);
        return desired;
    }
}

/// <summary>
///     Keeps a column within one level of an already-stable neighbor. If inherited state is itself
///     inconsistent (for example immediately after teleport), the midpoint is deterministic and
///     lets the local field converge without iteration-order bias.
/// </summary>
internal static class TerrainLodNeighborLevelConstraint
{
    public static int Constrain(int requestedLevel, ReadOnlySpan<int> neighborLevels)
    {
        if (neighborLevels.IsEmpty) return requestedLevel;
        var lower = int.MinValue;
        var upper = int.MaxValue;
        var minimum = int.MaxValue;
        var maximum = int.MinValue;
        foreach (var level in neighborLevels)
        {
            lower = Math.Max(lower, level - 1);
            upper = Math.Min(upper, level + 1);
            minimum = Math.Min(minimum, level);
            maximum = Math.Max(maximum, level);
        }

        if (lower <= upper) return Math.Clamp(requestedLevel, lower, upper);
        var midpointLow = (minimum + maximum) / 2;
        var midpointHigh = (minimum + maximum + 1) / 2;
        return Math.Abs(requestedLevel - midpointLow) <=
               Math.Abs(requestedLevel - midpointHigh)
            ? midpointLow
            : midpointHigh;
    }
}
