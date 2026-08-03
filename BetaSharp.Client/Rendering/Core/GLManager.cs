using BetaSharp.Client.Rendering.Core.OpenGL;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace BetaSharp.Client.Rendering.Core;

public class GLManager
{
    public static IGL GL { get; private set; }

    /// <summary>The model-view transform stack.</summary>
    /// <remarks>
    ///     Held directly rather than driven through <c>MatrixMode</c> and the fixed-function entry
    ///     points. Both reach the same stack, so a renderer can move to this one at a time and
    ///     everything keeps drawing.
    /// </remarks>
    public static MatrixStack ModelView => _emulated.ModelView;

    /// <inheritdoc cref="ModelView" />
    public static MatrixStack Projection => _emulated.Projection;

    /// <inheritdoc cref="ModelView" />
    public static MatrixStack TextureMatrix => _emulated.TextureMatrix;

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
        get => _emulated.Color;
        set => _emulated.Color = value;
    }

    /// <summary>The normal geometry is lit by when it carries none of its own.</summary>
    /// <inheritdoc cref="Color" />
    public static Vector3D<float> Normal
    {
        get => _emulated.Normal;
        set => _emulated.Normal = value;
    }

    /// <summary>Whether a draw samples its bound texture, or is coloured alone.</summary>
    /// <remarks>
    ///     These four were <c>Enable</c>/<c>Disable</c> of capabilities a GL 4.3 core context does
    ///     not have. Nothing was switching a fixed pipeline on and off — each is one shader uniform,
    ///     and always was.
    /// </remarks>
    public static bool TextureEnabled
    {
        get => _emulated.TextureEnabled;
        set => _emulated.TextureEnabled = value;
    }

    /// <summary>Whether <see cref="Lighting" /> is applied, or geometry keeps its own colour.</summary>
    /// <inheritdoc cref="TextureEnabled" />
    public static bool LightingEnabled
    {
        get => _emulated.LightingEnabled;
        set => _emulated.LightingEnabled = value;
    }

    /// <summary>Whether <see cref="AlphaThreshold" /> is applied.</summary>
    /// <inheritdoc cref="TextureEnabled" />
    public static bool AlphaTestEnabled
    {
        get => _emulated.AlphaTestEnabled;
        set => _emulated.AlphaTestEnabled = value;
    }

    /// <summary>Whether <see cref="Fog" /> is applied.</summary>
    /// <inheritdoc cref="TextureEnabled" />
    public static bool FogEnabled
    {
        get => _emulated.FogEnabled;
        set => _emulated.FogEnabled = value;
    }

    /// <summary>The lights everything shaded is lit by.</summary>
    /// <remarks>
    ///     Set through <see cref="Lighting.turnOn" />, which is also where the directions are put
    ///     into eye space. Whether anything is lit is separate and stays on <c>Enable</c>/
    ///     <c>Disable</c> of <c>GLEnum.Lighting</c>.
    /// </remarks>
    public static LightingState Lighting
    {
        get => _emulated.Lighting;
        set => _emulated.Lighting = value;
    }

    /// <summary>Whether a shaded colour is taken per vertex or per face.</summary>
    public static ShadeModel ShadeModel
    {
        get => _emulated.ShadeModel;
        set => _emulated.ShadeModel = value;
    }

    /// <summary>What the distance fog looks like, for every pass that draws under it.</summary>
    /// <remarks>
    ///     Set once per pass, in <c>GameRenderer.ApplyFog</c>. Whether fog applies at all is
    ///     separate and still goes through <c>Enable</c>/<c>Disable</c> of <c>GLEnum.Fog</c>, which
    ///     renderers flip constantly; this survives that untouched.
    /// </remarks>
    public static FogState Fog
    {
        get => _emulated.Fog;
        set => _emulated.Fog = value;
    }

    /// <summary>The alpha a fragment has to exceed to survive, while the alpha test is on.</summary>
    public static float AlphaThreshold
    {
        get => _emulated.AlphaThreshold;
        set => _emulated.AlphaThreshold = value;
    }

    private static EmulatedGL _emulated = null!;

    public static void Init(GL silkGl)
    {
        _emulated = new EmulatedGL(silkGl);
        GL = _emulated;
        State.Invalidate();
    }
}
