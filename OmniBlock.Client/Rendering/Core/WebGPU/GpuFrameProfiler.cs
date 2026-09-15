using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using OmniBlock.Profiling;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>The stable, coarse GPU buckets available without splitting the existing render passes.</summary>
internal enum GpuPassCategory
{
    World,
    EntityImpostorCapture,
    FirstPersonHand,
    Interface,
    Composite
}

internal readonly record struct GpuPassTiming(
    GpuPassCategory Category,
    int PhysicalPasses,
    ulong RawTicks,
    double? Milliseconds);

internal readonly record struct GpuFrameTimingSnapshot(
    long Frame,
    string Status,
    double? TimestampPeriodNanoseconds,
    uint Width,
    uint Height,
    ulong RenderSpanRawTicks,
    double? RenderSpanMilliseconds,
    GpuPassTiming World,
    GpuPassTiming EntityImpostorCapture,
    GpuPassTiming FirstPersonHand,
    GpuPassTiming Interface,
    GpuPassTiming Composite)
{
    public GpuPassTiming Get(GpuPassCategory category) => category switch
    {
        GpuPassCategory.World => World,
        GpuPassCategory.EntityImpostorCapture => EntityImpostorCapture,
        GpuPassCategory.FirstPersonHand => FirstPersonHand,
        GpuPassCategory.Interface => Interface,
        GpuPassCategory.Composite => Composite,
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}

/// <summary>
///     Records render-pass timestamps into a bounded multi-frame ring and maps old results without
///     ever waiting for the current frame. Query support is optional; unsupported adapters keep the
///     same rendering path and expose an explicit diagnostic status.
/// </summary>
internal sealed unsafe class GpuFrameProfiler : IDisposable
{
    internal const int MaximumPassesPerFrame = 32;
    private const int RingSize = 4;
    private const int QueryCount = MaximumPassesPerFrame * 2 + 2;
    private const ulong QueryBufferBytes = QueryCount * sizeof(ulong);
    private const string TimestampPeriodOverride = "OMNIBLOCK_GPU_TIMESTAMP_PERIOD_NS";

    private readonly WebGpuDevice _device;
    private readonly Slot[] _slots = new Slot[RingSize];
    private readonly double? _timestampPeriodNanoseconds;
    private readonly string _status;
    private Slot? _recording;
    private long _nextFrame;
    private int _droppedFrames;
    private bool _disposed;

    internal GpuFrameProfiler(WebGpuDevice device, bool timestampQueriesEnabled)
    {
        _device = device;

        if (!timestampQueriesEnabled)
        {
            _status = "unsupported: adapter/device does not expose timestamp-query";
            Latest = EmptySnapshot(_status);
            return;
        }

        _timestampPeriodNanoseconds = ResolveTimestampPeriod(device, out var periodStatus);
        _status = periodStatus;
        Latest = EmptySnapshot(_status, _timestampPeriodNanoseconds);

        for (var i = 0; i < _slots.Length; i++) _slots[i] = new Slot(device);
    }

    public bool Supported => _slots[0] is not null;
    public GpuFrameTimingSnapshot Latest { get; private set; }
    public int DroppedFrames => _droppedFrames;

    /// <summary>Collect old mappings, publish their values, then reserve an idle ring slot.</summary>
    public void BeginFrame(CommandEncoder* encoder, uint width, uint height)
    {
        if (_disposed || !Supported) return;

        CollectCompleted();
        _recording = null;
        foreach (var slot in _slots)
        {
            if (slot.State != SlotState.Idle) continue;
            _recording = slot;
            break;
        }
        if (_recording is null)
        {
            _droppedFrames++;
            return;
        }

        _recording.Begin(++_nextFrame, width, height);
        _recording.FrameBeginningIndex = _recording.NextQuery++;
        _recording.FrameEndIndex = _recording.NextQuery++;
        _device.Api.CommandEncoderWriteTimestamp(
            encoder, _recording.QuerySet, _recording.FrameBeginningIndex);
    }

    /// <summary>
    ///     Allocates one beginning/end pair for a physical render pass. Multiple physical passes
    ///     may use one category; their durations are summed after readback (cloud blur does this for
    ///     <see cref="GpuPassCategory.World" />).
    /// </summary>
    public bool TryCreatePassWrites(GpuPassCategory category, out RenderPassTimestampWrites writes)
    {
        writes = default;
        if (_recording is not { State: SlotState.Encoding } slot || slot.RangeCount >= MaximumPassesPerFrame)
            return false;

        if (slot.NextQuery + 2 > QueryCount) return false;
        var beginning = slot.NextQuery++;
        var end = slot.NextQuery++;
        slot.Ranges[slot.RangeCount++] = new QueryRange(category, beginning, end);
        writes = new RenderPassTimestampWrites
        {
            QuerySet = slot.QuerySet,
            BeginningOfPassWriteIndex = beginning,
            EndOfPassWriteIndex = end
        };
        return true;
    }

    /// <summary>Resolve this frame's used queries and copy them into its persistently-owned map buffer.</summary>
    public void Resolve(CommandEncoder* encoder)
    {
        if (_recording is not { State: SlotState.Encoding } slot) return;

        if (slot.NextQuery == 0)
        {
            slot.ResetWithoutMap();
            _recording = null;
            return;
        }

        _device.Api.CommandEncoderWriteTimestamp(encoder, slot.QuerySet, slot.FrameEndIndex);
        var queryCount = slot.NextQuery;
        var byteCount = (ulong)queryCount * sizeof(ulong);
        _device.Api.CommandEncoderResolveQuerySet(encoder, slot.QuerySet, 0, queryCount, slot.ResolveBuffer, 0);
        _device.Api.CommandEncoderCopyBufferToBuffer(
            encoder, slot.ResolveBuffer, 0, slot.ReadbackBuffer, 0, byteCount);
        slot.State = SlotState.AwaitingSubmit;
    }

    /// <summary>Starts asynchronous mapping only after the command buffer containing the copy was submitted.</summary>
    public void AfterSubmit()
    {
        if (_recording is not { State: SlotState.AwaitingSubmit } slot)
        {
            _recording = null;
            return;
        }

        slot.StartMap();
        _recording = null;
    }

    /// <summary>Feeds the most recently completed (therefore several frames delayed) GPU values into the CPU profiler tree.</summary>
    public void RecordLatestToProfiler()
    {
        var latest = Latest;
        if (latest.RenderSpanMilliseconds is not { } total) return;

        Profiler.Record("GpuRenderSpan", total);
        Record(latest.World);
        Record(latest.EntityImpostorCapture);
        Record(latest.FirstPersonHand);
        Record(latest.Interface);
        Record(latest.Composite);

        static void Record(GpuPassTiming pass)
        {
            if (pass.Milliseconds is { } milliseconds)
                Profiler.Record($"Gpu{pass.Category}", milliseconds);
        }
    }

    public string CreateTsvSnapshot()
    {
        var latest = Latest;
        var text = new StringBuilder(512);
        text.Append("status\t").AppendLine(latest.Status);
        text.Append("frame\t").AppendLine(latest.Frame.ToString(CultureInfo.InvariantCulture));
        text.Append("timestampPeriodNs\t")
            .AppendLine(latest.TimestampPeriodNanoseconds?.ToString("F9", CultureInfo.InvariantCulture) ?? "unavailable");
        text.Append("resolution\t").Append(latest.Width).Append('x')
            .AppendLine(latest.Height.ToString(CultureInfo.InvariantCulture));
        text.Append("droppedFrames\t").AppendLine(_droppedFrames.ToString(CultureInfo.InvariantCulture));
        text.AppendLine("scope\tphysicalPasses\trawTicks\tmilliseconds");
        Append("RenderSpan", 1, latest.RenderSpanRawTicks, latest.RenderSpanMilliseconds);
        foreach (var category in Enum.GetValues<GpuPassCategory>())
        {
            var pass = latest.Get(category);
            Append(pass.Category.ToString(), pass.PhysicalPasses, pass.RawTicks, pass.Milliseconds);
        }
        return text.ToString();

        void Append(string name, int physicalPasses, ulong ticks, double? milliseconds)
        {
            text.Append(name).Append('\t').Append(physicalPasses).Append('\t')
                .Append(ticks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .AppendLine(milliseconds?.ToString("F6", CultureInfo.InvariantCulture) ?? "unavailable");
        }
    }

    internal static GpuFrameTimingSnapshot BuildSnapshot(
        long frame,
        string status,
        double? timestampPeriodNanoseconds,
        ReadOnlySpan<ulong> queryValues,
        ReadOnlySpan<QueryRange> ranges,
        uint frameBeginningIndex = uint.MaxValue,
        uint frameEndIndex = uint.MaxValue,
        uint width = 0,
        uint height = 0)
    {
        Span<ulong> totals = stackalloc ulong[5];
        Span<int> counts = stackalloc int[5];
        ulong first = ulong.MaxValue;
        ulong last = 0;

        foreach (var range in ranges)
        {
            if (range.EndIndex >= queryValues.Length) continue;
            var beginning = queryValues[(int)range.BeginningIndex];
            var end = queryValues[(int)range.EndIndex];
            if (end < beginning) continue;
            totals[(int)range.Category] += end - beginning;
            counts[(int)range.Category]++;
            first = Math.Min(first, beginning);
            last = Math.Max(last, end);
        }

        var explicitFrameSpan = frameBeginningIndex < queryValues.Length &&
                                frameEndIndex < queryValues.Length &&
                                queryValues[(int)frameEndIndex] >= queryValues[(int)frameBeginningIndex];
        var span = explicitFrameSpan
            ? queryValues[(int)frameEndIndex] - queryValues[(int)frameBeginningIndex]
            : first == ulong.MaxValue || last < first ? 0 : last - first;
        return new GpuFrameTimingSnapshot(
            frame,
            status,
            timestampPeriodNanoseconds,
            width,
            height,
            span,
            ToMilliseconds(span, timestampPeriodNanoseconds),
            new(GpuPassCategory.World, counts[(int)GpuPassCategory.World], totals[(int)GpuPassCategory.World],
                ToMilliseconds(totals[(int)GpuPassCategory.World], timestampPeriodNanoseconds)),
            new(GpuPassCategory.EntityImpostorCapture, counts[(int)GpuPassCategory.EntityImpostorCapture],
                totals[(int)GpuPassCategory.EntityImpostorCapture],
                ToMilliseconds(totals[(int)GpuPassCategory.EntityImpostorCapture], timestampPeriodNanoseconds)),
            new(GpuPassCategory.FirstPersonHand, counts[(int)GpuPassCategory.FirstPersonHand],
                totals[(int)GpuPassCategory.FirstPersonHand],
                ToMilliseconds(totals[(int)GpuPassCategory.FirstPersonHand], timestampPeriodNanoseconds)),
            new(GpuPassCategory.Interface, counts[(int)GpuPassCategory.Interface], totals[(int)GpuPassCategory.Interface],
                ToMilliseconds(totals[(int)GpuPassCategory.Interface], timestampPeriodNanoseconds)),
            new(GpuPassCategory.Composite, counts[(int)GpuPassCategory.Composite], totals[(int)GpuPassCategory.Composite],
                ToMilliseconds(totals[(int)GpuPassCategory.Composite], timestampPeriodNanoseconds)));
    }

    internal static double? ToMilliseconds(ulong ticks, double? periodNanoseconds) =>
        periodNanoseconds is { } period ? ticks * period / 1_000_000.0 : null;

    private void CollectCompleted()
    {
        var mapping = false;
        foreach (var slot in _slots)
            mapping |= slot.State == SlotState.Mapping;
        if (mapping) _device.PollNonBlocking();

        foreach (var slot in _slots)
        {
            if (!slot.TryRead(out var values)) continue;
            if (!values.IsEmpty)
                Latest = BuildSnapshot(
                    slot.Frame,
                    _status,
                    _timestampPeriodNanoseconds,
                    values,
                    slot.Ranges.AsSpan(0, slot.RangeCount),
                    slot.FrameBeginningIndex,
                    slot.FrameEndIndex,
                    slot.Width,
                    slot.Height);
            slot.FinishRead();
        }
    }

    private static double? ResolveTimestampPeriod(WebGpuDevice device, out string status)
    {
        var configured = Environment.GetEnvironmentVariable(TimestampPeriodOverride);
        if (!string.IsNullOrWhiteSpace(configured) &&
            double.TryParse(configured, NumberStyles.Float, CultureInfo.InvariantCulture, out var overrideValue) &&
            double.IsFinite(overrideValue) && overrideValue > 0)
        {
            status = $"available: timestamp period from {TimestampPeriodOverride}";
            return overrideValue;
        }

        try
        {
            var period = WgpuQueueGetTimestampPeriod(device.Queue);
            if (float.IsFinite(period) && period > 0)
            {
                status = "available: timestamp period from wgpu-native";
                return period;
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Silk.NET 2.23 bundles an older wgpu-native ABI which exposes raw timestamp queries
            // but not the native timestamp-period extension. Raw ticks remain valuable and are
            // labelled as such; never pretend that they are nanoseconds.
        }
        catch (DllNotFoundException)
        {
            // The WebGPU loader already resolved the implementation for Silk. Some platform
            // loaders do not make that same module visible to a second direct P/Invoke.
        }

        var vulkanPeriod = VulkanTimestampPeriodResolver.TryResolve(device, out var vulkanDetail);
        if (vulkanPeriod is { } resolvedPeriod)
        {
            status = $"available: timestamp period from {vulkanDetail}";
            return resolvedPeriod;
        }

        status = $"raw ticks only: timestamp period unavailable ({vulkanDetail}); set {TimestampPeriodOverride}";
        return null;
    }

    private static GpuFrameTimingSnapshot EmptySnapshot(string status, double? period = null) =>
        new(
            0, status, period, 0, 0, 0, null,
            new(GpuPassCategory.World, 0, 0, null),
            new(GpuPassCategory.EntityImpostorCapture, 0, 0, null),
            new(GpuPassCategory.FirstPersonHand, 0, 0, null),
            new(GpuPassCategory.Interface, 0, 0, null),
            new(GpuPassCategory.Composite, 0, 0, null));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var mapping = false;
        foreach (var slot in _slots)
            mapping |= slot is { State: SlotState.Mapping };
        if (mapping)
        {
            // Disposal is outside the frame path. Waiting here prevents a native map callback from
            // retaining a delegate into an object whose buffers have already been released.
            _device.Poll();
            CollectCompleted();
        }

        foreach (var slot in _slots)
            slot?.Dispose();
    }

    [DllImport("wgpu_native", EntryPoint = "wgpuQueueGetTimestampPeriod")]
    private static extern float WgpuQueueGetTimestampPeriod(Queue* queue);

    internal readonly record struct QueryRange(
        GpuPassCategory Category,
        uint BeginningIndex,
        uint EndIndex);

    private enum SlotState
    {
        Idle,
        Encoding,
        AwaitingSubmit,
        Mapping
    }

    private sealed class Slot : IDisposable
    {
        private const int Pending = -1;
        private readonly WebGpuDevice _device;
        private readonly PfnBufferMapCallback _callback;
        private int _mapStatus = Pending;

        public Slot(WebGpuDevice device)
        {
            _device = device;
            QuerySetDescriptor queryDesc = new() { Type = QueryType.Timestamp, Count = QueryCount };
            QuerySet = device.Api.DeviceCreateQuerySet(device.Device, in queryDesc);

            BufferDescriptor resolveDesc = new()
            {
                Size = QueryBufferBytes,
                Usage = BufferUsage.QueryResolve | BufferUsage.CopySrc
            };
            ResolveBuffer = device.Api.DeviceCreateBuffer(device.Device, in resolveDesc);

            BufferDescriptor readbackDesc = new()
            {
                Size = QueryBufferBytes,
                Usage = BufferUsage.CopyDst | BufferUsage.MapRead
            };
            ReadbackBuffer = device.Api.DeviceCreateBuffer(device.Device, in readbackDesc);
            _callback = new PfnBufferMapCallback((status, _) => Volatile.Write(ref _mapStatus, (int)status));
        }

        public QuerySet* QuerySet { get; }
        public WgpuBuffer* ResolveBuffer { get; }
        public WgpuBuffer* ReadbackBuffer { get; }
        public QueryRange[] Ranges { get; } = new QueryRange[MaximumPassesPerFrame];
        public SlotState State { get; set; }
        public int RangeCount { get; set; }
        public uint NextQuery { get; set; }
        public uint FrameBeginningIndex { get; set; } = uint.MaxValue;
        public uint FrameEndIndex { get; set; } = uint.MaxValue;
        public uint Width { get; private set; }
        public uint Height { get; private set; }
        public long Frame { get; private set; }

        public void Begin(long frame, uint width, uint height)
        {
            Frame = frame;
            Width = width;
            Height = height;
            RangeCount = 0;
            NextQuery = 0;
            FrameBeginningIndex = uint.MaxValue;
            FrameEndIndex = uint.MaxValue;
            Volatile.Write(ref _mapStatus, Pending);
            State = SlotState.Encoding;
        }

        public void ResetWithoutMap()
        {
            RangeCount = 0;
            NextQuery = 0;
            State = SlotState.Idle;
        }

        public void StartMap()
        {
            State = SlotState.Mapping;
            _device.Api.BufferMapAsync(
                ReadbackBuffer,
                MapMode.Read,
                0,
                (nuint)(NextQuery * sizeof(ulong)),
                _callback,
                null);
        }

        public bool TryRead(out ReadOnlySpan<ulong> values)
        {
            values = default;
            if (State != SlotState.Mapping) return false;
            var status = Volatile.Read(ref _mapStatus);
            if (status == Pending) return false;
            if (status != (int)BufferMapAsyncStatus.Success) return true;

            var byteCount = checked((int)NextQuery * sizeof(ulong));
            var pointer = _device.Api.BufferGetConstMappedRange(ReadbackBuffer, 0, (nuint)byteCount);
            if (pointer is null) return true;
            values = new ReadOnlySpan<ulong>(pointer, checked((int)NextQuery));
            return true;
        }

        public void FinishRead()
        {
            if (Volatile.Read(ref _mapStatus) == (int)BufferMapAsyncStatus.Success)
                _device.Api.BufferUnmap(ReadbackBuffer);
            RangeCount = 0;
            NextQuery = 0;
            State = SlotState.Idle;
        }

        public void Dispose()
        {
            if (ReadbackBuffer is not null)
            {
                _device.Api.BufferDestroy(ReadbackBuffer);
                _device.Api.BufferRelease(ReadbackBuffer);
            }
            if (ResolveBuffer is not null)
            {
                _device.Api.BufferDestroy(ResolveBuffer);
                _device.Api.BufferRelease(ResolveBuffer);
            }
            if (QuerySet is not null)
            {
                _device.Api.QuerySetDestroy(QuerySet);
                _device.Api.QuerySetRelease(QuerySet);
            }
        }
    }
}
