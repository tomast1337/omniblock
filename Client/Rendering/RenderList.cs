using java.nio;
using Silk.NET.OpenGL.Legacy;

namespace betareborn.Client.Rendering
{
    public class RenderList
    {
        private int field_1242_a;
        private int field_1241_b;
        private int field_1240_c;
        private float field_1239_d;
        private float field_1238_e;
        private float field_1237_f;
        private ByteBuffer baseBuffer = GLAllocation.createDirectByteBuffer(65536 * sizeof(int));
        private IntBuffer field_1236_g;
        private bool field_1235_h = false;
        private bool field_1234_i = false;

        public RenderList()
        {
            field_1236_g = baseBuffer.asIntBuffer();
        }

        public void func_861_a(int var1, int var2, int var3, double var4, double var6, double var8)
        {
            field_1235_h = true;
            field_1236_g.clear();
            field_1242_a = var1;
            field_1241_b = var2;
            field_1240_c = var3;
            field_1239_d = (float)var4;
            field_1238_e = (float)var6;
            field_1237_f = (float)var8;
        }

        public bool func_862_a(int var1, int var2, int var3)
        {
            return !field_1235_h ? false : var1 == field_1242_a && var2 == field_1241_b && var3 == field_1240_c;
        }

        public void func_858_a(int var1)
        {
            field_1236_g.put(var1);
            if (field_1236_g.remaining() == 0)
            {
                func_860_a();
            }

        }

        public unsafe void func_860_a()
        {
            if (field_1235_h)
            {
                if (!field_1234_i)
                {
                    field_1236_g.flip();
                    field_1234_i = true;
                }

                if (field_1236_g.remaining() > 0)
                {
                    GLManager.GL.PushMatrix();
                    GLManager.GL.Translate(field_1242_a - field_1239_d, field_1241_b - field_1238_e, field_1240_c - field_1237_f);
                    BufferHelper.UsePointer(baseBuffer, (p) =>
                    {
                        var ptr = (byte*)p;
                        int count = field_1236_g.remaining();
                        int offset = field_1236_g.position() * sizeof(int);
                        GLManager.GL.CallLists((uint)count, GLEnum.UnsignedInt, ptr + offset);
                        field_1236_g.position(field_1236_g.position() + count);
                    });
                    GLManager.GL.PopMatrix();
                }

            }
        }

        public void func_859_b()
        {
            field_1235_h = false;
            field_1234_i = false;
        }
    }

}