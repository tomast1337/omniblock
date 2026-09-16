using System.Runtime.InteropServices;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Worlds.Core.Systems;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>The independently replaceable GPU light streams for one section presentation.</summary>
/// <remarks>
///     Geometry owns only stable light probes. A propagated-light update evaluates those probes
///     against the live world, creates complete replacement buffers, and swaps this object only
///     after every upload succeeds. The old buffers retire at a presentation boundary.
/// </remarks>
internal sealed unsafe class SectionLighting : IDisposable
{
    private readonly WebGpuDevice _device;
    private TerrainGpuBufferLease? _solidLease;
    private TerrainGpuBufferLease? _translucentLease;
    private bool _disposed;

    private SectionLighting(
        WebGpuDevice device,
        SectionLightModel? solidModel,
        SectionLightModel? translucentModel,
        WgpuBuffer* solid,
        WgpuBuffer* translucent,
        TerrainGpuBufferLease? solidLease,
        TerrainGpuBufferLease? translucentLease,
        long epoch)
    {
        _device = device;
        SolidModel = solidModel;
        TranslucentModel = translucentModel;
        Solid = solid;
        Translucent = translucent;
        _solidLease = solidLease;
        _translucentLease = translucentLease;
        Epoch = epoch;
    }

    public WgpuBuffer* Solid { get; }
    public WgpuBuffer* Translucent { get; }
    public TerrainGpuBufferSlice SolidSlice => _solidLease?.Slice ?? DedicatedSlice(Solid, SolidModel);
    public TerrainGpuBufferSlice TranslucentSlice =>
        _translucentLease?.Slice ?? DedicatedSlice(Translucent, TranslucentModel);
    public long Epoch { get; }

    private SectionLightModel? SolidModel { get; }
    private SectionLightModel? TranslucentModel { get; }
    public static SectionLighting CreateInitial(
        WebGpuDevice device,
        SectionLightModel? solidModel,
        SectionLightModel? translucentModel)
    {
        var result = Create(device, solidModel, translucentModel, null, 0);
        solidModel?.ReleaseInitialValues();
        translucentModel?.ReleaseInitialValues();
        return result;
    }

    public static SectionLighting CreateInitialRegional(
        WebGpuDevice device,
        TerrainGpuArenaSet arenas,
        TerrainRenderRegionKey regionKey,
        SectionLightModel? solidModel,
        SectionLightModel? translucentModel)
    {
        var result = CreateRegional(
            device, arenas, regionKey, solidModel, translucentModel, null, null, 0);
        solidModel?.ReleaseInitialValues();
        translucentModel?.ReleaseInitialValues();
        return result;
    }

    public SectionLightingPlan? CapturePlan() =>
        SolidModel is null && TranslucentModel is null
            ? null
            : new SectionLightingPlan(SolidModel, TranslucentModel, Epoch);

    public static SectionLighting CreateReplacement(
        WebGpuDevice device,
        in SectionLightingEvaluation evaluation)
    {
        WgpuBuffer* solid = null;
        WgpuBuffer* translucent = null;
        try
        {
            solid = CreateBuffer(device, evaluation.SolidModel, evaluation.SolidValues);
            translucent = CreateBuffer(
                device, evaluation.TranslucentModel, evaluation.TranslucentValues);
            return new SectionLighting(
                device,
                evaluation.SolidModel,
                evaluation.TranslucentModel,
                solid,
                translucent,
                null,
                null,
                evaluation.SourceEpoch + 1);
        }
        catch
        {
            WgpuRelease.DeferredBuffers(device, (nint)solid, (nint)translucent);
            throw;
        }
    }

    public static SectionLighting CreateReplacementRegional(
        WebGpuDevice device,
        TerrainGpuArenaSet arenas,
        TerrainRenderRegionKey regionKey,
        in SectionLightingEvaluation evaluation) =>
        CreateRegional(
            device,
            arenas,
            regionKey,
            evaluation.SolidModel,
            evaluation.TranslucentModel,
            evaluation.SolidValues,
            evaluation.TranslucentValues,
            evaluation.SourceEpoch + 1);

