using System.Runtime.InteropServices;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Util;
using Silk.NET.OpenGL;
using Color = BetaSharp.Client.UI.Colors.Color;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Core;

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
    ///     Alongside the colour rather than multiplied into it, the same way a <see cref="ChunkVertex" />
    ///     carries it — a block drawn from the Tessellator is still a block, and the shader applies
    ///     the same ramp to it. Before this the field did not exist, and the light every block draw
    ///     was already setting reached the chunk mesh and nothing else, so a moving piston or a
    ///     primed TNT drew at full daylight wherever it was.
    /// </remarks>
    public int Light = Tessellator.FullBrightLight; // 36 bytes total
}

[StructLayout(LayoutKind.Sequential, Size = 18)]
public struct ChunkVertex
{
    public int Color; // 4 bytes
    public short X; // 2 bytes + 4 bytes = 6 bytes
    public short Y; // 2 bytes + 6 bytes = 8 bytes
    public short Z; // 2 bytes + 8 bytes = 10 bytes

    // Within the vertex's own array layer, 1.0 stored as 32767. Unsigned rather than signed so the
    // spare top half reaches 2.0: flowing water turns its quad about the tile's corner and needs to
    // run past the edge, which the layer's wrap folds back onto itself.
    public ushort U; // 2 bytes + 10 bytes = 12 bytes
    public ushort V; // 2 bytes + 12 bytes = 14 bytes

    // The two light channels, in quarter levels: a smooth-lit corner is the mean of four cells each
    // 0-15, so the value is a multiple of 0.25 and 0..60 holds it exactly. This is where the spare
    // padding byte went; a vertex attribute for mc_Entity needs the struct to grow.
    public byte SkyLight; // 1 byte + 14 bytes = 15 bytes
    public byte BlockLight; // 1 byte + 15 bytes = 16 bytes

    /// <summary>Which layer of the terrain array this vertex samples.</summary>
    public ushort ArrayLayer; // 18 bytes total
}

public static class ChunkVertexHelper
{
    private const float POSITION_SCALE = 32767f / 64f;

    private const float UV_SCALE = 32767f;

    public static ChunkVertex Create(
        int color,
        float x,
        float y,
        float z,
        float u,
        float v,
        int arrayLayer,
        byte skyLight,
        byte blockLight)
    {
        return new ChunkVertex
        {
            Color = color,
            X = FloatToShortPosition(x),
            Y = FloatToShortPosition(y),
            Z = FloatToShortPosition(z),
            U = FloatToShortUV(u),
            V = FloatToShortUV(v),
            ArrayLayer = (ushort)arrayLayer,
            SkyLight = skyLight,
            BlockLight = blockLight
        };
    }

    /// <summary>A light level in 0..15, possibly fractional, as the quarter levels a vertex holds.</summary>
    public static byte ToQuarterLevels(float level) =>
        (byte)Math.Clamp((int)MathF.Round(level * 4.0f), 0, 60);

    public static short FloatToShortPosition(float position)
    {
        return (short)System.Math.Round(position * POSITION_SCALE);
    }

    /// <summary>
    ///     A texture coordinate within its own array layer as the fixed point a vertex holds, where
    ///     1.0 is a whole tile and the representable range runs to 2.0.
    /// </summary>
    /// <remarks>
    ///     Straight quantization, with none of the inward bias an atlas needed: a layer's edge is the
    ///     texture's edge, so a neighbouring cell is no longer somewhere a coordinate can slip into.
    ///     Bleeding is what the bias existed to hide.
    /// </remarks>
    public static ushort FloatToShortUV(float uv)
    {
        return (ushort)Math.Clamp((int)MathF.Round(uv * UV_SCALE), 0, ushort.MaxValue);
    }

}

public enum TesselatorCaptureVertexFormat
{
    Default,
    Chunk
}

public class Tessellator
{
    /// <summary>
    ///     What a vertex carries when its texture is the plain 2D one on unit 0 rather than a layer of
    ///     a named array. The default, so a draw that never heard of arrays keeps sampling as it did.
    /// </summary>
    public const int NoArrayLayer = -1;

