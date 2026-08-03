using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Util.Maths;
using Silk.NET.Maths;

namespace BetaSharp.Client.Rendering;

public class Frustum : FrustumData
{

    private static readonly Frustum _instance = new();

    public static FrustumData Instance()
    {
        _instance.Initialize();
        return _instance;
    }


    private void Normalize(int side)
    {
        int offset = side * 4;
        float x = Frustum[offset];
        float y = Frustum[offset + 1];
        float z = Frustum[offset + 2];

        float length = MathHelper.Sqrt(x * x + y * y + z * z);

        Frustum[offset] /= length;
        Frustum[offset + 1] /= length;
        Frustum[offset + 2] /= length;
        Frustum[offset + 3] /= length;
    }

    /// <summary>Row-major, which is the order the clipping arithmetic below indexes in.</summary>
    private static void Copy(Matrix4X4<float> matrix, float[] destination)
    {
        destination[0] = matrix.M11; destination[1] = matrix.M12; destination[2] = matrix.M13; destination[3] = matrix.M14;
        destination[4] = matrix.M21; destination[5] = matrix.M22; destination[6] = matrix.M23; destination[7] = matrix.M24;
        destination[8] = matrix.M31; destination[9] = matrix.M32; destination[10] = matrix.M33; destination[11] = matrix.M34;
        destination[12] = matrix.M41; destination[13] = matrix.M42; destination[14] = matrix.M43; destination[15] = matrix.M44;
    }

    private void Initialize()
    {
        Copy(GLManager.Projection.Top, ProjectionMatrix);
        Copy(GLManager.ModelView.Top, ModelviewMatrix);

        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                ClippingMatrix[i * 4 + j] =
                    ModelviewMatrix[i * 4 + 0] * ProjectionMatrix[0 + j] +
                    ModelviewMatrix[i * 4 + 1] * ProjectionMatrix[4 + j] +
                    ModelviewMatrix[i * 4 + 2] * ProjectionMatrix[8 + j] +
                    ModelviewMatrix[i * 4 + 3] * ProjectionMatrix[12 + j];
            }
        }
        // Right Plane
        SetPlane(0, ClippingMatrix[3] - ClippingMatrix[0], ClippingMatrix[7] - ClippingMatrix[4], ClippingMatrix[11] - ClippingMatrix[8], ClippingMatrix[15] - ClippingMatrix[12]);
        // Left Plane
        SetPlane(1, ClippingMatrix[3] + ClippingMatrix[0], ClippingMatrix[7] + ClippingMatrix[4], ClippingMatrix[11] + ClippingMatrix[8], ClippingMatrix[15] + ClippingMatrix[12]);
        // Bottom Plane
        SetPlane(2, ClippingMatrix[3] + ClippingMatrix[1], ClippingMatrix[7] + ClippingMatrix[5], ClippingMatrix[11] + ClippingMatrix[9], ClippingMatrix[15] + ClippingMatrix[13]);
        // Top Plane
        SetPlane(3, ClippingMatrix[3] - ClippingMatrix[1], ClippingMatrix[7] - ClippingMatrix[5], ClippingMatrix[11] - ClippingMatrix[9], ClippingMatrix[15] - ClippingMatrix[13]);
        // Far Plane
        SetPlane(4, ClippingMatrix[3] - ClippingMatrix[2], ClippingMatrix[7] - ClippingMatrix[6], ClippingMatrix[11] - ClippingMatrix[10], ClippingMatrix[15] - ClippingMatrix[14]);
        // Near Plane
        SetPlane(5, ClippingMatrix[3] + ClippingMatrix[2], ClippingMatrix[7] + ClippingMatrix[6], ClippingMatrix[11] + ClippingMatrix[10], ClippingMatrix[15] + ClippingMatrix[14]);
    }
    private void SetPlane(int side, float a, float b, float c, float d)
    {
        int offset = side * 4;
        Frustum[offset] = a;
        Frustum[offset + 1] = b;
        Frustum[offset + 2] = c;
        Frustum[offset + 3] = d;
        Normalize(side);
    }
}
