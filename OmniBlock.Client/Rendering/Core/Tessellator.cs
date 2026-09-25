using System.Runtime.InteropServices;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.Rendering.Core;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 36)]
public struct Vertex(float x, float y, float z, float u, float v, int color, int normal)
{
    public float X = x; // 4 bytes
    public float Y = y; // 4 bytes + 4 bytes = 8 bytes
    public float Z = z; // 4 bytes + 8 bytes = 12 bytes
    public float U = u; // 4 bytes + 12 bytes = 16 bytes
    public float V = v; // 4 bytes + 16 bytes = 20 bytes
    public int Color = color; // 4 bytes + 20 bytes = 24 bytes
    public int Normal = normal; // 4 bytes + 20 bytes = 28 bytes

    /// <summary>
    ///     Which layer of the named texture array this vertex samples, or
    ///     <see cref="Tessellator.NoArrayLayer" /> for the plain 2D texture on unit 0.
    /// </summary>
    public int ArrayLayer = Tessellator.NoArrayLayer; // 4 bytes + 28 bytes = 32 bytes

    /// <summary>
    ///     The two world-light channels in quarter levels, sky in the low byte and block in the next.
    ///     <see cref="Tessellator.FullBrightLight" /> for a draw that sets none.
    /// </summary>
    /// <remarks>
    ///     Alongside the colour rather than multiplied into it, the same way the chunk light stream
    ///     carries it — a block drawn from the Tessellator is still a block, and the shader applies
    ///     the same ramp to it. Before this the field did not exist, and the light every block draw
    ///     was already setting reached the chunk mesh and nothing else, so a moving piston or a
    ///     primed TNT drew at full daylight wherever it was.
    /// </remarks>
    public int Light = Tessellator.FullBrightLight; // 36 bytes total
}

[StructLayout(LayoutKind.Explicit, Size = 20)]
public struct ChunkVertex
{
    // Position: 4×short. WebGPU has no Sint16x3 format, so the fourth lane carries the packed
    // X/Z offset of a distant terrain page relative to its tile draw origin. Exact meshes keep it
    // zero. This lets many 64-block CPU pages share one GPU allocation and draw without widening
    // the compact 20-byte vertex.
    [FieldOffset(0)] public short X;
    [FieldOffset(2)] public short Y;
    [FieldOffset(4)] public short Z;
    [FieldOffset(6)] public short PageOffsetXZ;

    // Colour: RGBA as 4 unsigned bytes, sent normalized.
    [FieldOffset(8)] public int Color;

    // Within the vertex's own array layer, 1.0 stored as UV_SCALE (4095). Unsigned rather than
    // signed so the range reaches past 1.0: flowing water turns its quad about the tile's corner
    // and needs to run past the edge, which the layer's wrap folds back onto itself, and a greedy-
    // merged quad tiles the texture across up to a sub-chunk's width (16) instead of stretching it.
    [FieldOffset(12)] public ushort U;
    [FieldOffset(14)] public ushort V;

    /// <summary>Which layer of the terrain array this vertex samples.</summary>
    [FieldOffset(16)] public byte ArrayLayer;

    /// <summary>
    ///     Power-of-two multiplier applied to both decoded UV coordinates. Ordinary chunk quads
    ///     use zero; coarse terrain LOD faces use it to tile beyond sixteen blocks without giving
    ///     up the exact renderer's fixed-point UV precision.
    /// </summary>
    [FieldOffset(17)] public byte UvScaleExponent;
    /// <summary>Distant-page Y offset in 64-block units; zero for exact terrain.</summary>
    [FieldOffset(18)] public byte PageOffsetY;
    /// <summary>Low 7 bits: texture mip level; bit 7: opaque LOD may blend toward its tile color.</summary>
    [FieldOffset(19)] public byte TextureMipLevel;
}

public static class ChunkVertexHelper
{
    private const float POSITION_SCALE = 32767f / 64f;

