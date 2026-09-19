using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Util;

namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>
///     Builds packed terrain vertices without going through the immediate-mode tessellator.
/// </summary>
/// <remarks>
///     Block renderers emit quads. This builder snapshots each corner's render state and stores the
///     four unique vertices; every terrain mesh shares the device's sequential quad index buffer.
/// </remarks>
internal sealed class ChunkMeshBuilder : IBlockVertexSink, IDisposable
{
    private readonly PendingVertex[] _quad = new PendingVertex[4];
    private int _arrayLayer = Tessellator.NoArrayLayer;
    private byte _blockLight;
    private int _color = unchecked((int)0xFFFFFFFF);
    private bool _hasColor;
    private bool _hasLight;
    private bool _fullBright;
    private bool _includeCellAbove;
    private int _lightSampleX;
    private int _lightSampleY;
    private int _lightSampleZ;
    private byte _minimumBlockLight;
    private bool _usesExactLightSample;
    private int _quadVertexCount;
    private Side? _quadDirection;
    private PooledList<byte>? _quadDirections = new();
    private byte _skyLight;
    private PooledList<ChunkVertex>? _vertices = new();
    private PooledList<ChunkLightVertex>? _lights = new();
    private double _xOffset;
    private double _yOffset;
    private double _zOffset;

    public void addVertexWithUV(double x, double y, double z, double u, double v)
    {
        ObjectDisposedException.ThrowIf(_vertices is null, this);

        _quad[_quadVertexCount++] = new PendingVertex(
            (float)(x + _xOffset),
            (float)(y + _yOffset),
            (float)(z + _zOffset),
            (float)u,
            (float)v,
            _hasColor ? _color : 0,
            _arrayLayer,
            _hasLight ? _skyLight : (byte)0,
            _hasLight ? _blockLight : (byte)0,
            _fullBright,
            _minimumBlockLight,
            _usesExactLightSample,
            _includeCellAbove,
            _lightSampleX,
            _lightSampleY,
            _lightSampleZ);

        if (_quadVertexCount != 4) return;

        Emit(0);
        Emit(1);
        Emit(2);
        Emit(3);
        _quadDirections!.Add(_quadDirection is { } side ? (byte)side : (byte)6);
        _quadVertexCount = 0;
        _quadDirection = null;
    }

    public void setArrayLayer(int layer) => _arrayLayer = layer;