    private static SectionLighting Create(
        WebGpuDevice device,
        SectionLightModel? solidModel,
        SectionLightModel? translucentModel,
        ILightProvider? lighting,
        long epoch)
    {
        WgpuBuffer* solid = null;
        WgpuBuffer* translucent = null;
        try
        {
            solid = CreateBuffer(device, solidModel, lighting);
            translucent = CreateBuffer(device, translucentModel, lighting);
            return new SectionLighting(
                device, solidModel, translucentModel,
                solid, translucent, null, null, epoch);
        }
        catch
        {
            WgpuRelease.DeferredBuffers(device, (nint)solid, (nint)translucent);
            throw;
        }
    }

    private static SectionLighting CreateRegional(
        WebGpuDevice device,
        TerrainGpuArenaSet arenas,
        TerrainRenderRegionKey regionKey,
        SectionLightModel? solidModel,
        SectionLightModel? translucentModel,
        ChunkLightVertex[]? solidValues,
        ChunkLightVertex[]? translucentValues,
        long epoch)
    {
        TerrainGpuBufferLease? solid = null;
        TerrainGpuBufferLease? translucent = null;
        try
        {
            solid = UploadRegional(arenas, regionKey, solidModel, solidValues);
            translucent = UploadRegional(arenas, regionKey, translucentModel, translucentValues);
            return new SectionLighting(
                device, solidModel, translucentModel,
                null, null, solid, translucent, epoch);
        }
        catch
        {
            solid?.Dispose();
            translucent?.Dispose();
            throw;
        }
    }

    private static TerrainGpuBufferLease? UploadRegional(
        TerrainGpuArenaSet arenas,
        TerrainRenderRegionKey regionKey,
        SectionLightModel? model,
        ChunkLightVertex[]? values)
    {
        if (model == null)
        {
            if (values != null)
                throw new ArgumentException("Light values were supplied without a light model.");
            return null;
        }

        var source = values ?? model.InitialValues;
        if (source.Length != model.VertexCount)
            throw new ArgumentException("Terrain light values must match the light model.");
        return arenas.Upload(
            regionKey, TerrainGpuStreamKind.Lighting,
            MemoryMarshal.AsBytes(source.AsSpan()));
    }

    private static WgpuBuffer* CreateBuffer(
        WebGpuDevice device,
        SectionLightModel? model,
        ILightProvider? lighting)
    {
        if (model == null) return null;
        var values = lighting == null ? model.InitialValues : model.Evaluate(lighting);
        var bytes = MemoryMarshal.AsBytes(values.AsSpan());
        BufferDescriptor descriptor = new()
        {
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
            Size = (ulong)bytes.Length
        };
        var buffer = device.Api.DeviceCreateBuffer(device.Device, in descriptor);
        fixed (byte* data = bytes)
            device.Api.QueueWriteBuffer(device.Queue, buffer, 0, data, (nuint)bytes.Length);
        return buffer;
    }

    private static WgpuBuffer* CreateBuffer(
        WebGpuDevice device,
        SectionLightModel? model,
        ChunkLightVertex[]? values)
    {
        if (model == null)
        {
            if (values != null)
                throw new ArgumentException("Light values were supplied without a light model.");
            return null;
        }
        if (values == null || values.Length != model.VertexCount)
            throw new ArgumentException("Replacement light values must match the light model.");

        var bytes = MemoryMarshal.AsBytes(values.AsSpan());
        BufferDescriptor descriptor = new()
        {
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
            Size = (ulong)bytes.Length
        };
        var buffer = device.Api.DeviceCreateBuffer(device.Device, in descriptor);
        fixed (byte* data = bytes)
            device.Api.QueueWriteBuffer(device.Queue, buffer, 0, data, (nuint)bytes.Length);
        return buffer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Exchange(ref _solidLease, null)?.Dispose();
        Interlocked.Exchange(ref _translucentLease, null)?.Dispose();
        WgpuRelease.DeferredBuffers(_device, (nint)Solid, (nint)Translucent);
    }

    private static TerrainGpuBufferSlice DedicatedSlice(
        WgpuBuffer* buffer,
        SectionLightModel? model) =>
        buffer == null || model == null
            ? default
            : new TerrainGpuBufferSlice(
                buffer, 0, checked((ulong)model.VertexCount * WgpuMesh.ChunkLightVertexStride));
}

internal readonly record struct SectionLightingPlan(
    SectionLightModel? SolidModel,
    SectionLightModel? TranslucentModel,
    long SourceEpoch)
{
    public SectionLightingEvaluation Evaluate(ILightProvider lighting) => new(
        SolidModel,
        TranslucentModel,
        SolidModel?.Evaluate(lighting),
        TranslucentModel?.Evaluate(lighting),
        SourceEpoch);
}