    // 65535 / 16 rounded down: the worst case is a greedy-merged quad spanning a whole sub-chunk
    // edge (SubChunkRenderer.Size = 16), which needs UV up to 16.0 to tile rather than stretch. A
    // ushort can't hold both that range and the old 1/32767-of-a-tile precision no texture this
    // small (terrain tiles are at most a few dozen px) could ever resolve, so precision loses.
    private const float UV_SCALE = 4095f;

    public static ChunkVertex Create(
        int color,
        float x,
        float y,
        float z,
        float u,
        float v,
        int arrayLayer,
        byte uvScaleExponent = 0)
    {
        return new ChunkVertex
        {
            Color = color,
            X = FloatToShortPosition(x),
            Y = FloatToShortPosition(y),
            Z = FloatToShortPosition(z),
            U = FloatToShortUV(u),
            V = FloatToShortUV(v),
            ArrayLayer = (byte)arrayLayer,
            PageOffsetXZ = 0,
            UvScaleExponent = uvScaleExponent,
            PageOffsetY = 0,
            TextureMipLevel = 0
        };
    }

    /// <summary>A light level in 0..15, possibly fractional, as the quarter levels a vertex holds.</summary>
    public static byte ToQuarterLevels(float level) =>
        (byte)Math.Clamp((int)MathF.Round(level * 4.0f), 0, 60);

    public static short FloatToShortPosition(float position) => (short)Math.Round(position * POSITION_SCALE);

    /// <summary>
    ///     A texture coordinate within its own array layer as the fixed point a vertex holds, where
    ///     1.0 is a whole tile and the representable range runs to 16.0 — a sub-chunk's width, the
    ///     widest a greedy-merged quad can tile across.
    /// </summary>
    /// <remarks>
    ///     Straight quantization, with none of the inward bias an atlas needed: a layer's edge is the
    ///     texture's edge, so a neighbouring cell is no longer somewhere a coordinate can slip into.
    ///     Bleeding is what the bias existed to hide.
    /// </remarks>
    public static ushort FloatToShortUV(float uv) => (ushort)Math.Clamp((int)MathF.Round(uv * UV_SCALE), 0, ushort.MaxValue);
}

public class Tessellator : IBlockVertexSink
{
    /// <summary>
    ///     What a vertex carries when its texture is the plain 2D one on unit 0 rather than a layer of
    ///     a named array. The default, so a draw that never heard of arrays keeps sampling as it did.
    /// </summary>
    public const int NoArrayLayer = -1;

    /// <summary>Ints per vertex in the raw buffer, matching <see cref="Vertex" /> field for field.</summary>
    private const int RawVertexInts = 9;

    /// <summary>
    ///     The packed light a vertex carries when its draw sets none: no sky, full block. Through the
    ///     block channel so it stays bright after dark, which is what text and inventory items want.
    /// </summary>
    public static readonly int FullBrightLight = ChunkVertexHelper.ToQuarterLevels(15.0f) << 8;

    private static readonly bool convertQuadsToTriangles = true;
    public static readonly Tessellator instance = new(2097152);
    private readonly int bufferSize;
    private readonly int[] rawBuffer;
    private int addedVertices;
    private int arrayLayer = NoArrayLayer;
    private byte blockLight;
    private int color;
    private int drawMode;
    private bool hasColor;
    private bool hasLight;
    private bool hasNormals;
    private bool hasTexture;
    private bool isColorDisabled;
    private int normal;
    private int rawBufferIndex;
    private byte skyLight;
    private double textureU;
    private double textureV;
    private int vertexCount;
    private double xOffset;
    private double yOffset;
    private double zOffset;

    private Tessellator(int bufferSize)
    {
        this.bufferSize = bufferSize;
        rawBuffer = new int[bufferSize];
    }

    public bool IsDrawing { get; private set; }

