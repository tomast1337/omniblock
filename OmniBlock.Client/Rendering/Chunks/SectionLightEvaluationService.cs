using OmniBlock.Worlds.Core;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks;

internal sealed record SectionLightEvaluationRequest(
    Vector3D<int> Position,
    long SectionId,
    long Generation,
    SectionPresentationLightPlan Plan,
    WorldRegionSnapshot Snapshot);

internal sealed record SectionLightEvaluationResult(
    Vector3D<int> Position,
    long SectionId,
    long Generation,
    SectionPresentationLightEvaluation Evaluation,
    Exception? Failure = null);

/// <summary>
///     Bounded CPU-only light-probe evaluator. Live world state is copied before submission;
///     workers never read mutable chunks and never create or touch WebGPU resources.
/// </summary>
internal sealed class SectionLightEvaluationService : IDisposable
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Queue<SectionLightEvaluationRequest> _queued = [];
    private readonly Queue<SectionLightEvaluationResult> _completed = [];
    private readonly Thread _worker;
    private int _owned;
    private bool _disposed;

    public SectionLightEvaluationService(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "Section-Light"
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

    public bool TrySubmit(SectionLightEvaluationRequest request)
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

    public bool TryTakeCompleted(out SectionLightEvaluationResult? result)
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
            SectionLightEvaluationRequest request;
            lock (_gate)
            {
                while (!_disposed && _queued.Count == 0) Monitor.Wait(_gate);
                if (_disposed) return;
                request = _queued.Dequeue();
            }

            SectionLightEvaluationResult result;
            try
            {
                result = new SectionLightEvaluationResult(
                    request.Position,
                    request.SectionId,
                    request.Generation,
                    request.Plan.Evaluate(request.Snapshot));
            }
            catch (Exception error)
            {
                result = new SectionLightEvaluationResult(
                    request.Position,
                    request.SectionId,
                    request.Generation,
                    new SectionPresentationLightEvaluation(
                        request.Plan.PresentationEpoch,
                        new SectionLightingEvaluation?[request.Plan.Pages.Length]),
                    error);
            }
            finally
            {
                request.Snapshot.Dispose();
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
            while (_queued.TryDequeue(out var request)) request.Snapshot.Dispose();
            _completed.Clear();
            Monitor.PulseAll(_gate);
        }
        if (_worker != Thread.CurrentThread) _worker.Join(TimeSpan.FromSeconds(5));
    }
}
