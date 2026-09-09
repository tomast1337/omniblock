using OmniBlock.Client.Rendering.Core;
using OmniBlock.Util;

namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>
///     Builds packed terrain vertices without going through the immediate-mode tessellator.
/// </summary>
/// <remarks>
///     Block renderers emit quads. This builder snapshots each corner's render state and expands
///     every complete quad to the terrain pipeline's triangle-list order: 0,1,2, 2,3,0.
/// </remarks>
internal sealed class ChunkMeshBuilder : IBlockVertexSink, IDisposable
{
    private readonly PendingVertex[] _quad = new PendingVertex[4];
    private PooledList<ChunkVertex>? _vertices = new();
    private int _arrayLayer = Tessellator.NoArrayLayer;
    private byte _blockLight;
    private int _color = unchecked((int)0xFFFFFFFF);
    private bool _hasColor;
    private bool _hasLight;
    private int _quadVertexCount;
    private byte _skyLight;
    private double _xOffset;
    private double _yOffset;
    private double _zOffset;

    public void Begin(double xOffset, double yOffset, double zOffset)
    {
        ObjectDisposedException.ThrowIf(_vertices is null, this);
        _vertices.Clear();
        _quadVertexCount = 0;
        _arrayLayer = Tessellator.NoArrayLayer;
        _color = unchecked((int)0xFFFFFFFF);
        _hasColor = false;
        _hasLight = false;
        _skyLight = 0;
        _blockLight = 0;
        _xOffset = xOffset;
        _yOffset = yOffset;
        _zOffset = zOffset;
    }

    public PooledList<ChunkVertex> Finish()
    {
        ObjectDisposedException.ThrowIf(_vertices is null, this);

        // Tessellator capture discarded an incomplete primitive at draw(). Block renderers should
        // emit whole quads, but retaining that behavior avoids turning an extraction into a new
        // runtime failure mode.
        _quadVertexCount = 0;

        var result = _vertices;
        _vertices = null;
        return result;
    }

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
            _hasLight ? _blockLight : (byte)0);

        if (_quadVertexCount != 4) return;

        Emit(0);
        Emit(1);
        Emit(2);
        Emit(2);
        Emit(3);
        Emit(0);
        _quadVertexCount = 0;
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
        _vertices = null;
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
            vertex.ArrayLayer,
            vertex.SkyLight,
            vertex.BlockLight));
    }

    private readonly record struct PendingVertex(
        float X,
        float Y,
        float Z,
        float U,
        float V,
        int Color,
        int ArrayLayer,
        byte SkyLight,
        byte BlockLight);
}
