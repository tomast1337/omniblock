using System.Runtime.InteropServices;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Util;
using Silk.NET.OpenGL;
using Color = BetaSharp.Client.UI.Colors.Color;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Core;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 32)]
public struct Vertex(float x, float y, float z, float u, float v, int color, int normal)
{
    public float X = x; // 4 bytes
    public float Y = y; // 4 bytes + 4 bytes = 8 bytes
    public float Z = z; // 4 bytes + 8 bytes = 12 bytes
    public float U = u; // 4 bytes + 12 bytes = 16 bytes
    public float V = v; // 4 bytes + 16 bytes = 20 bytes
    public int Color = color; // 4 bytes + 20 bytes = 24 bytes
    public int Normal = normal; // 4 bytes + 20 bytes = 28 bytes
    public int Padding; // 32 bytes total
}

[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct ChunkVertex
{
    public int Color; // 4 bytes
    public short X; // 2 bytes + 4 bytes = 6 bytes
    public short Y; // 2 bytes + 6 bytes = 8 bytes
    public short Z; // 2 bytes + 8 bytes = 10 bytes
    public short U; // 2 bytes + 10 bytes = 12 bytes
    public short V; // 2 bytes + 12 bytes = 14 bytes

    // The two light channels, in quarter levels: a smooth-lit corner is the mean of four cells each
    // 0-15, so the value is a multiple of 0.25 and 0..60 holds it exactly. This is where the spare
    // padding byte went; a vertex attribute for mc_Entity needs the struct to grow.
    public byte SkyLight; // 1 byte + 14 bytes = 15 bytes
    public byte BlockLight; // 16 bytes total
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
        float centroidU,
        float centroidV,
        byte skyLight,
        byte blockLight)
    {
        return new ChunkVertex
        {
            Color = color,
            X = FloatToShortPosition(x),
            Y = FloatToShortPosition(y),
            Z = FloatToShortPosition(z),
            U = FloatToShortUVWithInset(u, centroidU),
            V = FloatToShortUVWithInset(v, centroidV),
            SkyLight = skyLight,
            BlockLight = blockLight
        };
    }

    /// <summary>A light level in 0..15, possibly fractional, as the quarter levels a vertex holds.</summary>
    public static byte ToQuarterLevels(float level) =>
        (byte)Math.Clamp((int)MathF.Round(level * 4.0f), 0, 60);

    private static short FloatToShortUVWithInset(float uv, float centroid)
    {
        int bias = uv < centroid ? 1 : -1;
        int quantized = (int)System.Math.Round(uv * UV_SCALE) + bias;

        return (short)(quantized & 0x7FFF | Sign(bias) << 15);
    }

    private static int Sign(int x)
    {
        return x < 0 ? 1 : 0;
    }

    public static short FloatToShortPosition(float position)
    {
        return (short)System.Math.Round(position * POSITION_SCALE);
    }

    public static short FloatToShortUV(float uv)
    {
        return (short)(uv * UV_SCALE);
    }

}

public enum TesselatorCaptureVertexFormat
{
    Default,
    Chunk
}

public class Tessellator
{
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
    public static readonly Tessellator instance = new(2097152);
    public bool IsDrawing { get; private set; }
    private readonly uint[] _vboIds;
    private readonly uint _tessVao;
    private int vboIndex;
    private readonly int vboCount = 10;
    private readonly int bufferSize;
    private float uvCentroidU;
    private float uvCentroidV;
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

        scratchBuffer = new int[32];
        scratchBufferIndex = 0;
        uvCentroidU = 0f;
        uvCentroidV = 0f;
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
    public void draw(ProgramSlot slot) => draw(SlotPrograms.Resolve(slot));

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
                vboIndex = (vboIndex + 1) % vboCount;
                GLManager.GL.BindBuffer(GLEnum.ArrayBuffer, _vboIds[vboIndex]);

                fixed (int* ptr = rawBuffer)
                {
                    GLManager.GL.BufferData(GLEnum.ArrayBuffer, (nuint)(rawBufferIndex * 4), ptr, GLEnum.StreamDraw);
                }

                GL silkGl = ((LegacyGL)GLManager.GL).SilkGL;
                silkGl.BindVertexArray(_tessVao);
                TessellatorVertexLayout.Bind(silkGl, hasTexture, hasColor, hasNormals);

                // Through GLManager rather than Silk, so the queued-geometry flush still fires:
                // a renderer holding batched vertices has to drain them before this lands.
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

            scratchBufferIndex += 8;

            if (drawMode == 7 && convertQuadsToTriangles && scratchBufferIndex == 32)
            {
                uvCentroidU = 0f;
                uvCentroidV = 0f;
                for (int i = 0; i < 4; i++)
                {
                    int idx = i * 8;
                    uvCentroidU += BitConverter.Int32BitsToSingle(scratchBuffer[idx + 3]);
                    uvCentroidV += BitConverter.Int32BitsToSingle(scratchBuffer[idx + 4]);
                }
                uvCentroidU *= 0.25f;
                uvCentroidV *= 0.25f;

                EmitVertexFromScratch(0);
                EmitVertexFromScratch(8);
                EmitVertexFromScratch(16);

                EmitVertexFromScratch(16);
                EmitVertexFromScratch(24);
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
                    int copyOffset = 8 * (3 - triangleCopyIndex);
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
                    ++vertexCount;
                    rawBufferIndex += 8;
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

            rawBuffer[rawBufferIndex + 0] = BitConverter.SingleToInt32Bits((float)(x + xOffset));
            rawBuffer[rawBufferIndex + 1] = BitConverter.SingleToInt32Bits((float)(y + yOffset));
            rawBuffer[rawBufferIndex + 2] = BitConverter.SingleToInt32Bits((float)(z + zOffset));
            rawBufferIndex += 8;
            ++vertexCount;

            if (vertexCount % 4 == 0 && rawBufferIndex >= bufferSize - 32)
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
                    uvCentroidU, uvCentroidV,
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

            capturedVertices.Add(new Vertex(x, y, z, u, v, col, norm));
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
