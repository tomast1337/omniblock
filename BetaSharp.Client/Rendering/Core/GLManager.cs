using BetaSharp.Client.Rendering.Core.OpenGL;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace BetaSharp.Client.Rendering.Core;

public class GLManager
{
    /// <summary>The OpenGL entry points, or null when the WebGPU backend is selected.</summary>
    /// <remarks>
    ///     Only for the handful of classes that own resources on both backends and have to branch.
    ///     Everything else takes <see cref="GL" /> and is entitled to assume it exists.
    /// </remarks>
    public static IGL? GLOrNull { get; private set; }

    /// <summary>The OpenGL entry points.</summary>
    /// <remarks>
    ///     Throws under WebGPU rather than returning something that accepts calls and drops them: a
    ///     backend that cannot answer a GL call has to fail where the unported call is made, not
    ///     leave a frame silently missing whatever that call would have drawn.
    /// </remarks>
    public static IGL GL => GLOrNull ?? throw new InvalidOperationException(
        "OpenGL was reached while the WebGPU backend is selected — this call site has not been ported.");

    /// <summary>
    ///     The values a draw is made under, which both backends read. Held apart from
    ///     <see cref="GL" /> because none of it is OpenGL — see <see cref="RenderContext" />.
    /// </summary>
    public static RenderContext Context { get; } = new();

    /// <summary>
    ///     What geometry submitted through <see cref="Tessellator" /> is currently drawn by, or null
    ///     if nothing can draw right now.
    /// </summary>
    /// <remarks>
    ///     Settable, and null for most of a WebGPU frame, because a WebGPU draw needs an open render
    ///     pass and there is no such thing outside one. The renderer installs a target for the length
    ///     of its pass and clears it afterwards. Under OpenGL one target is installed at startup and
    ///     stays.
    /// </remarks>
    public static IDrawTarget? DrawTargetOrNull { get; set; }

    /// <inheritdoc cref="DrawTargetOrNull" />
    public static IDrawTarget DrawTarget => DrawTargetOrNull ?? throw new InvalidOperationException(
        "Geometry was submitted with no draw target installed — either the backend never set one, or the draw is outside the render pass that would have drawn it.");

    static GLManager()
    {
        Context.RasterStateChanging += OnRasterStateChanging;
    }

    /// <summary>The model-view transform stack.</summary>
    /// <remarks>
    ///     Composing transforms on a stack is not the fixed-function part, and survives into any
    ///     backend; hierarchical models need it. Reaching it through a mode selector and a global
    ///     was the fixed-function part, and that is gone.
    /// </remarks>
    public static MatrixStack ModelView => Context.ModelView;

    /// <inheritdoc cref="ModelView" />
    public static MatrixStack Projection => Context.Projection;

