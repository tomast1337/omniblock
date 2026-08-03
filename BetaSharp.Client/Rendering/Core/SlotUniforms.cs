using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Client.Rendering.Core;

/// <summary>The state more than one slot's program needs uploaded, and the state none of them may inherit.</summary>
internal static class SlotUniforms
{
    private static readonly ILogger s_logger = Log.Instance.For(nameof(SlotUniforms));
    private static readonly HashSet<string> s_reportedLit = [];

    /// <summary>The transforms every slot draws under, in the names the shaders declare them by.</summary>
    public static void UploadTransforms(Shader shader)
    {
        shader.SetUniformMatrix4("modelViewMatrix", GLManager.ModelView.Top);
        shader.SetUniformMatrix4("projectionMatrix", GLManager.Projection.Top);
    }

    /// <summary>The fog and the alpha threshold, which every slot has to carry for itself.</summary>
    public static void UploadFogAndAlpha(Shader shader)
    {
        FogState fog = GLManager.Fog;

        shader.SetUniform1("alphaThreshold", GLManager.EffectiveAlphaThreshold);
        shader.SetUniform1("shadeModel", (int)GLManager.ShadeModel);
        shader.SetUniform1("fogEnabled", GLManager.FogEnabled ? 1 : 0);
        shader.SetUniform1("fogMode", (int)fog.Curve);
        shader.SetUniform1("fogStart", fog.Start);
        shader.SetUniform1("fogEnd", fog.End);
        shader.SetUniform1("fogDensity", fog.Density);
        shader.SetUniform4("fogColor", fog.Color);
    }

    /// <summary>
    ///     Reports a draw routed to an unlit slot while lighting is on, once per slot.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An unlit slot silently drops the lighting the fixed-function shader would have
    ///         applied, so the whole of a mis-routed draw's symptom is that it comes out a slightly
    ///         different shade. That is the one kind of mistake this migration can make that nothing
    ///         else would catch — the geometry is right, the texture is right, and a frame hash
    ///         taken from a scene that never enables lighting agrees.
    ///     </para>
    ///     <para>
    ///         A report rather than a throw: being wrong about this costs a shade, and crashing the
    ///         client over it during a loading screen would cost more.
    ///     </para>
    /// </remarks>
    [Conditional("DEBUG")]
    public static void ReportIfLit(string slot)
    {
        if (GLManager.LightingEnabled && s_reportedLit.Add(slot))
        {
            s_logger.LogWarning(
                "Draw routed to unlit slot {Slot} while lighting was on; it will come out unlit. " +
                "Either the call site should turn lighting off or the draw belongs to a lit slot.",
                slot);
        }
    }
}
