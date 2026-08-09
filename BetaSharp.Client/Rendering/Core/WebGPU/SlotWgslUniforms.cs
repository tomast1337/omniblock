using System.Numerics;
using System.Runtime.InteropServices;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     The uniform block sky.wgsl declares, matching
///     <c>[StructLayout(LayoutKind.Explicit, Size = 196)]</c>.
/// </summary>
/// <remarks>
///     Set by <see cref="WorldRenderer" /> before each <see cref="ProgramSlot.SkyBasic" /> or
///     <see cref="ProgramSlot.SkyTextured" /> draw on the WebGPU path, and read by
///     <see cref="WebGpuDrawTarget" /> when it records one.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 196)]
public struct SkyWgslUniforms
{
    [FieldOffset(0)]
    public Matrix4x4 ModelViewMatrix;

    [FieldOffset(64)]
    public Matrix4x4 ProjectionMatrix;

    [FieldOffset(128)]
    public Vector4 Tint;

    [FieldOffset(144)]
    public Vector3 SkyColor;
    // 4 bytes padding — vec3 takes 16 bytes in the WGSL uniform block

    [FieldOffset(160)]
    public Vector3 GroundColor;
    // 4 bytes padding

    [FieldOffset(176)]
    public float FogStart;

    [FieldOffset(180)]
    public float FogEnd;

    [FieldOffset(184)]
    public uint GradientMode;

    [FieldOffset(188)]
    public uint UseTexture;

    [FieldOffset(192)]
    public uint UseVertexColor;
}

/// <summary>
///     The uniform block cloud.wgsl declares, matching
///     <c>[StructLayout(LayoutKind.Explicit, Size = 240)]</c>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 240)]
public struct CloudWgslUniforms
{
    [FieldOffset(0)]
    public Matrix4x4 ModelViewMatrix;

    [FieldOffset(64)]
    public Matrix4x4 ProjectionMatrix;

    [FieldOffset(128)]
    public Matrix4x4 TextureMatrix;

    [FieldOffset(192)]
    public Vector3 CloudOffset;
    // 4 bytes padding — vec3 takes 16 bytes

    [FieldOffset(208)]
    public float CloudScale;

    [FieldOffset(212)]
    public float FogStart;

    [FieldOffset(216)]
    public float FogEnd;
    // 4 bytes padding to reach the next 16-byte boundary

    [FieldOffset(224)]
    public Vector4 Tint;
}
