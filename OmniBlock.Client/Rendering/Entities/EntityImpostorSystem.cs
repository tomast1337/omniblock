using System.Numerics;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Core.WebGPU;
using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>
/// Provider-keyed owner for bounded atlas residency. Providers describe capture inputs; entries own
/// their GPU/cache lifecycle. The shared portable memory LRU prevents the per-provider multiplication
/// that the original cow prototype would have caused.
/// </summary>
internal sealed unsafe class EntityImpostorSystem : IDisposable
{
    private const int MaxResidentAtlases = 8;
    private readonly EntityImpostorMemoryCache _memory = new();
    private readonly Dictionary<ResourceLocation, Entry> _entries = [];
    private long _frame;
    private int _captureCursor;
    private bool _holdReadback, _holdCapture;
    private int _failures, _replacements, _memoryHits, _diskHits, _cacheMisses, _cacheWrites,
        _cacheErrors, _cancellations, _staleResults, _capturedViews, _invalidations, _completedBakes;
    private double _totalBakeLatencyMs, _captureCpuMs;

    private sealed class Entry(IEntityImpostorProvider provider, EntityImpostorMemoryCache memory)
    {
        public readonly EntityImpostorAtlas Atlas = new(provider, memory);
        public long LastUsed;
    }

    public bool Enabled { get; set; }
    public bool ForceTierForTest { get; set; }
    public int CompletedViews => _entries.Values.Sum(e => e.Atlas.CompletedViews);
    public bool Ready => _entries.Values.Any(e => e.Atlas.Ready);
    public int LastSubmitted { get; private set; }
    public int Failures => _failures + _entries.Values.Sum(e => e.Atlas.Failures);
    public int Replacements => _replacements + _entries.Values.Sum(e => e.Atlas.Replacements);
    public int PendingFallbacks => _entries.Values.Sum(e => e.Atlas.PendingFallbacks);
    public int LastPoseMask => _entries.Values.Aggregate(0, (mask, e) => mask | e.Atlas.LastPoseMask);
    public int LastHurtSubmitted => _entries.Values.Sum(e => e.Atlas.LastHurtSubmitted);
    public int LastOverlaySubmitted => _entries.Values.Sum(e => e.Atlas.LastOverlaySubmitted);
    public int MemoryHits => _memoryHits + _entries.Values.Sum(e => e.Atlas.MemoryHits);
    public int DiskHits => _diskHits + _entries.Values.Sum(e => e.Atlas.DiskHits);
    public int CacheMisses => _cacheMisses + _entries.Values.Sum(e => e.Atlas.CacheMisses);
    public int CacheWrites => _cacheWrites + _entries.Values.Sum(e => e.Atlas.CacheWrites);
    public int CacheErrors => _cacheErrors + _entries.Values.Sum(e => e.Atlas.CacheErrors);
    public int Cancellations => _cancellations + _entries.Values.Sum(e => e.Atlas.Cancellations);
    public int StaleResults => _staleResults + _entries.Values.Sum(e => e.Atlas.StaleResults);
    public int CapturedViews => _capturedViews + _entries.Values.Sum(e => e.Atlas.CapturedViews);
    public int Invalidations => _invalidations + _entries.Values.Sum(e => e.Atlas.Invalidations);
    public double OldestBakeQueueAgeMs => _entries.Values.Select(e => e.Atlas.BakeQueueAgeMs).DefaultIfEmpty().Max();
    public double LastBakeLatencyMs => _entries.Values.Select(e => e.Atlas.LastBakeLatencyMs).DefaultIfEmpty().Max();
    public double AverageBakeLatencyMs
    {
        get
        {
            var count = _completedBakes + _entries.Values.Sum(e => e.Atlas.CompletedBakes);
            var total = _totalBakeLatencyMs + _entries.Values.Sum(e => e.Atlas.TotalBakeLatencyMs);
            return count == 0 ? 0 : total / count;
        }
    }
    public double CaptureCpuMs => _captureCpuMs + _entries.Values.Sum(e => e.Atlas.CaptureCpuMs);
    public long ResidentGpuBytes => _entries.Values.Sum(e => e.Atlas.ResidentGpuBytes);
    public long StagingBytes => _entries.Values.Sum(e => e.Atlas.StagingBytes);
    public int LastDrawBatches => _entries.Values.Sum(e => e.Atlas.LastDrawBatches);
    public long MemoryBytes => _memory.Bytes;
    public bool ReadbackPending => _entries.Values.Any(e => e.Atlas.ReadbackPending);
    internal int ResidentAtlasCount => _entries.Count;

