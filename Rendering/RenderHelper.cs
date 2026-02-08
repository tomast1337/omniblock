using betareborn.Util.Maths;
using Silk.NET.OpenGL.Legacy;

namespace betareborn.Rendering
{
    public unsafe class RenderHelper
    {
        private static readonly float[] buffer = new float[4];

        public static void disableStandardItemLighting()
        {
            GLManager.GL.Disable(GLEnum.Lighting);
            GLManager.GL.Disable(GLEnum.Light0);
            GLManager.GL.Disable(GLEnum.Light1);
            GLManager.GL.Disable(GLEnum.ColorMaterial);
        }

        public static void enableStandardItemLighting()
        {
            GLManager.GL.Enable(GLEnum.Lighting);
            GLManager.GL.Enable(GLEnum.Light0);
            GLManager.GL.Enable(GLEnum.Light1);
            GLManager.GL.Enable(GLEnum.ColorMaterial);
            GLManager.GL.ColorMaterial(GLEnum.FrontAndBack, GLEnum.AmbientAndDiffuse);
            float var0 = 0.4F;
            float var1 = 0.6F;
            float var2 = 0.0F;
            Vec3D var3 = Vec3D.createVector((double)0.2F, 1.0D, (double)-0.7F).normalize();
            fixed (float* buf = buffer)
            {
                GLManager.GL.Light(GLEnum.Light0, GLEnum.Position, func_1157_a(buf, var3.xCoord, var3.yCoord, var3.zCoord, 0.0D));
                GLManager.GL.Light(GLEnum.Light0, GLEnum.Diffuse, func_1156_a(buf, var1, var1, var1, 1.0F));
                GLManager.GL.Light(GLEnum.Light0, GLEnum.Ambient, func_1156_a(buf, 0.0F, 0.0F, 0.0F, 1.0F));
                GLManager.GL.Light(GLEnum.Light0, GLEnum.Specular, func_1156_a(buf, var2, var2, var2, 1.0F));
                var3 = Vec3D.createVector((double)-0.2F, 1.0D, (double)0.7F).normalize();
                GLManager.GL.Light(GLEnum.Light1, GLEnum.Position, func_1157_a(buf, var3.xCoord, var3.yCoord, var3.zCoord, 0.0D));
                GLManager.GL.Light(GLEnum.Light1, GLEnum.Diffuse, func_1156_a(buf, var1, var1, var1, 1.0F));
                GLManager.GL.Light(GLEnum.Light1, GLEnum.Ambient, func_1156_a(buf, 0.0F, 0.0F, 0.0F, 1.0F));
                GLManager.GL.Light(GLEnum.Light1, GLEnum.Specular, func_1156_a(buf, var2, var2, var2, 1.0F));
                GLManager.GL.ShadeModel(GLEnum.Flat);
                GLManager.GL.LightModel(GLEnum.LightModelAmbient, func_1156_a(buf, var0, var0, var0, 1.0F));
            }
        }

        private static float* func_1157_a(float* buffer, double var0, double var2, double var4, double var6)
        {
            return func_1156_a(buffer, (float)var0, (float)var2, (float)var4, (float)var6);
        }

        private static float* func_1156_a(float* buffer, float var0, float var1, float var2, float var3)
        {
            buffer[0] = var0;
            buffer[1] = var1;
            buffer[2] = var2;
            buffer[3] = var3;
            return buffer;
        }
    }

}