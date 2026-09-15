using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal sealed record TerrainLodMeshCompilationRequest(
    TerrainLodConversionResult Conversion,
    int MinimumLevel,
    int MaximumLevel,
    WorldRegionSnapshot Visuals,
    bool HasSkyLight);

internal sealed record TerrainLodMeshCompilationResult(
    TerrainLodConversionResult Conversion,
    int MinimumLevel,
    TerrainLodBoundarySummary? Boundaries,
    TerrainLodMeshData[] Levels,
    Exception? Failure = null);

/// <summary>
///     Bounded CPU-only stage between hierarchy reduction/cache loading and render-thread GPU
///     installation. The request owns its immutable world snapshot until the worker finishes.
/// </summary>
internal sealed class TerrainLodMeshCompilationService : IDisposable
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Queue<TerrainLodMeshCompilationRequest> _queued = [];
    private readonly Queue<TerrainLodMeshCompilationResult> _completed = [];
    private readonly Thread _worker;
    private int _owned;
    private bool _disposed;

    public TerrainLodMeshCompilationService(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "TerrainLOD-Mesh"
        };
        _worker.Start();
    }

    public bool HasCapacity
    {
        get
        {
            lock (_gate) return !_disposed && _owned < _capacity;
        }
    }

    public bool TrySubmit(TerrainLodMeshCompilationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_owned >= _capacity) return false;
            _owned++;
            _queued.Enqueue(request);
            Monitor.Pulse(_gate);
            return true;
        }
    }

    public bool TryTakeCompleted(out TerrainLodMeshCompilationResult? result)
    {
        lock (_gate)
        {
            if (_completed.Count == 0)
            {
                result = null;
                return false;
            }

            result = _completed.Dequeue();
            _owned--;
            return true;
        }
    }

    private void WorkerLoop()
    {
        while (true)
        {
            TerrainLodMeshCompilationRequest request;
            lock (_gate)
            {
                while (!_disposed && _queued.Count == 0) Monitor.Wait(_gate);
                if (_disposed) return;
                request = _queued.Dequeue();
            }

            TerrainLodMeshCompilationResult result;
            try
            {
                var hierarchy = request.Conversion.Hierarchy;
                var maximum = Math.Min(request.MaximumLevel, hierarchy.Levels.Count - 1);
                var minimum = Math.Clamp(request.MinimumLevel, 0, maximum);
                var boundaries = TerrainLodBoundarySummary.Capture(hierarchy, minimum, maximum);
                var levels = new TerrainLodMeshData[maximum - minimum + 1];
                for (var level = minimum; level <= maximum; level++)
                {
                    levels[level - minimum] = TerrainLodMeshBuilder.Build(
                        hierarchy,
                        level,
                        request.Visuals.ContentBlocks,
                        request.HasSkyLight,
                        request.Conversion.Lighting,
                        request.Visuals);
                }
                result = new TerrainLodMeshCompilationResult(
                    request.Conversion, minimum, boundaries, levels);
            }
            catch (Exception error)
            {
                result = new TerrainLodMeshCompilationResult(
                    request.Conversion, request.MinimumLevel, null, [], error);
            }
            finally
            {
                request.Visuals.Dispose();
            }

            lock (_gate)
            {
                if (_disposed) return;
                _completed.Enqueue(result);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            while (_queued.TryDequeue(out var request)) request.Visuals.Dispose();
            _completed.Clear();
            Monitor.PulseAll(_gate);
        }
        if (_worker != Thread.CurrentThread) _worker.Join(TimeSpan.FromSeconds(5));
    }
}