internal readonly record struct SectionLightingEvaluation(
    SectionLightModel? SolidModel,
    SectionLightModel? TranslucentModel,
    ChunkLightVertex[]? SolidValues,
    ChunkLightVertex[]? TranslucentValues,
    long SourceEpoch);

/// <summary>A four-byte, independently uploaded terrain-light vertex.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
internal readonly record struct ChunkLightVertex(byte Sky, byte Block, byte Pad0 = 0, byte Pad1 = 0);

/// <summary>
///     Compact CPU probes retained beside geometry so later light changes never invoke block
///     renderers. Axis-aligned quads sample the four outward cells meeting at each corner; unusual
///     model quads conservatively sample their nearest cell.
/// </summary>
internal sealed class SectionLightModel
{
    private const float PositionScaleInv = 64.0f / 32767.0f;
    private readonly LightProbe[] _probes;
    private readonly Vector3D<int> _sectionPosition;
    private ChunkLightVertex[]? _initialValues;

    private SectionLightModel(
        Vector3D<int> sectionPosition,
        ChunkLightVertex[] initialValues,
        LightProbe[] probes)
    {
        _sectionPosition = sectionPosition;
        _initialValues = initialValues;
        _probes = probes;
    }

    public ChunkLightVertex[] InitialValues => _initialValues
        ?? throw new InvalidOperationException("Initial light values have already been uploaded.");
    public int VertexCount => _probes.Length;
    public long RetainedBytes =>
        (long)_probes.Length * 8 + (long)(_initialValues?.Length ?? 0) * Marshal.SizeOf<ChunkLightVertex>();
    public long InitialUploadBytes =>
        (long)(_initialValues?.Length ?? 0) * Marshal.SizeOf<ChunkLightVertex>();

    public void ReleaseInitialValues() => _initialValues = null;

    public static SectionLightModel? Create(
        Vector3D<int> sectionPosition,
        ReadOnlySpan<ChunkVertex> vertices,
        ReadOnlySpan<ChunkLightVertex> initialLights)
    {
        if (vertices.IsEmpty) return null;
        if (vertices.Length % 4 != 0)
            throw new ArgumentException("Terrain light probes require four vertices per quad.", nameof(vertices));
        if (vertices.Length != initialLights.Length)
            throw new ArgumentException("Terrain geometry and light streams must have the same vertex count.", nameof(initialLights));

        var initial = initialLights.ToArray();
        var probes = new LightProbe[vertices.Length];
        for (var i = 0; i < vertices.Length; i += 4)
        {
            var normal = DominantNormal(vertices[i], vertices[i + 1], vertices[i + 2]);
            for (var corner = 0; corner < 4; corner++)
            {
                ref readonly var vertex = ref vertices[i + corner];
                probes[i + corner] = LightProbe.Create(vertex, normal,
                    initialLights[i + corner].Pad0 != 0);
            }
        }

        return new SectionLightModel(sectionPosition, initial, probes);
    }

    public ChunkLightVertex[] Evaluate(ILightProvider lighting)
    {
        var values = new ChunkLightVertex[_probes.Length];
        for (var i = 0; i < values.Length; i++)
            values[i] = _probes[i].Evaluate(lighting, _sectionPosition);
        return values;
    }