    /// <summary>
    ///     The packed light a vertex carries when its draw sets none: no sky, full block. Through the
    ///     block channel so it stays bright after dark, which is what text and inventory items want.
    /// </summary>
    public static readonly int FullBrightLight = ChunkVertexHelper.ToQuarterLevels(15.0f) << 8;

    /// <summary>Ints per vertex in the capture scratch buffer: x, y, z, u, v, colour, normal, light, array layer.</summary>
    private const int ScratchVertexInts = 9;

    /// <summary>Ints per vertex in the raw buffer, matching <see cref="Vertex" /> field for field.</summary>
    private const int RawVertexInts = 9;

    /// <summary>The scratch buffer holds exactly one quad, which is emitted as two triangles once full.</summary>
    private const int ScratchQuadInts = ScratchVertexInts * 4;

    private static readonly bool convertQuadsToTriangles = true;
    private readonly int[] rawBuffer;
    private int vertexCount;
    private double textureU;
    private double textureV;
    private int color;
    private bool hasColor;
    private bool hasTexture;
    private bool hasNormals;
    private byte skyLight;
    private byte blockLight;
    private bool hasLight;
    private int rawBufferIndex;
    private int addedVertices;
    private bool isColorDisabled;
    private int drawMode;
    private double xOffset;
    private double yOffset;
    private double zOffset;
    private int normal;
    private int arrayLayer = NoArrayLayer;
    public static readonly Tessellator instance = new(2097152);
    public bool IsDrawing { get; private set; }
    private readonly uint[] _vboIds;
    private readonly uint _tessVao;
    private int vboIndex;
    private readonly int vboCount = 10;
    private readonly int bufferSize;
    private bool isCaptureMode;
    private PooledList<Vertex> capturedVertices;
    private PooledList<ChunkVertex> capturedChunkVertices;
    private int[] scratchBuffer;
    private int scratchBufferIndex;
    private TesselatorCaptureVertexFormat vertexFormat;

    private Tessellator(int bufferSize)
    {
        this.bufferSize = bufferSize;
        rawBuffer = new int[bufferSize];
        _vboIds = new uint[vboCount];
        GLManager.GL.GenBuffers((uint)vboCount, _vboIds);
        _tessVao = GLManager.GL.GenVertexArray();
    }

    public Tessellator()
    {
    }

    public void startCapture(TesselatorCaptureVertexFormat format)
    {
        if (format == TesselatorCaptureVertexFormat.Chunk && IsDrawing)
        {
            throw new InvalidOperationException("Chunk vertex format is only supported in capture mode!");
        }

        vertexFormat = format;
        isCaptureMode = true;

        capturedVertices = null;
        capturedChunkVertices = null;

        if (format == TesselatorCaptureVertexFormat.Default)
        {
            capturedVertices = new();
        }
        else
        {
            capturedChunkVertices = new();
        }

        scratchBuffer = new int[ScratchQuadInts];
        scratchBufferIndex = 0;
    }

    public PooledList<Vertex> endCaptureVertices()
    {
        if (!isCaptureMode || vertexFormat != TesselatorCaptureVertexFormat.Default)
        {
            throw new InvalidOperationException("Not capturing default vertices!");
        }

        isCaptureMode = false;
        var result = capturedVertices;
        CleanupCapture();
        return result;
    }

    public PooledList<ChunkVertex> endCaptureChunkVertices()
    {
        if (!isCaptureMode || vertexFormat != TesselatorCaptureVertexFormat.Chunk)
        {
            throw new InvalidOperationException("Not capturing chunk vertices!");
        }

        isCaptureMode = false;
        var result = capturedChunkVertices;
        CleanupCapture();
        return result;
    }

    private void CleanupCapture()
    {
        capturedVertices = null;
        capturedChunkVertices = null;
        scratchBuffer = null;
        scratchBufferIndex = 0;
    }

