using System.Numerics;
using System.Runtime.InteropServices;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     The uniform block sky.wgsl declares, matching
///     <c>[StructLayout(LayoutKind.Explicit, Size = 192)]</c>.
/// </summary>
/// <remarks>
///     <para>
///         Set by <see cref="WorldRenderer" /> before each <see cref="ProgramSlot.SkyBasic" /> or
///         <see cref="ProgramSlot.SkyTextured" /> draw on the WebGPU path, and read by
///         <see cref="WebGpuDrawTarget" /> when it records one.
///     </para>
///     <para>
///         A <c>vec3</c> only forces its successor to a 16-byte boundary when that successor itself
///         needs one — a plain <c>f32</c>/<c>u32</c> packs immediately after the 12 bytes a vec3
///         actually uses. <see cref="GroundColor" /> is followed by scalars, not another vector, so
///         nothing here pads after it.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 192)]
public struct SkyWgslUniforms
{
    [FieldOffset(0)] public Matrix4x4 ModelViewMatrix;

    [FieldOffset(64)] public Matrix4x4 ProjectionMatrix;

    [FieldOffset(128)] public Vector4 Tint;

    [FieldOffset(144)] public Vector3 SkyColor;
    // 4 bytes padding — the next field is another vec3, which itself needs 16-byte alignment

    [FieldOffset(160)] public Vector3 GroundColor;

    [FieldOffset(172)] public float FogStart;

    [FieldOffset(176)] public float FogEnd;

    [FieldOffset(180)] public uint GradientMode;

    [FieldOffset(184)] public uint UseTexture;

    [FieldOffset(188)] public uint UseVertexColor;
}

/// <summary>
///     The uniform block cloud.wgsl declares, matching
///     <c>[StructLayout(LayoutKind.Explicit, Size = 256)]</c>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 256)]
public struct CloudWgslUniforms
{
    [FieldOffset(0)] public Matrix4x4 ModelViewMatrix;

    [FieldOffset(64)] public Matrix4x4 ProjectionMatrix;

    [FieldOffset(128)] public Matrix4x4 TextureMatrix;

    [FieldOffset(192)] public Vector3 CloudOffset;

    [FieldOffset(204)] public float CloudScale;

    [FieldOffset(208)] public float FogStart;

    [FieldOffset(212)] public float FogEnd;
    // 8 bytes padding — the next field is a vec4, which needs 16-byte alignment

    [FieldOffset(224)] public Vector4 Tint;

    /// <summary>Unit vector toward the sun (day) or moon (night), in the same world-relative axes RenderSky rotates the sky dome by.</summary>
    [FieldOffset(240)] public Vector3 LightDir;
    // 4 bytes trailing padding — struct size must stay a multiple of 16 for a uniform buffer.
}
