namespace OmniBlock.Client.Rendering.Core;

public static class GLU
{
    public static void gluPerspective(float fovY, float aspect, float zNear, float zFar)
    {
        var fH = (float)Math.Tan(fovY / 360.0 * Math.PI) * zNear;
        var fW = fH * aspect;
        GLManager.Projection.Frustum(-fW, fW, -fH, fH, zNear, zFar);
    }
}