    private static AxisNormal DominantNormal(in ChunkVertex a, in ChunkVertex b, in ChunkVertex c)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var abz = b.Z - a.Z;
        var acx = c.X - a.X;
        var acy = c.Y - a.Y;
        var acz = c.Z - a.Z;
        var nx = (long)aby * acz - (long)abz * acy;
        var ny = (long)abz * acx - (long)abx * acz;
        var nz = (long)abx * acy - (long)aby * acx;
        var ax = Math.Abs(nx);
        var ay = Math.Abs(ny);
        var az = Math.Abs(nz);
        var largest = Math.Max(ax, Math.Max(ay, az));
        if (largest == 0) return AxisNormal.None;
        // Only call a quad axis-aligned when the other components are effectively zero. Sloped
        // rails and crossed plants deliberately use the conservative nearest-cell fallback.
        if (ax == largest && ay * 100 <= ax && az * 100 <= ax) return nx >= 0 ? AxisNormal.PositiveX : AxisNormal.NegativeX;
        if (ay == largest && ax * 100 <= ay && az * 100 <= ay) return ny >= 0 ? AxisNormal.PositiveY : AxisNormal.NegativeY;
        if (az == largest && ax * 100 <= az && ay * 100 <= az) return nz >= 0 ? AxisNormal.PositiveZ : AxisNormal.NegativeZ;
        return AxisNormal.None;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
    private readonly record struct LightProbe(
        short X, short Y, short Z, AxisNormal Normal, bool FullBright)
    {
        public static LightProbe Create(
            in ChunkVertex vertex,
            AxisNormal normal,
            bool fullBright)
            => new(vertex.X, vertex.Y, vertex.Z, normal, fullBright);

        public ChunkLightVertex Evaluate(ILightProvider lighting, Vector3D<int> sectionPosition)
        {
            if (FullBright) return new ChunkLightVertex(0, 60);
            var x = sectionPosition.X + X * PositionScaleInv;
            var y = sectionPosition.Y + Y * PositionScaleInv;
            var z = sectionPosition.Z + Z * PositionScaleInv;
            if (Normal == AxisNormal.None)
                return Sample(new Cell(FloorInside(x), FloorInside(y), FloorInside(z)), lighting);

            var xs = TangentCells(x);
            var ys = TangentCells(y);
            var zs = TangentCells(z);
            if (Normal is AxisNormal.PositiveX or AxisNormal.NegativeX)
            {
                var nx = OutwardCell(x, Normal == AxisNormal.PositiveX);
                return MeanFour(new Cell(nx, ys.Low, zs.Low), new Cell(nx, ys.Low, zs.High),
                    new Cell(nx, ys.High, zs.Low), new Cell(nx, ys.High, zs.High), lighting);
            }
            if (Normal is AxisNormal.PositiveY or AxisNormal.NegativeY)
            {
                var ny = OutwardCell(y, Normal == AxisNormal.PositiveY);
                return MeanFour(new Cell(xs.Low, ny, zs.Low), new Cell(xs.Low, ny, zs.High),
                    new Cell(xs.High, ny, zs.Low), new Cell(xs.High, ny, zs.High), lighting);
            }

            var nz = OutwardCell(z, Normal == AxisNormal.PositiveZ);
            return MeanFour(new Cell(xs.Low, ys.Low, nz), new Cell(xs.Low, ys.High, nz),
                new Cell(xs.High, ys.Low, nz), new Cell(xs.High, ys.High, nz), lighting);
        }

        private static ChunkLightVertex MeanFour(
            Cell a, Cell b, Cell c, Cell d, ILightProvider lighting)
        {
            var sky = 0f;
            var block = 0f;
            Add(a, lighting, ref sky, ref block);
            Add(b, lighting, ref sky, ref block);
            Add(c, lighting, ref sky, ref block);
            Add(d, lighting, ref sky, ref block);
            return new ChunkLightVertex(
                ChunkVertexHelper.ToQuarterLevels(sky * 0.25f),
                ChunkVertexHelper.ToQuarterLevels(block * 0.25f));
        }

        private static ChunkLightVertex Sample(Cell cell, ILightProvider lighting)
        {
            var levels = lighting.GetLightLevels(cell.X, cell.Y, cell.Z, 0);
            return new ChunkLightVertex(
                ChunkVertexHelper.ToQuarterLevels(levels.Sky),
                ChunkVertexHelper.ToQuarterLevels(levels.Block));
        }

        private static void Add(Cell cell, ILightProvider lighting, ref float sky, ref float block)
        {
            var levels = lighting.GetLightLevels(cell.X, cell.Y, cell.Z, 0);
            sky += levels.Sky;
            block += levels.Block;
        }

        private static (int Low, int High) TangentCells(float value)
        {
            var rounded = MathF.Round(value);
            if (MathF.Abs(value - rounded) < 0.002f)
                return ((int)rounded - 1, (int)rounded);
            var cell = FloorInside(value);
            return (cell, cell);
        }

        private static int OutwardCell(float value, bool positive) =>
            positive ? (int)MathF.Floor(value + 0.002f) : (int)MathF.Ceiling(value - 0.002f) - 1;

        private static int FloorInside(float value) => (int)MathF.Floor(value + 0.002f);
    }

    private readonly record struct Cell(int X, int Y, int Z);

    private enum AxisNormal : byte
    {
        None,
        PositiveX,
        NegativeX,
        PositiveY,
        NegativeY,
        PositiveZ,
        NegativeZ
    }
}