    public void setColorOpaque_F(float red, float green, float blue)
    {
        var r = Math.Clamp((int)(red * 255.0F), 0, 255);
        var g = Math.Clamp((int)(green * 255.0F), 0, 255);
        var b = Math.Clamp((int)(blue * 255.0F), 0, 255);

        _color = BitConverter.IsLittleEndian
            ? unchecked((int)(0xFF000000u | (uint)(b << 16) | (uint)(g << 8) | (uint)r))
            : unchecked((int)(((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | 0xFFu));
        _hasColor = true;
    }

    public void setLight(float sky, float block)
    {
        _skyLight = ChunkVertexHelper.ToQuarterLevels(sky);
        _blockLight = ChunkVertexHelper.ToQuarterLevels(block);
        _hasLight = true;
        _fullBright = false;
        _usesExactLightSample = false;
        _includeCellAbove = false;
    }

    public void setMinimumBlockLight(int minimumBlockLight) =>
        _minimumBlockLight = (byte)Math.Clamp(minimumBlockLight, 0, 15);

    public void setLightSample(
        float sky,
        float block,
        int x,
        int y,
        int z,
        int minimumBlockLight,
        bool includeCellAbove = false)
    {
        setLight(sky, block);
        setMinimumBlockLight(minimumBlockLight);
        _usesExactLightSample = true;
        _includeCellAbove = includeCellAbove;
        _lightSampleX = checked((int)(x + _xOffset));
        _lightSampleY = checked((int)(y + _yOffset));
        _lightSampleZ = checked((int)(z + _zOffset));
    }

    public void setQuadDirection(Side? side) => _quadDirection = side;

    public void setFullBright()
    {
        setLight(0.0f, 15.0f);
        _fullBright = true;
    }

    public void setTranslationF(float x, float y, float z)
    {
        _xOffset += x;
        _yOffset += y;
        _zOffset += z;
    }

    public void Dispose()
    {
        _vertices?.Dispose();
        _lights?.Dispose();
        _quadDirections?.Dispose();
        _vertices = null;
        _lights = null;
        _quadDirections = null;
    }

    public void Begin(double xOffset, double yOffset, double zOffset)
    {
        ObjectDisposedException.ThrowIf(_vertices is null, this);
        ObjectDisposedException.ThrowIf(_lights is null, this);
        _vertices.Clear();
        _lights.Clear();
        _quadDirections!.Clear();
        _quadVertexCount = 0;
        _quadDirection = null;
        _arrayLayer = Tessellator.NoArrayLayer;
        _color = unchecked((int)0xFFFFFFFF);
        _hasColor = false;
        _hasLight = false;
        _fullBright = false;
        _includeCellAbove = false;
        _lightSampleX = 0;
        _lightSampleY = 0;
        _lightSampleZ = 0;
        _minimumBlockLight = 0;
        _usesExactLightSample = false;
        _skyLight = 0;
        _blockLight = 0;
        _xOffset = xOffset;
        _yOffset = yOffset;
        _zOffset = zOffset;
    }

    public PooledList<ChunkVertex> Finish(out PooledList<ChunkLightVertex> lights)
    {
        return Finish(out lights, out _);
    }

    public PooledList<ChunkVertex> Finish(
        out PooledList<ChunkLightVertex> lights,
        out ChunkDirectionalRanges ranges)
    {
        ObjectDisposedException.ThrowIf(_vertices is null, this);

        // Tessellator capture discarded an incomplete primitive at draw(). Block renderers should
        // emit whole quads, but retaining that behavior avoids turning an extraction into a new
        // runtime failure mode.
        _quadVertexCount = 0;

        var sourceVertices = _vertices;
        var sourceLights = _lights!;
        var directions = _quadDirections!;
        if (sourceVertices.Count != sourceLights.Count || sourceVertices.Count / 4 != directions.Count)
            throw new InvalidOperationException("Chunk geometry, lighting, and direction streams diverged.");

        Span<int> quadCounts = stackalloc int[7];
        foreach (var direction in directions.Span) quadCounts[direction]++;

        var result = new PooledList<ChunkVertex>(sourceVertices.Count);
        lights = new PooledList<ChunkLightVertex>(sourceLights.Count);
        Span<ChunkQuadRange> packedRanges = stackalloc ChunkQuadRange[7];
        for (var bucket = 0; bucket < packedRanges.Length; bucket++)
        {
            var firstQuad = result.Count / 4;
            for (var quad = 0; quad < directions.Count; quad++)
            {
                if (directions.Buffer[quad] != bucket) continue;
                result.AddRange(sourceVertices.Buffer.AsSpan(quad * 4, 4));
                lights.AddRange(sourceLights.Buffer.AsSpan(quad * 4, 4));
            }

            packedRanges[bucket] = new ChunkQuadRange(firstQuad, quadCounts[bucket]);
        }

        ranges = new ChunkDirectionalRanges(
            packedRanges[0], packedRanges[1], packedRanges[2], packedRanges[3],
            packedRanges[4], packedRanges[5], packedRanges[6]);

        sourceVertices.Dispose();
        sourceLights.Dispose();
        directions.Dispose();
        _vertices = null;
        _lights = null;
        _quadDirections = null;
        return result;
    }

    internal PooledList<ChunkVertex> Finish()
    {
        var vertices = Finish(out var lights);
        lights.Dispose();
        return vertices;
    }

    private void Emit(int index)
    {
        var vertex = _quad[index];
        _vertices!.Add(ChunkVertexHelper.Create(
            _hasColor ? vertex.Color : unchecked((int)0xFFFFFFFF),
            vertex.X,
            vertex.Y,
            vertex.Z,
            vertex.U,
            vertex.V,
            vertex.ArrayLayer));
        var probeKind = vertex.FullBright
            ? ChunkLightProbeKind.FullBright
            : vertex.UsesExactLightSample
                ? vertex.IncludeCellAbove
                    ? ChunkLightProbeKind.ExactCellAndAbove
                    : ChunkLightProbeKind.ExactCell
                : ChunkLightProbeKind.InferredFace;
        var vertexCellX = FloorInside(vertex.X);
        var vertexCellY = FloorInside(vertex.Y);
        var vertexCellZ = FloorInside(vertex.Z);
        _lights!.Add(ChunkLightVertex.WithProbe(
            vertex.SkyLight,
            vertex.BlockLight,
            probeKind,
            vertex.MinimumBlockLight,
            vertex.LightSampleX - vertexCellX,
            vertex.LightSampleY - vertexCellY,
            vertex.LightSampleZ - vertexCellZ));
    }

    private static int FloorInside(float value) => (int)MathF.Floor(value + 0.002f);

    private readonly record struct PendingVertex(
        float X,
        float Y,
        float Z,
        float U,
        float V,
        int Color,
        int ArrayLayer,
        byte SkyLight,
        byte BlockLight,
        bool FullBright,
        byte MinimumBlockLight,
        bool UsesExactLightSample,
        bool IncludeCellAbove,
        int LightSampleX,
        int LightSampleY,
        int LightSampleZ);
}
