using System.Numerics;
using System.Runtime.InteropServices;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     The uniform block <c>gbuffers_basic.wgsl</c> declares as <c>Uniforms</c>.
/// </summary>
/// <remarks>
///     <para>
///         WGSL uniform-buffer layout: <c>mat4x4</c> = 4 × <c>vec4</c> (64 bytes, align 16).
///         Mismatched offsets read garbage silently, so every field is explicit.
///     </para>
///     <para>
///         The lighting, fog and alpha-test fields that existed in the GLSL version are not here
///         yet — they will be added when Phase 3 feeds the full Tessellator vertex through this
///         shader and those paths need them. The struct size and the shader's <c>Uniforms</c>
///         must grow in lockstep.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct GbuffersBasicUniforms
{
    public const int Size = 128;

    [FieldOffset(0)] public Matrix4x4 ModelViewMatrix;
    [FieldOffset(64)] public Matrix4x4 ProjectionMatrix;
}