    public void begin()
    {
        arrayLayer = NoArrayLayer;
        scratchBufferIndex = 0;
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
    public void drawWithBoundProgram() => draw(SlotPrograms.CallerBound);

    /// <summary>Draws the accumulated vertices under whatever program <paramref name="slot" /> resolves to.</summary>
    /// <remarks>
    ///     The slot is an argument rather than ambient state on purpose. Everything this migration
    ///     has cost was some draw inheriting state a previous one left set, and a draw that has to
    ///     name what it is cannot inherit the answer.
    /// </remarks>
    public void draw(ProgramSlot slot) => draw(SlotPrograms.Resolve(slot, VertexLayoutKind.Generic));

    private unsafe void draw(ISlotProgram program)
    {
        if (!IsDrawing)
        {
            throw new InvalidOperationException("Not tesselating!");
        }
        else
        {
            IsDrawing = false;

            if (isCaptureMode)
            {
                scratchBufferIndex = 0;
                return;
            }

            if (vertexCount > 0)
            {
                // Before anything of ours is bound, because draining binds and unbinds its own.
                ((LegacyGL)GLManager.GL).FlushQueuedGeometry();

                vboIndex = (vboIndex + 1) % vboCount;
                GLManager.GL.BindBuffer(GLEnum.ArrayBuffer, _vboIds[vboIndex]);

                fixed (int* ptr = rawBuffer)
                {
                    GLManager.GL.BufferData(GLEnum.ArrayBuffer, (nuint)(rawBufferIndex * 4), ptr, GLEnum.StreamDraw);
                }

                GL silkGl = ((LegacyGL)GLManager.GL).SilkGL;
                silkGl.BindVertexArray(_tessVao);
                TessellatorVertexLayout.Bind(silkGl, hasTexture, hasColor, hasNormals);

                program.Activate();
                GLManager.GL.DrawArrays(SubmittedDrawMode, 0, (uint)vertexCount);
                program.Deactivate();

                TessellatorVertexLayout.Unbind(silkGl, hasTexture, hasColor, hasNormals);
                silkGl.BindVertexArray(0);
            }

            reset();
        }
    }

    /// <summary>
    ///     The primitive the accumulated vertices are actually submitted as.
    /// </summary>
    /// <remarks>
    ///     Quads are expanded into triangles as vertices are added, so the recorded draw mode is not
    ///     what gets drawn.
    /// </remarks>
    private GLEnum SubmittedDrawMode =>
        drawMode == 7 && convertQuadsToTriangles ? GLEnum.Triangles : (GLEnum)drawMode;

    /// <summary>
    ///     Ends the batch by handing its vertices to a buffer that outlives the frame, instead of
    ///     drawing them.
    /// </summary>
    /// <remarks>
    ///     For geometry that never changes. <see cref="draw" /> cycles through a ring of streaming
    ///     buffers, which is the right trade when the contents are rebuilt every frame and the wrong
    ///     one when they are built once and drawn forever.
    /// </remarks>
    public unsafe StaticMesh captureStatic()
    {
        if (!IsDrawing)
        {
            throw new InvalidOperationException("Not tesselating!");
        }

        IsDrawing = false;

        uint buffer = GLManager.GL.GenBuffer();
        GLManager.GL.BindBuffer(GLEnum.ArrayBuffer, buffer);

        fixed (int* ptr = rawBuffer)
        {
            GLManager.GL.BufferData(GLEnum.ArrayBuffer, (nuint)(rawBufferIndex * 4), ptr, GLEnum.StaticDraw);
        }

        StaticMesh mesh = new(buffer, vertexCount, SubmittedDrawMode, hasTexture, hasColor, hasNormals);
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

    public void startDrawingQuads()
    {
        startDrawing(7);
    }

    public void startDrawing(int mode)
    {
        if (IsDrawing)
        {
            throw new InvalidOperationException("Already tesselating!");
        }
        else
        {
            IsDrawing = true;
            reset();
            drawMode = mode;
            hasNormals = false;
            hasColor = false;
            hasTexture = false;
            isColorDisabled = false;
        }
    }

    public void setTextureUV(double u, double v)
    {
        hasTexture = true;
        textureU = u;
        textureV = v;
    }

    /// <summary>
    ///     Which layer of the bound texture array the vertices from here on sample. Stays set until
    ///     changed, like the colour and the UV do.
    /// </summary>
    /// <remarks>
    ///     A layer rather than a texture name because the caller is usually resolving a legacy
    ///     <c>TextureId</c>: see <see cref="Textures.AtlasTileMap.LayerOfGridIndex" />.
    /// </remarks>
    public void setArrayLayer(int layer)
    {
        arrayLayer = layer;
    }

    /// <summary>Goes back to sampling the plain 2D texture bound to unit 0.</summary>
    public void clearArrayLayer()
    {
        arrayLayer = NoArrayLayer;
    }

    public void setColorOpaque_F(float red, float green, float blue)
    {
        setColorOpaque((int)(red * 255.0F), (int)(green * 255.0F), (int)(blue * 255.0F));
    }

    public void setColorRGBA_F(float red, float green, float blue, float alpha)
    {
        setColorRGBA((int)(red * 255.0F), (int)(green * 255.0F), (int)(blue * 255.0F), (int)(alpha * 255.0F));
    }

    public void setColorOpaque(int red, int green, int blue)
    {
        setColorRGBA(red, green, blue, 255);
    }

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
                color = alpha << 24 | blue << 16 | green << 8 | red;
            }
            else
            {
                color = red << 24 | green << 16 | blue << 8 | alpha;
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
            int v = (int)c;
            color = (v << 8) | (v >> 24);
        }
        hasColor = true;
    }

