using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Profiling;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Lod;
using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal readonly record struct ClientTerrainLodSnapshot(
    int PendingColumns,
    int ConversionOwnedColumns,
    int ResidentColumns,
    int PresentedColumns,
    int UploadsThisFrame,
    long ResidentGpuBytes,
    long StaleResults,
    long RejectedAdmissions,
    long Evictions);

/// <summary>
///     Client owner for the first terrain-horizon slice. It compiles immutable chunk snapshots on
///     the LOD worker and publishes opaque GPU presentations atomically on the render thread.
/// </summary>
/// <remarks>
///     A LOD presentation is coverage, never authority: it is drawn before the ordinary chunk
///     renderer and is hidden only after that renderer has a complete column presentation. An old
///     LOD mesh remains valid coverage while a newer revision is being built.
/// </remarks>
internal sealed class ClientTerrainLodRenderer : IDisposable
{
    public const float MaximumDistanceBlocks = 1024.0f;
    private const int ConversionCapacity = 16;
    private const int PendingCapacity = 4096;
    private const int ResidentCapacity = 2048;
    private const int SnapshotsPerTick = 4;
    private const int UploadsPerFrame = 2;
    private const int DrawsPerFrame = 768;
    private const int QuietTicks = 2;
    private const int MinimumMeshLevel = 2;
    private const int MaximumMeshLevel = 4;

    private readonly World _world;
    private readonly TerrainLodConversionService _conversion;
    private readonly Dictionary<(int X, int Z), PendingColumn> _pending = [];
    private readonly Dictionary<(int X, int Z), ColumnPresentation> _resident = [];
    private readonly List<VisibleColumn> _visible = [];
    private ChunkUniforms[] _uniforms = [];
    private WgpuPipeline? _pipeline;
    private long _tick;
    private long _staleResults;
    private long _rejectedAdmissions;
    private long _evictions;
    private bool _disposed;
    private ClientTerrainLodSnapshot _snapshot;

    public ClientTerrainLodRenderer(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _conversion = new TerrainLodConversionService(
            world.Dimension.Id,
            TerrainLodMaterialCatalog.FromRuntime(world.Content),
            TerrainLodReductionStrategy.SurfacePreserving,
            ConversionCapacity);
    }

    public ClientTerrainLodSnapshot Snapshot => _snapshot;

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

        var due = _pending
            .Where(pair => pair.Value.DueTick <= _tick)
            .OrderByDescending(pair => DistanceSquared(pair.Key, viewPosition))
            .ThenBy(pair => pair.Key.X)
            .ThenBy(pair => pair.Key.Z)
            .Take(SnapshotsPerTick)
            .Select(pair => pair.Key)
            .ToArray();

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

            var result = _conversion.Submit(TerrainLodSourceSnapshot.Capture(chunk));
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
        var uploads = InstallCompleted(UploadsPerFrame);

        if (RenderSystem.Fog.Curve != FogCurve.Linear ||
            RenderSystem.DrawTargetOrNull is not WebGpuDrawTarget target ||
            target.CurrentPass is null || target.TerrainArray is not { } terrainArray ||
            WebGpuDevice.Current is not { } device)
        {
            PublishSnapshot(uploads, 0);
            return;
        }

        _visible.Clear();
        var maximumDistanceSquared = MaximumDistanceBlocks * MaximumDistanceBlocks;
        foreach (var (key, presentation) in _resident)
        {
            var distanceSquared = DistanceSquared(key, parameters.ViewPos);
            if (distanceSquared > maximumDistanceSquared ||
                !parameters.Camera.IsBoundingBoxInFrustum(new Box(
                    key.X * 16, 0, key.Z * 16,
                    key.X * 16 + 16, ChuckFormat.WorldHeight, key.Z * 16 + 16)))
                continue;

            var nearComplete = _world.BlockHost.HasChunk(key.X, key.Z) &&
                               _world.BlockHost.GetChunk(key.X, key.Z).Loaded &&
                               nearRenderer.IsMeshColumnReady(key.X, key.Z);
            if (nearComplete) continue;

            var level = TerrainLodDetailSelector.SelectLevel(
                Math.Sqrt(distanceSquared), presentation.MaximumLevel, presentation.LastLevel);
            if (!presentation.Levels.TryGetValue(level, out var gpu) || gpu.Mesh is null) continue;
            presentation.LastLevel = level;
            presentation.LastPresentedTick = _tick;
            _visible.Add(new VisibleColumn(key, distanceSquared, gpu));
        }

