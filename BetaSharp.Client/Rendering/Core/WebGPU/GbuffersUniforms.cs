using System.Numerics;
using System.Runtime.InteropServices;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     The uniform block the <c>gbuffers_*.wgsl</c> programs declare as <c>Uniforms</c>.
/// </summary>
/// <remarks>
///     <para>
///         WGSL uniform-buffer layout: <c>mat4x4</c> = 4 × <c>vec4</c> (64 bytes, align 16).
///         Mismatched offsets read garbage silently, so every field is explicit, and this struct
///         and the shaders' <c>Uniforms</c> have to grow in lockstep.
///     </para>
///     <para>
///         Fog and lighting are not here yet. They belong to the slots that shade world geometry,
///         and nothing draws through those under WebGPU so far.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct GbuffersUniforms
{
    public const int Size = 224;

    [FieldOffset(0)] public Matrix4x4 ModelViewMatrix;
    [FieldOffset(64)] public Matrix4x4 ProjectionMatrix;

    /// <summary>
    ///     What the geometry is tinted by, which is its whole colour when it carries none of its own.
    /// </summary>
    /// <remarks>
    ///     OpenGL supplies this as the default value of vertex attribute 1, for a draw that binds no
    ///     colour array. WebGPU has no default attribute value — an attribute the buffer does not
    ///     supply does not exist — so the same number arrives as a uniform, and
    ///     <see cref="UseVertexColor" /> says whether the buffer's colour channel means anything.
    /// </remarks>
    [FieldOffset(128)] public Vector4 Tint;

    /// <summary>1 when the vertices carry their own colour, 0 when the field holds nothing.</summary>
    [FieldOffset(144)] public float UseVertexColor;

    /// <summary>The alpha below which a fragment is discarded, or 0 for no test.</summary>
    [FieldOffset(148)] public float AlphaThreshold;

    [FieldOffset(152)] public float Pad0;
    [FieldOffset(156)] public float Pad1;

    /// <summary>
    ///     Applied to the texcoord before sampling. Identity for almost every draw; the dynamic
    ///     textures (compass, clock, scrolling cloud sheets) animate by mutating this instead of the
    ///     texture itself.
    /// </summary>
    [FieldOffset(160)] public Matrix4x4 TextureMatrix;
}