    /// <summary>
    ///     The primitive the accumulated vertices are actually submitted as.
    /// </summary>
    /// <remarks>
    ///     Quads are expanded into triangles as vertices are added, so the recorded draw mode is not
    ///     what gets drawn. The numbers are OpenGL's, because <see cref="startDrawing" /> takes them
    ///     from callers that have always spelled them that way.
    /// </remarks>
    private DrawTopology SubmittedTopology
    {
        get
        {
            if (drawMode == 7 && convertQuadsToTriangles)
            {
                return DrawTopology.Triangles;
            }

            return drawMode switch
            {
                0 => DrawTopology.Points,
                1 => DrawTopology.Lines,
                3 => DrawTopology.LineStrip,
                4 => DrawTopology.Triangles,
                5 => DrawTopology.TriangleStrip,
                6 => DrawTopology.TriangleFan,
                _ => throw new InvalidOperationException($"No topology for draw mode {drawMode}.")
            };
        }
    }

    private VertexChannels Channels =>
        (hasTexture ? VertexChannels.Texture : VertexChannels.None)
        | (hasColor ? VertexChannels.Color : VertexChannels.None)
        | (hasNormals ? VertexChannels.Normal : VertexChannels.None);

    /// <summary>The light the vertices from here on carry, or full brightness if none was set.</summary>
    private int PackedLight => hasLight ? skyLight | (blockLight << 8) : FullBrightLight;

    /// <summary>
    ///     Which layer of the bound texture array the vertices from here on sample. Stays set until
    ///     changed, like the colour and the UV do.
    /// </summary>
    /// <remarks>
    ///     A layer rather than a texture name because the caller is usually resolving a legacy
    ///     <c>TextureId</c>: see <see cref="Textures.AtlasTileMap.LayerOfGridIndex" />.
    /// </remarks>
    public void setArrayLayer(int layer) => arrayLayer = layer;

    public void setColorOpaque_F(float red, float green, float blue) => setColorOpaque((int)(red * 255.0F), (int)(green * 255.0F), (int)(blue * 255.0F));

    public void addVertexWithUV(double x, double y, double z, double u, double v)
    {
        setTextureUV(u, v);
        addVertex(x, y, z);
    }

    /// <summary>
    ///     The two light levels the next vertices carry, each 0..15 and allowed to be fractional.
    /// </summary>
    /// <remarks>
    ///     Levels, not brightness: the ramp and the time of day are applied by the terrain shader, so
    ///     what is stored here is what the world holds rather than what it currently looks like.
    /// </remarks>
    public void setLight(float sky, float block)
    {
        skyLight = ChunkVertexHelper.ToQuarterLevels(sky);
        blockLight = ChunkVertexHelper.ToQuarterLevels(block);
        hasLight = true;
    }

    public void setTranslationF(float x, float y, float z)
    {
        xOffset += x;
        yOffset += y;
        zOffset += z;
    }

    public void begin()
    {
        arrayLayer = NoArrayLayer;
        vertexCount = 0;
        hasTexture = false;
        hasColor = false;
        hasNormals = false;
    }

    /// <summary>Draws the accumulated vertices under whatever program the caller has already bound.</summary>
    /// <remarks>
    ///     For the sky, which binds its own shader and sets its own uniforms around a batch built
    ///     here. Not a fallback — there is nothing left to fall back to — and not a way to avoid
    ///     naming a slot: a draw that reaches this without a program bound draws with none.
    /// </remarks>
    public void drawWithBoundProgram() => Submit(null);

    /// <summary>Draws the accumulated vertices under whatever program <paramref name="slot" /> resolves to.</summary>
    /// <remarks>
    ///     The slot is an argument rather than ambient state on purpose. Everything this migration
    ///     has cost was some draw inheriting state a previous one left set, and a draw that has to
    ///     name what it is cannot inherit the answer.
    /// </remarks>
    public void draw(ProgramSlot slot) => Submit(slot);