        _visible.Sort(static (a, b) =>
        {
            var distance = a.DistanceSquared.CompareTo(b.DistanceSquared);
            if (distance != 0) return distance;
            var x = a.Key.X.CompareTo(b.Key.X);
            return x != 0 ? x : a.Key.Z.CompareTo(b.Key.Z);
        });
        if (_visible.Count > DrawsPerFrame)
            _visible.RemoveRange(DrawsPerFrame, _visible.Count - DrawsPerFrame);
        if (_visible.Count == 0)
        {
            PublishSnapshot(uploads, 0);
            return;
        }

        _pipeline ??= ChunkRenderer.CreateWgpuPipeline(device, RenderState.Opaque);
        _pipeline.Bind(target.CurrentPass);
        WgpuPipeline.BindGroup(target.CurrentPass, 1,
            terrainArray.BindGroupFor(_pipeline.TextureBindGroupLayout), device.Api);

        if (_uniforms.Length < _visible.Count) _uniforms = new ChunkUniforms[_visible.Count];
        for (var i = 0; i < _visible.Count; i++)
            _uniforms[i] = BuildUniforms(parameters, _visible[i].Key);
        _pipeline.WriteDynamicUniforms(_uniforms.AsSpan(0, _visible.Count));

        for (var i = 0; i < _visible.Count; i++)
        {
            _pipeline.BindDynamicUniforms(target.CurrentPass, i);
            var gpu = _visible[i].Gpu;
            gpu.Mesh!.Draw(target.CurrentPass, lightBuffer: gpu.Lighting!.Solid);
        }

        PublishSnapshot(uploads, _visible.Count);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _conversion.Dispose();
        foreach (var presentation in _resident.Values) presentation.Dispose();
        _resident.Clear();
        _pending.Clear();
        _pipeline?.Dispose();
        _pipeline = null;
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

    private int InstallCompleted(int budget)
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
                candidate = ColumnPresentation.Create(_world, result);
                if (_resident.Remove(key, out var old)) old.Dispose();
                _resident.Add(key, candidate);
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