    /// <inheritdoc cref="ModelView" />
    public static MatrixStack TextureMatrix => Context.TextureMatrix;

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
        get => Context.Color;
        set => Context.Color = value;
    }

    /// <summary>The normal geometry is lit by when it carries none of its own.</summary>
    /// <inheritdoc cref="Color" />
    public static Vector3D<float> Normal
    {
        get => Context.Normal;
        set => Context.Normal = value;
    }

    /// <summary>Whether a draw samples its bound texture, or is coloured alone.</summary>
    /// <remarks>
    ///     These four were <c>Enable</c>/<c>Disable</c> of capabilities a GL 4.3 core context does
    ///     not have. Nothing was switching a fixed pipeline on and off — each is one shader uniform,
    ///     and always was.
    /// </remarks>
    public static bool TextureEnabled
    {
        get => Context.TextureEnabled;
        set => Context.TextureEnabled = value;
    }

    /// <summary>Whether <see cref="Lighting" /> is applied, or geometry keeps its own colour.</summary>
    /// <inheritdoc cref="TextureEnabled" />
    public static bool LightingEnabled
    {
        get => Context.LightingEnabled;
        set => Context.LightingEnabled = value;
    }

    /// <summary>Whether <see cref="AlphaThreshold" /> is applied.</summary>
    /// <inheritdoc cref="TextureEnabled" />
    public static bool AlphaTestEnabled
    {
        get => Context.AlphaTestEnabled;
        set => Context.AlphaTestEnabled = value;
    }

    /// <summary>Whether <see cref="Fog" /> is applied.</summary>
    /// <inheritdoc cref="TextureEnabled" />
    public static bool FogEnabled
    {
        get => Context.FogEnabled;
        set => Context.FogEnabled = value;
    }

    /// <inheritdoc cref="RenderContext.GuiTextureId" />
    public static int GuiTextureId
    {
        get => Context.GuiTextureId;
        set => Context.GuiTextureId = value;
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
        get => Context.Lighting;
        set => Context.Lighting = value;
    }

    /// <summary>Whether a shaded colour is taken per vertex or per face.</summary>
    public static ShadeModel ShadeModel
    {
        get => Context.ShadeModel;
        set => Context.ShadeModel = value;
    }

    /// <summary>How the world's light levels turn into brightness right now.</summary>
    /// <remarks>
    ///     Set once a frame, from the world the camera is in. Ambient for the same reason the fog is:
    ///     every pass that draws something standing in the world reads it, and none of them own it.
    /// </remarks>
    public static WorldLightState WorldLight
    {
        get => Context.WorldLight;
        set => Context.WorldLight = value;
    }

    /// <summary>What the distance fog looks like, for every pass that draws under it.</summary>
    /// <remarks>
    ///     Set once per pass, in <c>GameRenderer.ApplyFog</c>. Whether fog applies at all is
    ///     <see cref="FogEnabled" />, separately, which renderers flip constantly; this survives
    ///     that untouched.
    /// </remarks>
    public static FogState Fog
    {
        get => Context.Fog;
        set => Context.Fog = value;
    }

    /// <summary>The alpha a fragment has to exceed to survive, while the alpha test is on.</summary>
    public static float AlphaThreshold
    {
        get => Context.AlphaThreshold;
        set => Context.AlphaThreshold = value;
    }

    /// <summary>
    ///     Raised just before geometry is drawn through <see cref="IGL.DrawArrays" /> (i.e. drawn
    ///     immediately rather than queued). A renderer holding queued geometry subscribes here so
    ///     what it is holding reaches the depth buffer first, in the order the caller issued it.
    /// </summary>
    public static event Action? ImmediateGeometryDrawing;

    /// <summary>
    ///     Raised just before a change to state that governs how geometry rasterizes. A renderer
    ///     that queues geometry instead of drawing it immediately subscribes here, so what it is
    ///     holding reaches the framebuffer while the state it was queued under is still in force.
    /// </summary>
    public static event Action? RasterStateChanging;

    internal static void OnImmediateGeometryDrawing() => ImmediateGeometryDrawing?.Invoke();
    internal static void OnRasterStateChanging() => RasterStateChanging?.Invoke();

    public static void Init(GL silkGl)
    {
        FixedFunctionPipeline pipeline = new(silkGl);
        GLOrNull = pipeline;

        // OpenGL wants the tint and facing pushed into default vertex attributes as they change;
        // WebGPU reads the same values as uniforms at submission and subscribes to neither.
        Context.ColorChanged += pipeline.SetDefaultColorAttribute;
        Context.NormalChanged += pipeline.SetDefaultNormalAttribute;

        DrawTargetOrNull = new GlDrawTarget();

        State.Invalidate();
    }

    /// <summary>
    ///     Selects the WebGPU backend, which has no <see cref="IGL" /> at all.
    /// </summary>
    /// <remarks>
    ///     A WebGPU draw goes through the <c>Wgpu*</c> classes. Nothing is installed in
    ///     <see cref="GLOrNull" />, so anything still reaching for <see cref="GL" /> throws at the
    ///     unported site instead of no-opping its way to an empty frame.
    /// </remarks>
    public static void InitWebGpu()
    {
        GLOrNull = null;
        DrawTargetOrNull = null;
        State.Invalidate();
    }
}
