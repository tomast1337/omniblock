using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Core;

/// <summary>How fog thickens with distance.</summary>
/// <remarks>
///     The numeric values reach the shaders as-is — every fog shader in the tree branches on the
///     integer — so renumbering these swaps the two curves without a compile error.
/// </remarks>
public enum FogCurve
{
    Linear = 0,
    Exponential = 1,
}

/// <summary>What the distance fog looks like, for the passes that draw under it.</summary>
/// <remarks>
///     <para>
///         One value rather than a spread of globals, because fog is decided once per pass — from
///         the weather, the dimension and what the camera is submerged in — and every shader that
///         fades geometry into the distance has to agree on it. Assigning
///         <see cref="GLManager.Fog" /> is the whole of setting it.
///     </para>
///     <para>
///         Whether fog applies at all is separate, and is still <c>Enable</c>/<c>Disable</c> of
///         <c>GLEnum.Fog</c>: turning it off mid-pass is common and must not lose these values.
///     </para>
///     <para>
///         <see cref="Start" /> and <see cref="End" /> mean nothing under
///         <see cref="FogCurve.Exponential" />, and <see cref="Density" /> means nothing under
///         <see cref="FogCurve.Linear" />. The unused pair keeps whatever it last held, so switching
///         curves and back does not need them restated.
///     </para>
/// </remarks>
public readonly record struct FogState(
    FogCurve Curve,
    Vector4D<float> Color,
    float Start,
    float End,
    float Density)
{
    /// <summary>What fog looks like before anyone has said, matching GL's own initial state.</summary>
    public static FogState Default { get; } = new(FogCurve.Linear, default, 0.0f, 1.0f, 1.0f);
}
