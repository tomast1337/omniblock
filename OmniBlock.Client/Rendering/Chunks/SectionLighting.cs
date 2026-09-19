using System.Runtime.InteropServices;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Worlds.Core.Systems;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>The independently refreshable light streams for one section presentation.</summary>
/// <remarks>
///     LOD keeps dedicated buffers. Near terrain uses the light half of a paired regional range;
///     propagated values are fully validated off-thread, then written in place after the preceding
///     frame was submitted. Queue ordering makes the next frame observe the complete replacement.
/// </remarks>
internal sealed unsafe class SectionLighting : IDisposable
{
    private readonly WebGpuDevice _device;
    private readonly TerrainChunkQuadMesh? _solidRegional;
    private readonly TerrainChunkQuadMesh? _translucentRegional;
    private ChunkLightVertex[]? _pendingSolidValues;
    private ChunkLightVertex[]? _pendingTranslucentValues;
    private bool _disposed;

    private SectionLighting(
        WebGpuDevice device,
        SectionLightModel? solidModel,
        SectionLightModel? translucentModel,
        WgpuBuffer* solid,
        WgpuBuffer* translucent,
        TerrainChunkQuadMesh? solidRegional,
        TerrainChunkQuadMesh? translucentRegional,
        ChunkLightVertex[]? pendingSolidValues,
        ChunkLightVertex[]? pendingTranslucentValues,
        long epoch)
    {
        _device = device;
        SolidModel = solidModel;
        TranslucentModel = translucentModel;
        Solid = solid;
        Translucent = translucent;
        _solidRegional = solidRegional;
        _translucentRegional = translucentRegional;
        _pendingSolidValues = pendingSolidValues;
        _pendingTranslucentValues = pendingTranslucentValues;
        Epoch = epoch;
    }

    public WgpuBuffer* Solid { get; }
    public WgpuBuffer* Translucent { get; }
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
        SectionLightModel? solidModel,
        SectionLightModel? translucentModel,
        TerrainChunkQuadMesh? solid,
        TerrainChunkQuadMesh? translucent)
    {
        ValidateRegional(solidModel, solidModel?.InitialValues, solid);
        ValidateRegional(translucentModel, translucentModel?.InitialValues, translucent);
        var result = new SectionLighting(
            device, solidModel, translucentModel, null, null,
            solid, translucent, null, null, 0);
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
        TerrainChunkQuadMesh? solid,
        TerrainChunkQuadMesh? translucent,
        in SectionLightingEvaluation evaluation) =>
        CreateRegionalReplacement(device, solid, translucent, evaluation);

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
                solid, translucent, null, null, null, null, epoch);
        }
        catch
        {
            WgpuRelease.DeferredBuffers(device, (nint)solid, (nint)translucent);
            throw;
        }
    }

    private static SectionLighting CreateRegionalReplacement(
        WebGpuDevice device,
        TerrainChunkQuadMesh? solid,
        TerrainChunkQuadMesh? translucent,
        in SectionLightingEvaluation evaluation)
    {
        ValidateRegional(evaluation.SolidModel, evaluation.SolidValues, solid);
        ValidateRegional(evaluation.TranslucentModel, evaluation.TranslucentValues, translucent);
        return new SectionLighting(
            device,
            evaluation.SolidModel,
            evaluation.TranslucentModel,
            null,
            null,
            solid,
            translucent,
            evaluation.SolidValues,
            evaluation.TranslucentValues,
            evaluation.SourceEpoch + 1);
    }

    private static void ValidateRegional(
        SectionLightModel? model,
        ChunkLightVertex[]? values,
        TerrainChunkQuadMesh? mesh)
    {
        if (model == null)
        {
            if (values != null || mesh != null)
                throw new ArgumentException("Regional lighting was supplied without a light model.");
            return;
        }
        if (values == null || values.Length != model.VertexCount ||
            mesh == null || mesh.VertexCount != model.VertexCount)
            throw new ArgumentException("Terrain light values must match the light model.");
    }

    /// <summary>
    ///     Writes a fully validated light replacement into the paired range. This runs from
    ///     ChunkRenderer.EndFrame after the preceding command buffer was submitted, so queue order
    ///     keeps the old draw ahead of this upload and the next frame sees the complete new values.
    /// </summary>
    public void PublishRegional()
    {
        if (_pendingSolidValues is { } solid)
        {
            _solidRegional!.RewriteLighting(solid);
            _pendingSolidValues = null;
        }
        if (_pendingTranslucentValues is { } translucent)
        {
            _translucentRegional!.RewriteLighting(translucent);
            _pendingTranslucentValues = null;
        }
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
        WgpuRelease.DeferredBuffers(_device, (nint)Solid, (nint)Translucent);
    }
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

