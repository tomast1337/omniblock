using System.Numerics;
using System.Runtime.InteropServices;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     The uniform block particle.wgsl declares, matching
///     <c>[StructLayout(LayoutKind.Explicit, Size = 160)]</c>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 160)]
public struct ParticleWgslUniforms
{
    [FieldOffset(0)]
    public Matrix4x4 ModelViewMatrix;

    [FieldOffset(64)]
    public Matrix4x4 ProjectionMatrix;

    [FieldOffset(128)]
    public Vector3 Right;
    // 4 bytes padding — the next field is another vec3, which itself needs 16-byte alignment

    [FieldOffset(144)]
    public Vector3 Up;
    // 4 bytes trailing padding — struct size must stay a multiple of 16 for a uniform buffer.
}

/// <summary>
///     One particle's record in particle.wgsl's storage buffer, matching
///     <c>[StructLayout(LayoutKind.Explicit, Size = 48)]</c>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 48)]
public struct ParticleInstance
{
    [FieldOffset(0)]
    public Vector3 Pos;

    [FieldOffset(12)]
    public float Size;

    [FieldOffset(16)]
    public Vector4 Color;

    [FieldOffset(32)]
    public Vector2 UvMin;

    [FieldOffset(40)]
    public Vector2 UvMax;
}
