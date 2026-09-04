using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering;

public class FrustumData
{
    public float[] ClippingMatrix = new float[16];
    public float[] Frustum = new float[24];
    public float[] ModelviewMatrix = new float[16];
    public float[] ProjectionMatrix = new float[16];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsBoxInFrustum(in Box box)
    {
        // Extract values once as floats for SIMD
        float fMinX = (float)box.MinX, fMinY = (float)box.MinY, fMinZ = (float)box.MinZ;
        float fMaxX = (float)box.MaxX, fMaxY = (float)box.MaxY, fMaxZ = (float)box.MaxZ;

        // Use SIMD if available (it will be on your 5950X)
        if (Vector128.IsHardwareAccelerated)
        {
            return IsBoxInFrustumSIMD(fMinX, fMinY, fMinZ, fMaxX, fMaxY, fMaxZ);
        }

        // Fallback to Unsafe Scalar
        return IsBoxInFrustumUnsafe(fMinX, fMinY, fMinZ, fMaxX, fMaxY, fMaxZ);
    }

    private bool IsBoxInFrustumSIMD(float fMinX, float fMinY, float fMinZ, float fMaxX, float fMaxY, float fMaxZ)
    {
        var vX1 = Vector128.Create(fMinX, fMaxX, fMinX, fMaxX);
        var vY1 = Vector128.Create(fMinY, fMinY, fMaxY, fMaxY);
        var vZ1 = Vector128.Create(fMinZ, fMinZ, fMinZ, fMinZ);
        var vX2 = vX1;
        var vY2 = vY1;
        var vZ2 = Vector128.Create(fMaxZ, fMaxZ, fMaxZ, fMaxZ);

        ref var frustumRef = ref MemoryMarshal.GetArrayDataReference(Frustum);

        for (var i = 0; i < 6; i++)
        {
            var offset = i << 2;
            var va = Vector128.Create(Unsafe.Add(ref frustumRef, offset));
            var vb = Vector128.Create(Unsafe.Add(ref frustumRef, offset + 1));
            var vc = Vector128.Create(Unsafe.Add(ref frustumRef, offset + 2));
            var vd = Vector128.Create(Unsafe.Add(ref frustumRef, offset + 3));

            var res1 = va * vX1 + vb * vY1 + vc * vZ1 + vd;
            var res2 = va * vX2 + vb * vY2 + vc * vZ2 + vd;

            if (Vector128.LessThanOrEqual(res1, Vector128<float>.Zero).ExtractMostSignificantBits() == 0b1111 &&
                Vector128.LessThanOrEqual(res2, Vector128<float>.Zero).ExtractMostSignificantBits() == 0b1111)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsBoxInFrustumUnsafe(float fMinX, float fMinY, float fMinZ, float fMaxX, float fMaxY, float fMaxZ)
    {
        ref var frustumRef = ref MemoryMarshal.GetArrayDataReference(Frustum);
        for (var i = 0; i < 6; i++)
        {
            var offset = i << 2;
            var a = Unsafe.Add(ref frustumRef, offset);
            var b = Unsafe.Add(ref frustumRef, offset + 1);
            var c = Unsafe.Add(ref frustumRef, offset + 2);
            var d = Unsafe.Add(ref frustumRef, offset + 3);

            if (a * fMinX + b * fMinY + c * fMinZ + d <= 0.0f &&
                a * fMaxX + b * fMinY + c * fMinZ + d <= 0.0f &&
                a * fMinX + b * fMaxY + c * fMinZ + d <= 0.0f &&
                a * fMaxX + b * fMaxY + c * fMinZ + d <= 0.0f &&
                a * fMinX + b * fMinY + c * fMaxZ + d <= 0.0f &&
                a * fMaxX + b * fMinY + c * fMaxZ + d <= 0.0f &&
                a * fMinX + b * fMaxY + c * fMaxZ + d <= 0.0f &&
                a * fMaxX + b * fMaxY + c * fMaxZ + d <= 0.0f)
            {
                return false;
            }
        }

        return true;
    }
}
