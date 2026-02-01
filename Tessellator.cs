using java.lang;
using java.nio;
using Silk.NET.OpenGL.Legacy;
using System.Runtime.InteropServices;

namespace betareborn
{
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 32)]
    public struct Vertex(float x, float y, float z, float u, float v, int color, int normal)
    {
        public float X = x;
        public float Y = y;
        public float Z = z;
        public float U = u;
        public float V = v;
        public int Color = color;
        public int Normal = normal;
        public int Padding;
    }

    public class Tessellator : java.lang.Object
    {
        private static readonly bool convertQuadsToTriangles = true;
        private readonly ByteBuffer byteBuffer;
        private readonly IntBuffer intBuffer;
        private readonly int[] rawBuffer;
        private int vertexCount = 0;
        private double textureU;
        private double textureV;
        private int color;
        private bool hasColor = false;
        private bool hasTexture = false;
        private bool hasNormals = false;
        private int rawBufferIndex = 0;
        private int addedVertices = 0;
        private bool isColorDisabled = false;
        private int drawMode;
        private double xOffset;
        private double yOffset;
        private double zOffset;
        private int normal;
        public static readonly Tessellator instance = new(2097152);
        private bool isDrawing = false;
        private readonly IntBuffer vertexBuffers;
        private int vboIndex = 0;
        private readonly int vboCount = 10;
        private readonly int bufferSize;

        private bool isCaptureMode = false;
        private List<Vertex> capturedVertices = null;
        private int[] scratchBuffer = null;
        private int scratchBufferIndex = 0;

        private Tessellator(int var1)
        {
            bufferSize = var1;
            byteBuffer = GLAllocation.createDirectByteBuffer(var1 * 4);
            intBuffer = byteBuffer.asIntBuffer();
            rawBuffer = new int[var1];
            vertexBuffers = GLAllocation.createDirectIntBuffer(vboCount);
            GLAllocation.generateBuffersARB(vertexBuffers);
        }

        public Tessellator()
        {
        }

        public void startCapture()
        {
            isCaptureMode = true;
            capturedVertices = [];
            scratchBuffer = new int[32];
            scratchBufferIndex = 0;
        }

        public List<Vertex> endCapture()
        {
            if (!isCaptureMode)
            {
                throw new IllegalStateException("Not in capture mode!");
            }

            isCaptureMode = false;
            List<Vertex> result = capturedVertices;
            capturedVertices = null;
            scratchBuffer = null;
            scratchBufferIndex = 0;
            return result;
        }

        public void begin()
        {
            scratchBufferIndex = 0;
            vertexCount = 0;
            hasTexture = false;
            hasColor = false;
            hasNormals = false;
        }

        public unsafe void draw()
        {
            if (!isDrawing)
            {
                throw new IllegalStateException("Not tesselating!");
            }
            else
            {
                isDrawing = false;

                if (isCaptureMode)
                {
                    scratchBufferIndex = 0;
                    return;
                }

                if (vertexCount > 0)
                {
                    intBuffer.clear();
                    intBuffer.put(rawBuffer, 0, rawBufferIndex);
                    byteBuffer.position(0);
                    byteBuffer.limit(rawBufferIndex * 4);

                    vboIndex = (vboIndex + 1) % vboCount;
                    GLManager.GL.BindBuffer(GLEnum.ArrayBuffer, (uint)vertexBuffers.get(vboIndex));

                    int size = byteBuffer.limit();

                    BufferHelper.UsePointer(byteBuffer, (p) =>
                    {
                        var ptr = (byte*)p;
                        GLManager.GL.BufferData(GLEnum.ArrayBuffer, (nuint)size, ptr, GLEnum.StreamDraw);
                    });

                    if (hasTexture)
                    {
                        GLManager.GL.TexCoordPointer(2, GLEnum.Float, 32, (void*)12);
                        GLManager.GL.EnableClientState(GLEnum.TextureCoordArray);
                    }
                    if (hasColor)
                    {
                        GLManager.GL.ColorPointer(4, ColorPointerType.UnsignedByte, 32, (void*)20);
                        GLManager.GL.EnableClientState(GLEnum.ColorArray);
                    }
                    if (hasNormals)
                    {
                        GLManager.GL.NormalPointer(NormalPointerType.Byte, 32, (void*)24);
                        GLManager.GL.EnableClientState(GLEnum.NormalArray);
                    }

                    GLManager.GL.VertexPointer(3, GLEnum.Float, 32, (void*)0);

                    GLManager.GL.EnableClientState(GLEnum.VertexArray);
                    if (drawMode == 7 && convertQuadsToTriangles)
                    {
                        GLManager.GL.DrawArrays(GLEnum.Triangles, 0, (uint)vertexCount);
                    }
                    else
                    {
                        GLManager.GL.DrawArrays((GLEnum)drawMode, 0, (uint)vertexCount);
                    }

                    GLManager.GL.DisableClientState(GLEnum.VertexArray);
                    if (hasTexture)
                    {
                        GLManager.GL.DisableClientState(GLEnum.TextureCoordArray);
                    }

                    if (hasColor)
                    {
                        GLManager.GL.DisableClientState(GLEnum.ColorArray);
                    }

                    if (hasNormals)
                    {
                        GLManager.GL.DisableClientState(GLEnum.NormalArray);
                    }
                }

                reset();
            }
        }

        private void reset()
        {
            vertexCount = 0;
            if (byteBuffer != null)
            {
                byteBuffer.clear();
            }
            rawBufferIndex = 0;
            addedVertices = 0;
        }

        public void startDrawingQuads()
        {
            startDrawing(7);
        }

        public void startDrawing(int var1)
        {
            if (isDrawing)
            {
                throw new IllegalStateException("Already tesselating!");
            }
            else
            {
                isDrawing = true;
                reset();
                drawMode = var1;
                hasNormals = false;
                hasColor = false;
                hasTexture = false;
                isColorDisabled = false;
            }
        }

        public void setTextureUV(double var1, double var3)
        {
            hasTexture = true;
            textureU = var1;
            textureV = var3;
        }

        public void setColorOpaque_F(float var1, float var2, float var3)
        {
            setColorOpaque((int)(var1 * 255.0F), (int)(var2 * 255.0F), (int)(var3 * 255.0F));
        }

        public void setColorRGBA_F(float var1, float var2, float var3, float var4)
        {
            setColorRGBA((int)(var1 * 255.0F), (int)(var2 * 255.0F), (int)(var3 * 255.0F), (int)(var4 * 255.0F));
        }

        public void setColorOpaque(int var1, int var2, int var3)
        {
            setColorRGBA(var1, var2, var3, 255);
        }

        public void setColorRGBA(int var1, int var2, int var3, int var4)
        {
            if (!isColorDisabled)
            {
                if (var1 > 255)
                {
                    var1 = 255;
                }

                if (var2 > 255)
                {
                    var2 = 255;
                }

                if (var3 > 255)
                {
                    var3 = 255;
                }

                if (var4 > 255)
                {
                    var4 = 255;
                }

                if (var1 < 0)
                {
                    var1 = 0;
                }

                if (var2 < 0)
                {
                    var2 = 0;
                }

                if (var3 < 0)
                {
                    var3 = 0;
                }

                if (var4 < 0)
                {
                    var4 = 0;
                }

                hasColor = true;
                if (ByteOrder.nativeOrder() == ByteOrder.LITTLE_ENDIAN)
                {
                    color = var4 << 24 | var3 << 16 | var2 << 8 | var1;
                }
                else
                {
                    color = var1 << 24 | var2 << 16 | var3 << 8 | var4;
                }

            }
        }

        public void addVertexWithUV(double var1, double var3, double var5, double var7, double var9)
        {
            setTextureUV(var7, var9);
            addVertex(var1, var3, var5);
        }

        public void addVertex(double var1, double var3, double var5)
        {
            if (isCaptureMode)
            {
                scratchBuffer[scratchBufferIndex + 0] = Float.floatToRawIntBits((float)(var1 + xOffset));
                scratchBuffer[scratchBufferIndex + 1] = Float.floatToRawIntBits((float)(var3 + yOffset));
                scratchBuffer[scratchBufferIndex + 2] = Float.floatToRawIntBits((float)(var5 + zOffset));

                if (hasTexture)
                {
                    scratchBuffer[scratchBufferIndex + 3] = Float.floatToRawIntBits((float)textureU);
                    scratchBuffer[scratchBufferIndex + 4] = Float.floatToRawIntBits((float)textureV);
                }

                if (hasColor)
                    scratchBuffer[scratchBufferIndex + 5] = color;

                if (hasNormals)
                    scratchBuffer[scratchBufferIndex + 6] = normal;

                scratchBufferIndex += 8;

                if (drawMode == 7 && convertQuadsToTriangles && scratchBufferIndex == 32)
                {
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
                    for (int var7 = 0; var7 < 2; ++var7)
                    {
                        int var8 = 8 * (3 - var7);
                        if (hasTexture)
                        {
                            rawBuffer[rawBufferIndex + 3] = rawBuffer[rawBufferIndex - var8 + 3];
                            rawBuffer[rawBufferIndex + 4] = rawBuffer[rawBufferIndex - var8 + 4];
                        }

                        if (hasColor)
                        {
                            rawBuffer[rawBufferIndex + 5] = rawBuffer[rawBufferIndex - var8 + 5];
                        }

                        rawBuffer[rawBufferIndex + 0] = rawBuffer[rawBufferIndex - var8 + 0];
                        rawBuffer[rawBufferIndex + 1] = rawBuffer[rawBufferIndex - var8 + 1];
                        rawBuffer[rawBufferIndex + 2] = rawBuffer[rawBufferIndex - var8 + 2];
                        ++vertexCount;
                        rawBufferIndex += 8;
                    }
                }

                if (hasTexture)
                {
                    rawBuffer[rawBufferIndex + 3] = Float.floatToRawIntBits((float)textureU);
                    rawBuffer[rawBufferIndex + 4] = Float.floatToRawIntBits((float)textureV);
                }

                if (hasColor)
                {
                    rawBuffer[rawBufferIndex + 5] = color;
                }

                if (hasNormals)
                {
                    rawBuffer[rawBufferIndex + 6] = normal;
                }

                rawBuffer[rawBufferIndex + 0] = Float.floatToRawIntBits((float)(var1 + xOffset));
                rawBuffer[rawBufferIndex + 1] = Float.floatToRawIntBits((float)(var3 + yOffset));
                rawBuffer[rawBufferIndex + 2] = Float.floatToRawIntBits((float)(var5 + zOffset));
                rawBufferIndex += 8;
                ++vertexCount;

                if (vertexCount % 4 == 0 && rawBufferIndex >= bufferSize - 32)
                {
                    draw();
                    isDrawing = true;
                }
            }
        }

        private void EmitVertexFromScratch(int baseIndex)
        {
            float x = Float.intBitsToFloat(scratchBuffer[baseIndex + 0]);
            float y = Float.intBitsToFloat(scratchBuffer[baseIndex + 1]);
            float z = Float.intBitsToFloat(scratchBuffer[baseIndex + 2]);
            float u = hasTexture ? Float.intBitsToFloat(scratchBuffer[baseIndex + 3]) : 0f;
            float v = hasTexture ? Float.intBitsToFloat(scratchBuffer[baseIndex + 4]) : 0f;
            int col = hasColor ? scratchBuffer[baseIndex + 5] : 0;
            int norm = hasNormals ? scratchBuffer[baseIndex + 6] : 0;

            capturedVertices.Add(new Vertex(x, y, z, u, v, col, norm));
        }


        public void setColorOpaque_I(int var1)
        {
            int var2 = var1 >> 16 & 255;
            int var3 = var1 >> 8 & 255;
            int var4 = var1 & 255;
            setColorOpaque(var2, var3, var4);
        }

        public void setColorRGBA_I(int var1, int var2)
        {
            int var3 = var1 >> 16 & 255;
            int var4 = var1 >> 8 & 255;
            int var5 = var1 & 255;
            setColorRGBA(var3, var4, var5, var2);
        }

        public void disableColor()
        {
            isColorDisabled = true;
        }

        public void setNormal(float var1, float var2, float var3)
        {
            if (!isDrawing)
            {
                java.lang.System.@out.println("But..");
            }

            hasNormals = true;
            byte var4 = (byte)((int)(var1 * 128.0F));
            byte var5 = (byte)((int)(var2 * 127.0F));
            byte var6 = (byte)((int)(var3 * 127.0F));
            normal = var4 | var5 << 8 | var6 << 16;
        }

        public void setTranslationD(double var1, double var3, double var5)
        {
            xOffset = var1;
            yOffset = var3;
            zOffset = var5;
        }

        public void setTranslationF(float var1, float var2, float var3)
        {
            xOffset += (double)var1;
            yOffset += (double)var2;
            zOffset += (double)var3;
        }
    }
}