    public void addVertexWithUV(double x, double y, double z, double u, double v)
    {
        setTextureUV(u, v);
        addVertex(x, y, z);
    }

    public void addVertex(double x, double y, double z)
    {
        if (isCaptureMode)
        {
            scratchBuffer[scratchBufferIndex + 0] = BitConverter.SingleToInt32Bits((float)(x + xOffset));
            scratchBuffer[scratchBufferIndex + 1] = BitConverter.SingleToInt32Bits((float)(y + yOffset));
            scratchBuffer[scratchBufferIndex + 2] = BitConverter.SingleToInt32Bits((float)(z + zOffset));

            if (hasTexture)
            {
                scratchBuffer[scratchBufferIndex + 3] = BitConverter.SingleToInt32Bits((float)textureU);
                scratchBuffer[scratchBufferIndex + 4] = BitConverter.SingleToInt32Bits((float)textureV);
            }
            else if (vertexFormat == TesselatorCaptureVertexFormat.Chunk)
            {
                throw new InvalidOperationException("ChunkVertex requires texture coordinates!");
            }

            if (hasColor)
            {
                scratchBuffer[scratchBufferIndex + 5] = color;
            }

            if (hasNormals)
            {
                scratchBuffer[scratchBufferIndex + 6] = normal;
            }

            if (hasLight)
            {
                scratchBuffer[scratchBufferIndex + 7] = skyLight | blockLight << 8;
            }

            scratchBuffer[scratchBufferIndex + 8] = arrayLayer;

            scratchBufferIndex += ScratchVertexInts;

            if (drawMode == 7 && convertQuadsToTriangles && scratchBufferIndex == ScratchQuadInts)
            {
                EmitVertexFromScratch(0);
                EmitVertexFromScratch(ScratchVertexInts);
                EmitVertexFromScratch(ScratchVertexInts * 2);

                EmitVertexFromScratch(ScratchVertexInts * 2);
                EmitVertexFromScratch(ScratchVertexInts * 3);
                EmitVertexFromScratch(0);

                scratchBufferIndex = 0;
            }

            return;
        }
        else
        {
            ++addedVertices;
            if (drawMode == 7 && convertQuadsToTriangles && addedVertices % 4 == 0)
            {
                for (int triangleCopyIndex = 0; triangleCopyIndex < 2; ++triangleCopyIndex)
                {
                    int copyOffset = RawVertexInts * (3 - triangleCopyIndex);
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

            if (vertexCount % 4 == 0 && rawBufferIndex >= bufferSize - (RawVertexInts * 4))
            {
                // In capture mode this draws nothing — it recycles the scratch buffer so a chunk
                // mesh larger than the buffer can keep accumulating, which is the only way a batch
                // gets near this size.
                if (!isCaptureMode)
                {
                    throw new InvalidOperationException(
                        $"A batch of {vertexCount} vertices filled the Tessellator before naming a " +
                        "slot. Splitting it here would draw the first half under whichever program " +
                        "happened to be bound, so the batch has to be broken up by its caller.");
                }

                draw(SlotPrograms.CallerBound);
                IsDrawing = true;
            }
        }
    }

    private void EmitVertexFromScratch(int baseIndex)
    {
        float x = BitConverter.Int32BitsToSingle(scratchBuffer[baseIndex + 0]);
        float y = BitConverter.Int32BitsToSingle(scratchBuffer[baseIndex + 1]);
        float z = BitConverter.Int32BitsToSingle(scratchBuffer[baseIndex + 2]);

        if (vertexFormat == TesselatorCaptureVertexFormat.Chunk)
        {
            int col = hasColor ? scratchBuffer[baseIndex + 5] : unchecked((int)0xFFFFFFFF);
            int light = hasLight ? scratchBuffer[baseIndex + 7] : 0;

            float u = BitConverter.Int32BitsToSingle(scratchBuffer[baseIndex + 3]);
            float v = BitConverter.Int32BitsToSingle(scratchBuffer[baseIndex + 4]);

            capturedChunkVertices.Add(
                ChunkVertexHelper.Create(
                    col,
                    x, y, z,
                    u, v,
                    scratchBuffer[baseIndex + 8],
                    (byte)(light & 0xFF),
                    (byte)((light >> 8) & 0xFF)
                )
            );
        }
        else
        {
            float u = hasTexture ? BitConverter.Int32BitsToSingle(scratchBuffer[baseIndex + 3]) : 0f;
            float v = hasTexture ? BitConverter.Int32BitsToSingle(scratchBuffer[baseIndex + 4]) : 0f;
            int col = hasColor ? scratchBuffer[baseIndex + 5] : 0;
            int norm = hasNormals ? scratchBuffer[baseIndex + 6] : 0;

            capturedVertices.Add(new Vertex(x, y, z, u, v, col, norm)
            {
                ArrayLayer = scratchBuffer[baseIndex + 8],
                Light = hasLight ? scratchBuffer[baseIndex + 7] : FullBrightLight
            });
        }
    }


    public void setColorOpaque_I(int color)
    {
        int red = color >> 16 & 255;
        int green = color >> 8 & 255;
        int blue = color & 255;
        setColorOpaque(red, green, blue);
    }

    public void setColorRGBA_I(int color, int alpha)
    {
        int red = color >> 16 & 255;
        int green = color >> 8 & 255;
        int blue = color & 255;
        setColorRGBA(red, green, blue, alpha);
    }

    public void disableColor()
    {
        isColorDisabled = true;
    }

    public void setNormal(float x, float y, float z)
    {
        hasNormals = true;
        byte packedX = (byte)(int)(x * 128.0F);
        byte packedY = (byte)(int)(y * 127.0F);
        byte packedZ = (byte)(int)(z * 127.0F);
        normal = packedX | packedY << 8 | packedZ << 16;
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

    /// <summary>The light the vertices from here on carry, or full brightness if none was set.</summary>
    private int PackedLight => hasLight ? skyLight | blockLight << 8 : FullBrightLight;

    public void setTranslationD(double x, double y, double z)
    {
        xOffset = x;
        yOffset = y;
        zOffset = z;
    }

    public void setTranslationF(float x, float y, float z)
    {
        xOffset += x;
        yOffset += y;
        zOffset += z;
    }
}
