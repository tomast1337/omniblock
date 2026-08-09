using System.Numerics;
using System.Runtime.InteropServices;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     The uniform block entity_instanced.wgsl declares, matching
///     <c>[StructLayout(LayoutKind.Explicit, Size = 240)]</c>.
/// </summary>
/// <remarks>
///     Offsets were hand-computed from WGSL's host-shareable layout rules (each member aligned to
///     its own alignment, struct size rounded up to the largest member's alignment — 16 here) rather
///     than written with <c>@align</c>/<c>@size</c> attributes in the shader, so this struct must be
///     kept in sync by hand if entity_instanced.wgsl's <c>Uniforms</c> block changes.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 240)]
public struct EntityInstancedWgslUniforms
{
    [FieldOffset(0)]
    public Matrix4x4 ProjectionMatrix;

    [FieldOffset(64)]
    public Matrix4x4 TextureMatrix;

    [FieldOffset(128)]
    public Vector3 Ambient;

    [FieldOffset(140)]
    public uint LightingEnabled;

    [FieldOffset(144)]
    public Vector3 Light0Dir;

    [FieldOffset(156)]
    public uint UseTexture;

    [FieldOffset(160)]
    public Vector3 Light0Diffuse;

    [FieldOffset(172)]
    public float AlphaThreshold;

    [FieldOffset(176)]
    public Vector3 Light1Dir;

    [FieldOffset(188)]
    public uint FogEnabled;

    [FieldOffset(192)]
    public Vector3 Light1Diffuse;

    [FieldOffset(204)]
    public int FogMode;

    [FieldOffset(208)]
    public Vector4 FogColor;

    [FieldOffset(224)]
    public float FogStart;

    [FieldOffset(228)]
    public float FogEnd;

    [FieldOffset(232)]
    public float FogDensity;
}