    private void EvictDistant(Vector3D<double> viewPosition)
    {
        if (_resident.Count <= ResidentCapacity) return;
        var remove = _resident
            .OrderByDescending(pair => DistanceSquared(pair.Key, viewPosition))
            .ThenBy(pair => pair.Value.LastPresentedTick)
            .Take(_resident.Count - ResidentCapacity)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in remove)
        {
            if (_resident.Remove(key, out var presentation)) presentation.Dispose();
            _evictions++;
        }
    }

    private void PublishSnapshot(int uploads, int draws)
    {
        var conversion = _conversion.Snapshot();
        _snapshot = new ClientTerrainLodSnapshot(
            _pending.Count,
            conversion.OwnedChunks,
            _resident.Count,
            draws,
            uploads,
            _resident.Values.Sum(static value => value.EstimatedBytes),
            _staleResults,
            _rejectedAdmissions,
            _evictions);
    }

    private static double DistanceSquared((int X, int Z) key, Vector3D<double> point)
    {
        var dx = key.X * 16 + 8 - point.X;
        var dz = key.Z * 16 + 8 - point.Z;
        return dx * dx + dz * dz;
    }

    private static ChunkUniforms BuildUniforms(in ChunkRenderParams parameters, (int X, int Z) key)
    {
        var relative = new Vector3D<float>(
            (float)(key.X * 16 - parameters.ViewPos.X),
            (float)(ChuckFormat.WorldHeight / 2.0 - parameters.ViewPos.Y),
            (float)(key.Z * 16 - parameters.ViewPos.Z));
        var modelView = Matrix4X4.CreateTranslation(relative) * parameters.ModelView;
        var fog = RenderSystem.Fog;
        var light = RenderSystem.WorldLight;
        return new ChunkUniforms
        {
            ModelViewMatrix = modelView,
            ProjectionMatrix = WgpuClip.FromGl(parameters.Projection),
            ChunkPosX = key.X * 16,
            ChunkPosY = key.Z * 16,
            AmbientDarkness = light.AmbientDarkness,
            LuminanceOffset = light.LuminanceOffset,
            FogMode = (uint)FogCurve.Linear,
            FogDensity = fog.Density,
            FogStart = Math.Max(fog.Start, parameters.RenderDistance * 16 * 0.75f),
            FogEnd = MaximumDistanceBlocks,
            FogColorR = fog.Color.X,
            FogColorG = fog.Color.Y,
            FogColorB = fog.Color.Z,
            FogColorA = fog.Color.W,
            ChunkFadeEnabled = 0,
            FadeProgress = 1
        };
    }

    private readonly record struct PendingColumn(long DueTick);
    private readonly record struct VisibleColumn(
        (int X, int Z) Key,
        double DistanceSquared,
        GpuLevel Gpu);

    private sealed class ColumnPresentation : IDisposable
    {
        private ColumnPresentation(long terrainRevision, Dictionary<int, GpuLevel> levels)
        {
            TerrainRevision = terrainRevision;
            Levels = levels;
            MaximumLevel = levels.Count == 0 ? MinimumMeshLevel : levels.Keys.Max();
        }

        public long TerrainRevision { get; }
        public Dictionary<int, GpuLevel> Levels { get; }
        public int MaximumLevel { get; }
        public int LastLevel { get; set; } = -1;
        public long LastPresentedTick { get; set; }
        public long EstimatedBytes => Levels.Values.Sum(static level => level.EstimatedBytes);

        public static ColumnPresentation Create(World world, TerrainLodConversionResult result)
        {
            var device = WebGpuDevice.Current
                ?? throw new InvalidOperationException("Terrain LOD upload requires a WebGPU device.");
            Dictionary<int, GpuLevel> levels = [];
            try
            {
                var maximum = Math.Min(MaximumMeshLevel, result.Hierarchy.Levels.Count - 1);
                for (var level = MinimumMeshLevel; level <= maximum; level++)
                {
                    var data = TerrainLodMeshBuilder.Build(
                        result.Hierarchy, level, world.Content.Blocks, !world.Dimension.HasCeiling);
                    levels.Add(level, GpuLevel.Create(device, result.ChunkX, result.ChunkZ, data));
                }
                return new ColumnPresentation(result.TerrainRevision, levels);
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
        private GpuLevel(WgpuMesh? mesh, SectionLighting? lighting, long estimatedBytes)
        {
            Mesh = mesh;
            Lighting = lighting;
            EstimatedBytes = estimatedBytes;
        }

        public WgpuMesh? Mesh { get; }
        public SectionLighting? Lighting { get; }
        public long EstimatedBytes { get; }

        public static GpuLevel Create(
            WebGpuDevice device, int chunkX, int chunkZ, TerrainLodMeshData data)
        {
            if (data.Vertices.Length == 0) return new GpuLevel(null, null, 0);
            WgpuMesh? mesh = null;
            SectionLighting? lighting = null;
            try
            {
                mesh = WgpuMesh.FromChunkQuads(device, data.Vertices);
                var model = SectionLightModel.Create(
                    new Vector3D<int>(chunkX * 16, ChuckFormat.WorldHeight / 2, chunkZ * 16),
                    data.Vertices, data.Lights);
                lighting = SectionLighting.CreateInitial(device, model, null);
                return new GpuLevel(mesh, lighting, data.EstimatedBytes);
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

/// <summary>Distance thresholds with hysteresis so neighboring LOD levels do not flicker.</summary>
internal static class TerrainLodDetailSelector
{
    private const double FineToMedium = 256;
    private const double MediumToCoarse = 512;
    private const double Hysteresis = 0.10;

    public static int SelectLevel(double distance, int maximumLevel, int previousLevel = -1)
    {
        if (!double.IsFinite(distance) || distance < 0)
            throw new ArgumentOutOfRangeException(nameof(distance));
        var maximum = Math.Clamp(maximumLevel, 2, 4);
        var desired = distance < FineToMedium ? 2 : distance < MediumToCoarse ? 3 : 4;
        desired = Math.Min(desired, maximum);

        if (previousLevel == 2 && desired > 2 && distance < FineToMedium * (1 + Hysteresis))
            return 2;
        if (previousLevel == 3)
        {
            if (desired < 3 && distance > FineToMedium * (1 - Hysteresis)) return 3;
            if (desired > 3 && distance < MediumToCoarse * (1 + Hysteresis)) return 3;
        }
        if (previousLevel == 4 && desired < 4 &&
            distance > MediumToCoarse * (1 - Hysteresis)) return Math.Min(4, maximum);
        return desired;
    }
}