internal enum ChunkLightProbeKind : byte
{
    InferredFace = 0,
    FullBright = 1,
    ExactCell = 2,
    ExactCellAndAbove = 3
}

/// <summary>A four-byte, independently uploaded terrain-light vertex.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
internal readonly record struct ChunkLightVertex(byte Sky, byte Block, byte Pad0 = 0, byte Pad1 = 0)
{
    private const int KindMask = 0b11;
    private const int MinimumShift = 2;
    private const int XShift = 6;
    private const int YShift = 8;
    private const int ZShift = 10;

    internal ChunkLightProbeKind ProbeKind => (ChunkLightProbeKind)(ProbeData & KindMask);
    internal int MinimumBlockLight => (ProbeData >> MinimumShift) & 0x0F;
    internal int SampleOffsetX => DecodeOffset(XShift);
    internal int SampleOffsetY => DecodeOffset(YShift);
    internal int SampleOffsetZ => DecodeOffset(ZShift);

    private ushort ProbeData => (ushort)(Pad0 | Pad1 << 8);

    internal static ChunkLightVertex WithProbe(
        byte sky,
        byte block,
        ChunkLightProbeKind kind,
        int minimumBlockLight,
        int sampleOffsetX = 0,
        int sampleOffsetY = 0,
        int sampleOffsetZ = 0)
    {
        // Preserve the original full-bright marker exactly. Apart from making old captures easy to
        // inspect, no sample offsets or emission floor have meaning for a full-bright primitive.
        if (kind == ChunkLightProbeKind.FullBright)
            return new ChunkLightVertex(sky, block, (byte)ChunkLightProbeKind.FullBright);

        var packed = (int)kind
                     | (Math.Clamp(minimumBlockLight, 0, 15) << MinimumShift);
        if (kind is ChunkLightProbeKind.ExactCell or ChunkLightProbeKind.ExactCellAndAbove)
        {
            packed |= EncodeOffset(sampleOffsetX) << XShift;
            packed |= EncodeOffset(sampleOffsetY) << YShift;
            packed |= EncodeOffset(sampleOffsetZ) << ZShift;
        }
        return new ChunkLightVertex(sky, block, (byte)packed, (byte)(packed >> 8));
    }

    private static int EncodeOffset(int offset)
    {
        if (offset is < -1 or > 1)
            throw new InvalidOperationException($"A retained light sample is {offset} cells from its vertex; expected -1..1.");
        return offset + 1;
    }

    private int DecodeOffset(int shift) => ((ProbeData >> shift) & 0b11) - 1;
}

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
        (long)_probes.Length * Marshal.SizeOf<LightProbe>() +
        (long)(_initialValues?.Length ?? 0) * Marshal.SizeOf<ChunkLightVertex>();
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
                probes[i + corner] = LightProbe.Create(vertex, normal, initialLights[i + corner]);
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
        short X,
        short Y,
        short Z,
        ushort Metadata)
    {
        private const int NormalMask = 0b111;
        private const int KindShift = 3;
        private const int MinimumShift = 5;
        private const int XShift = 9;
        private const int YShift = 11;
        private const int ZShift = 13;

        private AxisNormal Normal => (AxisNormal)(Metadata & NormalMask);
        private ChunkLightProbeKind Kind => (ChunkLightProbeKind)((Metadata >> KindShift) & 0b11);
        private int MinimumBlockLight => (Metadata >> MinimumShift) & 0x0F;
        private int SampleOffsetX => DecodeOffset(XShift);
        private int SampleOffsetY => DecodeOffset(YShift);
        private int SampleOffsetZ => DecodeOffset(ZShift);

        public static LightProbe Create(
            in ChunkVertex vertex,
            AxisNormal normal,
            in ChunkLightVertex light)
        {
            var metadata = (int)normal
                           | ((int)light.ProbeKind << KindShift)
                           | (light.MinimumBlockLight << MinimumShift);
            if (light.ProbeKind is ChunkLightProbeKind.ExactCell or ChunkLightProbeKind.ExactCellAndAbove)
            {
                metadata |= EncodeOffset(light.SampleOffsetX) << XShift;
                metadata |= EncodeOffset(light.SampleOffsetY) << YShift;
                metadata |= EncodeOffset(light.SampleOffsetZ) << ZShift;
            }
            return new LightProbe(vertex.X, vertex.Y, vertex.Z, (ushort)metadata);
        }

        public ChunkLightVertex Evaluate(ILightProvider lighting, Vector3D<int> sectionPosition)
        {
            if (Kind == ChunkLightProbeKind.FullBright) return new ChunkLightVertex(0, 60);
            var x = sectionPosition.X + X * PositionScaleInv;
            var y = sectionPosition.Y + Y * PositionScaleInv;
            var z = sectionPosition.Z + Z * PositionScaleInv;
            if (Kind is ChunkLightProbeKind.ExactCell or ChunkLightProbeKind.ExactCellAndAbove)
            {
                var cell = new Cell(
                    FloorInside(x) + SampleOffsetX,
                    FloorInside(y) + SampleOffsetY,
                    FloorInside(z) + SampleOffsetZ);
                var exact = Sample(cell, lighting, MinimumBlockLight);
                return Kind == ChunkLightProbeKind.ExactCellAndAbove
                    ? Max(exact, Sample(cell with { Y = cell.Y + 1 }, lighting, MinimumBlockLight))
                    : exact;
            }
            if (Normal == AxisNormal.None)
                return Sample(new Cell(FloorInside(x), FloorInside(y), FloorInside(z)), lighting, MinimumBlockLight);

            var xs = TangentCells(x);
            var ys = TangentCells(y);
            var zs = TangentCells(z);
            if (Normal is AxisNormal.PositiveX or AxisNormal.NegativeX)
            {
                var nx = OutwardCell(x, Normal == AxisNormal.PositiveX);
                return MeanFour(new Cell(nx, ys.Low, zs.Low), new Cell(nx, ys.Low, zs.High),
                    new Cell(nx, ys.High, zs.Low), new Cell(nx, ys.High, zs.High), lighting, MinimumBlockLight);
            }
            if (Normal is AxisNormal.PositiveY or AxisNormal.NegativeY)
            {
                var ny = OutwardCell(y, Normal == AxisNormal.PositiveY);
                return MeanFour(new Cell(xs.Low, ny, zs.Low), new Cell(xs.Low, ny, zs.High),
                    new Cell(xs.High, ny, zs.Low), new Cell(xs.High, ny, zs.High), lighting, MinimumBlockLight);
            }

            var nz = OutwardCell(z, Normal == AxisNormal.PositiveZ);
            return MeanFour(new Cell(xs.Low, ys.Low, nz), new Cell(xs.Low, ys.High, nz),
                new Cell(xs.High, ys.Low, nz), new Cell(xs.High, ys.High, nz), lighting, MinimumBlockLight);
        }

        private static ChunkLightVertex MeanFour(
            Cell a, Cell b, Cell c, Cell d, ILightProvider lighting, int minimumBlockLight)
        {
            var sky = 0f;
            var block = 0f;
            Add(a, lighting, minimumBlockLight, ref sky, ref block);
            Add(b, lighting, minimumBlockLight, ref sky, ref block);
            Add(c, lighting, minimumBlockLight, ref sky, ref block);
            Add(d, lighting, minimumBlockLight, ref sky, ref block);
            return new ChunkLightVertex(
                ChunkVertexHelper.ToQuarterLevels(sky * 0.25f),
                ChunkVertexHelper.ToQuarterLevels(block * 0.25f));
        }

        private static ChunkLightVertex Sample(Cell cell, ILightProvider lighting, int minimumBlockLight)
        {
            var levels = lighting.GetLightLevels(cell.X, cell.Y, cell.Z, minimumBlockLight);
            return new ChunkLightVertex(
                ChunkVertexHelper.ToQuarterLevels(levels.Sky),
                ChunkVertexHelper.ToQuarterLevels(levels.Block));
        }

        private static void Add(
            Cell cell,
            ILightProvider lighting,
            int minimumBlockLight,
            ref float sky,
            ref float block)
        {
            var levels = lighting.GetLightLevels(cell.X, cell.Y, cell.Z, minimumBlockLight);
            sky += levels.Sky;
            block += levels.Block;
        }

        private static ChunkLightVertex Max(ChunkLightVertex a, ChunkLightVertex b) =>
            new(Math.Max(a.Sky, b.Sky), Math.Max(a.Block, b.Block));

        private static int EncodeOffset(int offset) => offset + 1;
        private int DecodeOffset(int shift) => ((Metadata >> shift) & 0b11) - 1;

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
