namespace BetaSharp.Client.Rendering.Core;

/// <summary>Which texture unit each named texture array lives on.</summary>
/// <remarks>
///     Fixed units, bound once a frame and left alone, rather than bound per draw. Unlike the 2D
///     texture on unit 0 — which changes constantly, and which every call site was already
///     responsible for — there is exactly one terrain array and one item array for the whole frame,
///     so a draw only has to say which layer it wants, not which texture.
/// </remarks>
public static class TextureArrayUnits
{
    public const int Terrain = 1;
    public const int Items = 2;
}
