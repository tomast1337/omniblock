using BetaSharp.Client.Rendering.Core.OpenGL;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace BetaSharp.Client.Rendering.Core;

public class GLManager
{
    public static IGL GL { get; private set; }

    /// <summary>The model-view transform stack.</summary>
    /// <remarks>
    ///     Composing transforms on a stack is not the fixed-function part, and survives into any
    ///     backend; hierarchical models need it. Reaching it through a mode selector and a global
    ///     was the fixed-function part, and that is gone.
    /// </remarks>
    public static MatrixStack ModelView => _pipeline.ModelView;

    /// <inheritdoc cref="ModelView" />
    public static MatrixStack Projection => _pipeline.Projection;

    /// <inheritdoc cref="ModelView" />
    public static MatrixStack TextureMatrix => _pipeline.TextureMatrix;

    /// <summary>
    ///     Blend, depth, cull and write masks, said once per draw rather than toggled a global at a
    ///     time.
    /// </summary>
    /// <remarks>
    ///     Call <see cref="RenderStateApplier.Invalidate" /> after any code that sets these through
    ///     the raw entry points, since what this believes is set will no longer be true. That is a
    ///     transitional hazard and goes away with the last of those call sites.
    /// </remarks>
    public static RenderStateApplier State { get; } = new();

    /// <summary>The colour geometry is tinted by when it carries none of its own.</summary>
    /// <remarks>
    ///     <para>
    ///         Reaches a draw as the value of vertex attribute 1 when no array is bound to it — the
    ///         Tessellator binds one only for geometry built with per-vertex colours, and models
    ///         never do. So for a model this is the whole of its colour, which is why the batching
    ///         renderers read it at submission rather than at the draw.
    ///     </para>
    ///     <para>
    ///         A default attribute value has no counterpart in WebGPU, where an attribute the buffer
    ///         does not supply simply does not exist. It becomes a uniform there, which is what this
    ///         being one value rather than a write-only global is for.
    ///     </para>
    /// </remarks>
    public static Vector4D<float> Color
    {
        get => _pipeline.Color;
        set => _pipeline.Color = value;
    }

    /// <summary>The normal geometry is lit by when it carries none of its own.</summary>
    /// <inheritdoc cref="Color" />
    public static Vector3D<float> Normal
    {
        get => _pipeline.Normal;
        set => _pipeline.Normal = value;
    }

    /// <summary>Whether a draw samples its bound texture, or is coloured alone.</summary>
    /// <remarks>
    ///     These four were <c>Enable</c>/<c>Disable</c> of capabilities a GL 4.3 core context does
    ///     not have. Nothing was switching a fixed pipeline on and off — each is one shader uniform,
    ///     and always was.
    /// </remarks>
    public static bool TextureEnabled
    {
        get => _pipeline.TextureEnabled;
        set => _pipeline.TextureEnabled = value;
    }

    /// <summary>Whether <see cref="Lighting" /> is applied, or geometry keeps its own colour.</summary>
    /// <inheritdoc cref="TextureEnabled" />
    public static bool LightingEnabled
    {
        get => _pipeline.LightingEnabled;
        set => _pipeline.LightingEnabled = value;
    }

    /// <summary>Whether <see cref="AlphaThreshold" /> is applied.</summary>
    /// <inheritdoc cref="TextureEnabled" />
    public static bool AlphaTestEnabled
    {
        get => _pipeline.AlphaTestEnabled;
        set => _pipeline.AlphaTestEnabled = value;
    }

    /// <summary>Whether <see cref="Fog" /> is applied.</summary>
    /// <inheritdoc cref="TextureEnabled" />
    public static bool FogEnabled
    {
        get => _pipeline.FogEnabled;
        set => _pipeline.FogEnabled = value;
    }

    /// <summary>
    ///     The alpha threshold in the form every shader takes it: below zero when the test is off.
    /// </summary>
    /// <remarks>
    ///     One encoding of two values, so a shader needs one uniform rather than a float and a flag.
    ///     Kept here rather than at each renderer that uploads it, because it is a contract with the
    ///     shaders and there are three of them.
    /// </remarks>
    public static float EffectiveAlphaThreshold => AlphaTestEnabled ? AlphaThreshold : -1.0f;

    /// <summary>The lights everything shaded is lit by.</summary>
    /// <remarks>
    ///     Set through <see cref="Lighting.turnOn" />, which is also where the directions are put
    ///     into eye space. Whether anything is lit is <see cref="LightingEnabled" />, separately, so
    ///     that turning it off for one overlay does not disturb the lights.
    /// </remarks>
    public static LightingState Lighting
    {
        get => _pipeline.Lighting;
        set => _pipeline.Lighting = value;
    }

    /// <summary>Whether a shaded colour is taken per vertex or per face.</summary>
    public static ShadeModel ShadeModel
    {
        get => _pipeline.ShadeModel;
        set => _pipeline.ShadeModel = value;
    }

    /// <summary>How the world's light levels turn into brightness right now.</summary>
    /// <remarks>
    ///     Set once a frame, from the world the camera is in. Ambient for the same reason the fog is:
    ///     every pass that draws something standing in the world reads it, and none of them own it.
    /// </remarks>
    public static WorldLightState WorldLight
    {
        get => _pipeline.WorldLight;
        set => _pipeline.WorldLight = value;
    }

    /// <summary>What the distance fog looks like, for every pass that draws under it.</summary>
    /// <remarks>
    ///     Set once per pass, in <c>GameRenderer.ApplyFog</c>. Whether fog applies at all is
    ///     <see cref="FogEnabled" />, separately, which renderers flip constantly; this survives
    ///     that untouched.
    /// </remarks>
    public static FogState Fog
    {
        get => _pipeline.Fog;
        set => _pipeline.Fog = value;
    }

    /// <summary>The alpha a fragment has to exceed to survive, while the alpha test is on.</summary>
    public static float AlphaThreshold
    {
        get => _pipeline.AlphaThreshold;
        set => _pipeline.AlphaThreshold = value;
    }

    private static FixedFunctionPipeline _pipeline = null!;

    public static void Init(GL silkGl)
    {
        _pipeline = new FixedFunctionPipeline(silkGl);
        GL = _pipeline;
        State.Invalidate();
    }
}