    private void Submit(ProgramSlot? slot)
    {
        if (!IsDrawing)
        {
            throw new InvalidOperationException("Not tesselating!");
        }

        IsDrawing = false;

        if (vertexCount > 0)
        {
            RenderSystem.DrawTarget.Submit(BuildCommand(slot));
        }

        reset();
    }

    /// <summary>The accumulated vertices as something a backend can be handed.</summary>
    private DrawCommand BuildCommand(ProgramSlot? slot) => new()
    {
        Vertices = MemoryMarshal.AsBytes(rawBuffer.AsSpan(0, rawBufferIndex)),
        VertexCount = vertexCount,
        Topology = SubmittedTopology,
        Channels = Channels,
        Slot = slot
    };

    /// <summary>
    ///     Ends the batch by handing its vertices to a buffer that outlives the frame, instead of
    ///     drawing them.
    /// </summary>
    /// <remarks>
    ///     For geometry that never changes. <see cref="draw" /> cycles through a ring of streaming
    ///     buffers, which is the right trade when the contents are rebuilt every frame and the wrong
    ///     one when they are built once and drawn forever.
    /// </remarks>
    public IStaticMesh captureStatic()
    {
        if (!IsDrawing)
        {
            throw new InvalidOperationException("Not tesselating!");
        }

        IsDrawing = false;

        var mesh = RenderSystem.DrawTarget.Capture(BuildCommand(null));
        reset();
        return mesh;
    }

    private void reset()
    {
        arrayLayer = NoArrayLayer;
        vertexCount = 0;
        rawBufferIndex = 0;
        addedVertices = 0;
    }

    public void startDrawingQuads() => startDrawing(7);

    public void startDrawing(int mode)
    {
        if (IsDrawing)
        {
            throw new InvalidOperationException("Already tesselating!");
        }

        IsDrawing = true;
        reset();
        drawMode = mode;
        hasNormals = false;
        hasColor = false;
        hasTexture = false;
        isColorDisabled = false;
    }

    public void setTextureUV(double u, double v)
    {
        hasTexture = true;
        textureU = u;
        textureV = v;
    }

    /// <summary>Goes back to sampling the plain 2D texture bound to unit 0.</summary>
    public void clearArrayLayer() => arrayLayer = NoArrayLayer;

    public void setColorRGBA_F(float red, float green, float blue, float alpha) => setColorRGBA((int)(red * 255.0F), (int)(green * 255.0F), (int)(blue * 255.0F), (int)(alpha * 255.0F));

    public void setColorOpaque(int red, int green, int blue) => setColorRGBA(red, green, blue, 255);

    public void setColorRGBA(int red, int green, int blue, int alpha)
    {
        if (!isColorDisabled)
        {
            if (red > 255)
            {
                red = 255;
            }

            if (green > 255)
            {
                green = 255;
            }

            if (blue > 255)
            {
                blue = 255;
            }

            if (alpha > 255)
            {
                alpha = 255;
            }

            if (red < 0)
            {
                red = 0;
            }

            if (green < 0)
            {
                green = 0;
            }

            if (blue < 0)
            {
                blue = 0;
            }

            if (alpha < 0)
            {
                alpha = 0;
            }

            hasColor = true;
            if (BitConverter.IsLittleEndian)
            {
                color = (alpha << 24) | (blue << 16) | (green << 8) | red;
            }
            else
            {
                color = (red << 24) | (green << 16) | (blue << 8) | alpha;
            }
        }
    }

    public void setColorOpaque(Color c)
    {
        if (isColorDisabled) return;
        if (BitConverter.IsLittleEndian)
        {
            color = (int)c | 255;
        }
        else
        {
            color = ((int)c << 8) | 255;
        }

        hasColor = true;
    }

    public void setColorRGBA(Color c)
    {
        if (isColorDisabled) return;
        if (BitConverter.IsLittleEndian)
        {
            color = (int)c;
        }
        else
        {
            var v = (int)c;
            color = (v << 8) | (v >> 24);
        }

        hasColor = true;
    }

