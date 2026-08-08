using Silk.NET.Maths;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     Converts a projection built for OpenGL's clip volume into WebGPU's.
/// </summary>
/// <remarks>
///     The two disagree on depth alone: OpenGL's near plane maps to -1 and WebGPU's to 0. Every
///     projection the client builds — the world's perspective, the first-person hand's, the
///     interface's ortho — comes from shared camera code that the OpenGL backend uses unchanged, so
///     the correction belongs at the point a WebGPU shader is handed the matrix rather than in the
///     code that builds it. Anything reading the projection for another purpose, frustum-plane
///     extraction in particular, wants the OpenGL form and must not go through here.
/// </remarks>
public static class WgpuClip
{
    /// <summary>Identity but for halving z and biasing it, right-multiplied for row vectors.</summary>
    private static readonly Matrix4X4<float> s_depthRemap = new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 0.5f, 0,
        0, 0, 0.5f, 1);

    public static Matrix4X4<float> FromGl(Matrix4X4<float> projection) => projection * s_depthRemap;
}