    internal bool HoldReadbackForTest
    {
        get => _holdReadback;
        set { _holdReadback = value; foreach (var entry in _entries.Values) entry.Atlas.HoldReadbackForTest = value; }
    }

    internal bool HoldCaptureForTest
    {
        get => _holdCapture;
        set { _holdCapture = value; foreach (var entry in _entries.Values) entry.Atlas.HoldCaptureForTest = value; }
    }

    public void Prepare(WebGpuDevice device, CommandEncoder* encoder, TextureManager textures, string gameDataDirectory)
    {
        _frame++;
        // Pump all atlas map callbacks once. Polling from every atlas multiplied one native
        // wgpuDevicePoll call by the resident provider count, even on frames with no readback.
        if (WgpuAtlasReadback.PendingCallbacks != 0) device.PollNonBlocking();
        var ordered = _entries.OrderBy(e => e.Key.ToString(), StringComparer.Ordinal).Select(e => e.Value).ToArray();
        Entry? scheduled = null;
        if (ordered.Length != 0)
        {
            for (var offset = 0; offset < ordered.Length; offset++)
            {
                var candidate = ordered[(_captureCursor + offset) % ordered.Length];
                if (!candidate.Atlas.CapturePending) continue;
                scheduled = candidate;
                _captureCursor = (_captureCursor + offset + 1) % ordered.Length;
                break;
            }
        }
        foreach (var entry in ordered)
        {
            entry.Atlas.Enabled = Enabled;
            entry.Atlas.Prepare(device, encoder, textures, gameDataDirectory, ReferenceEquals(entry, scheduled));
        }
    }

    public bool TrySubmit(IEntityLodProvider? provider, EntityLodSelector.Decision decision,
        Vector3 cameraRelativePosition, float yaw, float light, int pose, bool hurt, Vector4 layerEffects)
    {
        if (!Enabled || provider is not IEntityImpostorProvider impostor) return false;
        if (!_entries.TryGetValue(impostor.Id, out var entry))
        {
            if (_entries.Count >= MaxResidentAtlases)
            {
                var victim = _entries.OrderBy(pair => pair.Value.LastUsed)
                    .ThenBy(pair => pair.Key.ToString(), StringComparer.Ordinal).First();
                Accumulate(victim.Value.Atlas);
                victim.Value.Atlas.Dispose();
                _entries.Remove(victim.Key);
            }
            entry = new Entry(impostor, _memory);
            entry.Atlas.Enabled = true;
            entry.Atlas.HoldCaptureForTest = _holdCapture;
            entry.Atlas.HoldReadbackForTest = _holdReadback;
            _entries.Add(impostor.Id, entry);
        }
        entry.LastUsed = _frame;
        return entry.Atlas.TrySubmit(decision, cameraRelativePosition, yaw, light, pose, hurt, layerEffects);
    }

    public void Draw()
    {
        LastSubmitted = 0;
        foreach (var entry in _entries.OrderBy(e => e.Key.ToString(), StringComparer.Ordinal))
        {
            entry.Value.Atlas.Draw();
            LastSubmitted += entry.Value.Atlas.LastSubmitted;
        }
    }

    public void AfterSubmit(WebGpuDevice device)
    {
        foreach (var entry in _entries.Values) entry.Atlas.AfterSubmit(device);
    }

    public void Reset()
    {
        LastSubmitted = 0;
        foreach (var entry in _entries.Values) entry.Atlas.Reset();
    }

    internal void ClearMemoryForTest()
    {
        Reset();
        _memory.Clear();
    }

    internal void RecreateGpuForTest()
    {
        foreach (var entry in _entries.Values) entry.Atlas.Dispose();
        _memory.Clear();
        LastSubmitted = 0;
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values) entry.Atlas.Dispose();
        _entries.Clear();
        _memory.Clear();
        LastSubmitted = 0;
    }

    private void Accumulate(EntityImpostorAtlas atlas)
    {
        _failures += atlas.Failures; _replacements += atlas.Replacements;
        _memoryHits += atlas.MemoryHits; _diskHits += atlas.DiskHits;
        _cacheMisses += atlas.CacheMisses; _cacheWrites += atlas.CacheWrites;
        _cacheErrors += atlas.CacheErrors; _cancellations += atlas.Cancellations;
        _staleResults += atlas.StaleResults; _capturedViews += atlas.CapturedViews;
        _invalidations += atlas.Invalidations; _completedBakes += atlas.CompletedBakes;
        _totalBakeLatencyMs += atlas.TotalBakeLatencyMs; _captureCpuMs += atlas.CaptureCpuMs;
    }
}