    public void addVertex(double x, double y, double z)
    {
        ++addedVertices;
        if (drawMode == 7 && convertQuadsToTriangles && addedVertices % 4 == 0)
        {
            for (var triangleCopyIndex = 0; triangleCopyIndex < 2; ++triangleCopyIndex)
            {
                var copyOffset = RawVertexInts * (3 - triangleCopyIndex);
                if (hasTexture)
                {
                    rawBuffer[rawBufferIndex + 3] = rawBuffer[rawBufferIndex - copyOffset + 3];
                    rawBuffer[rawBufferIndex + 4] = rawBuffer[rawBufferIndex - copyOffset + 4];
                }

                if (hasColor)
                {
                    rawBuffer[rawBufferIndex + 5] = rawBuffer[rawBufferIndex - copyOffset + 5];
                }

                rawBuffer[rawBufferIndex + 0] = rawBuffer[rawBufferIndex - copyOffset + 0];
                rawBuffer[rawBufferIndex + 1] = rawBuffer[rawBufferIndex - copyOffset + 1];
                rawBuffer[rawBufferIndex + 2] = rawBuffer[rawBufferIndex - copyOffset + 2];
                rawBuffer[rawBufferIndex + 7] = rawBuffer[rawBufferIndex - copyOffset + 7];
                rawBuffer[rawBufferIndex + 8] = rawBuffer[rawBufferIndex - copyOffset + 8];
                ++vertexCount;
                rawBufferIndex += RawVertexInts;
            }
        }

        if (hasTexture)
        {
            rawBuffer[rawBufferIndex + 3] = BitConverter.SingleToInt32Bits((float)textureU);
            rawBuffer[rawBufferIndex + 4] = BitConverter.SingleToInt32Bits((float)textureV);
        }

        if (hasColor)
        {
            rawBuffer[rawBufferIndex + 5] = color;
        }

        if (hasNormals)
        {
            rawBuffer[rawBufferIndex + 6] = normal;
        }

        rawBuffer[rawBufferIndex + 7] = arrayLayer;
        rawBuffer[rawBufferIndex + 8] = PackedLight;

        rawBuffer[rawBufferIndex + 0] = BitConverter.SingleToInt32Bits((float)(x + xOffset));
        rawBuffer[rawBufferIndex + 1] = BitConverter.SingleToInt32Bits((float)(y + yOffset));
        rawBuffer[rawBufferIndex + 2] = BitConverter.SingleToInt32Bits((float)(z + zOffset));
        rawBufferIndex += RawVertexInts;
        ++vertexCount;

        if (vertexCount % 4 == 0 && rawBufferIndex >= bufferSize - RawVertexInts * 4)
        {
            throw new InvalidOperationException(
                $"A batch of {vertexCount} vertices filled the Tessellator before naming a " +
                "slot. Splitting it here would draw the first half under whichever program " +
                "happened to be bound, so the batch has to be broken up by its caller.");
        }
    }

    public void setColorOpaque_I(int color)
    {
        var red = (color >> 16) & 255;
        var green = (color >> 8) & 255;
        var blue = color & 255;
        setColorOpaque(red, green, blue);
    }

    public void setColorRGBA_I(int color, int alpha)
    {
        var red = (color >> 16) & 255;
        var green = (color >> 8) & 255;
        var blue = color & 255;
        setColorRGBA(red, green, blue, alpha);
    }

    public void disableColor() => isColorDisabled = true;

    public void setNormal(float x, float y, float z)
    {
        hasNormals = true;
        var packedX = (byte)(int)(x * 128.0F);
        var packedY = (byte)(int)(y * 127.0F);
        var packedZ = (byte)(int)(z * 127.0F);
        normal = packedX | (packedY << 8) | (packedZ << 16);
    }

    public void setTranslationD(double x, double y, double z)
    {
        xOffset = x;
        yOffset = y;
        zOffset = z;
    }
}
