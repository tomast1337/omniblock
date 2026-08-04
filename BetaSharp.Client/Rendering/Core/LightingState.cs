using Silk.NET.Maths;

namespace BetaSharp.Client.Rendering.Core;

/// <summary>Whether a shaded colour is taken per vertex or per face.</summary>
/// <remarks>
///     The numeric values reach every slot's program as-is, which picks between two
///     interpolated colours on the integer.
/// </remarks>
public enum ShadeModel
{
    Flat = 0,
    Smooth = 1,
}

/// <summary>The two directional lights and the ambient term everything shaded is lit by.</summary>
/// <remarks>
///     <para>
///         The directions are in eye space, already through the model-view. That is not a detail to
///         tidy away: <c>Lighting.turnOnGui</c> works by rotating the model-view around the call
///         that sets these, so where the light comes from depends on the transform in force when it
///         was assigned, not when the draw happens.
///     </para>
///     <para>
///         Whether anything is lit at all is separate, and is still <c>Enable</c>/<c>Disable</c> of
///         <c>GLEnum.Lighting</c>: renderers turn lighting off for a single overlay and back on
///         constantly, and that must not disturb these.
///     </para>
///     <para>
///         There is no specular term and no per-light ambient. The fixed-function calls that set
///         them were always passing zero and black, and nothing downstream ever read them.
///     </para>
/// </remarks>
public readonly record struct LightingState(
    Vector3D<float> Light0Direction,
    Vector3D<float> Light0Diffuse,
    Vector3D<float> Light1Direction,
    Vector3D<float> Light1Diffuse,
    Vector3D<float> Ambient)
{
    /// <summary>What lighting looks like before anyone has said, matching GL's own initial state.</summary>
    public static LightingState Default { get; } = new(
        default,
        default,
        default,
        default,
        new Vector3D<float>(0.2f, 0.2f, 0.2f));
}
