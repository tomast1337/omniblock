using OmniBlock.Client.Rendering.Core.WebGPU;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Core;

/// <summary>
///     The per-context rendering state every backend needs: matrix stacks, tint, facing, fog and
///     lights. None of it is OpenGL — it is the values a draw is made under, which each backend
///     reads and delivers its own way.
/// </summary>
/// <remarks>
///     <para>
///         This lived inside <c>FixedFunctionPipeline</c>, which meant reaching a matrix stack went
///         through the OpenGL object. That made ~750 call sites across the client OpenGL call sites
///         by accident, and a second backend could only be introduced by faking the whole GL
///         interface underneath them. The state was never GL's; it is held here so both backends
///         read the same values and neither owns them.
///     </para>
///     <para>
///         A backend that needs to hear about a change subscribes rather than being called: OpenGL
///         pushes <see cref="Color" /> and <see cref="Normal" /> into default vertex attributes
///         eagerly, which WebGPU has no counterpart for — there they become uniforms read at
///         submission.
///     </para>
/// </remarks>
public sealed class RenderContext
{
    /// <summary>
    ///     The uniform block for the next <see cref="ProgramSlot.Clouds" /> draw on the WebGPU path.
    /// </summary>
    /// <inheritdoc cref="SkySlot" />
    internal CloudWgslUniforms CloudSlot;

    // ── Slot-specific uniform data ─────────────────────────────────────────

    /// <summary>
    ///     The uniform block for the next <see cref="ProgramSlot.SkyBasic" /> or
    ///     <see cref="ProgramSlot.SkyTextured" /> draw on the WebGPU path.
    ///     Set by <see cref="WorldRenderer" /> before the draw call.
    /// </summary>
    internal SkyWgslUniforms SkySlot;
    // ── Matrix stacks ──────────────────────────────────────────────────────

    /// <inheritdoc cref="GLManager.ModelView" />
    public MatrixStack ModelView { get; } = new();

    /// <inheritdoc cref="ModelView" />
    public MatrixStack Projection { get; } = new();

    /// <inheritdoc cref="ModelView" />
    public MatrixStack TextureMatrix { get; } = new();

    // ── Per-vertex defaults ─────────────────────────────────────────────────

    /// <inheritdoc cref="GLManager.Color" />
    public Vector4D<float> Color
    {
        get;
        set
        {
            field = value;
            ColorChanged?.Invoke(value);
        }
    } = Vector4D<float>.One;

    /// <inheritdoc cref="GLManager.Normal" />
    public Vector3D<float> Normal
    {
        get;
        set
        {
            field = value;
            NormalChanged?.Invoke(value);
        }
    }

    // ── Capabilities ────────────────────────────────────────────────────────

    public bool TextureEnabled { get; set; }
    public bool LightingEnabled { get; set; }

    public bool AlphaTestEnabled
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            RasterStateChanging?.Invoke();
        }
    }

    public bool FogEnabled
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            RasterStateChanging?.Invoke();
        }
    }

    public ShadeModel ShadeModel { get; set; } = ShadeModel.Smooth;
    public float AlphaThreshold { get; set; } = 0.1f;

    // ── Per-pass state ─────────────────────────────────────────────────────

    public FogState Fog { get; set; } = FogState.Default;
    public LightingState Lighting { get; set; } = LightingState.Default;
    public WorldLightState WorldLight { get; set; } = WorldLightState.Default;

    /// <summary>
    ///     Which interface texture the next GUI draw samples, by the numbering in
    ///     <c>shaders/ui_textures.properties</c>. Zero for one that is not named there.
    /// </summary>
    /// <remarks>
    ///     Ambient rather than part of the draw because it is a pack's hook, not the client's: the
    ///     interface arrives at the backend as untyped quads, and this is the only thing that tells
    ///     a pack it is shading an inventory rather than a button.
    /// </remarks>
    public int GuiTextureId { get; set; }

    /// <summary>
    ///     The rectangle draws are clipped to, or null for the whole target.
    /// </summary>
    /// <remarks>
    ///     Not a <see cref="RenderState" /> field, even though it reads like one: in a backend with
    ///     pipelines it is not part of one — WebGPU scissors with a command on the pass encoder,
    ///     always on and defaulting to the whole attachment — and folding it in would multiply every
    ///     state by every rectangle a screen happens to clip to. So it is ambient, read by the target
    ///     at submission the way the matrices and the tint are.
    /// </remarks>
    public ScissorRect? Scissor { get; set; }

    // ── Backend notifications ───────────────────────────────────────────────

    /// <summary>
    ///     Raised when <see cref="Color" /> changes, for a backend that has to push the value
    ///     somewhere rather than read it at the draw.
    /// </summary>
    public event Action<Vector4D<float>>? ColorChanged;

    /// <summary>Raised when <see cref="Normal" /> changes.</summary>
    /// <inheritdoc cref="ColorChanged" />
    public event Action<Vector3D<float>>? NormalChanged;

    /// <summary>
    ///     Raised just before a change to state that governs how geometry rasterizes, so a renderer
    ///     holding queued geometry can flush it under the state it was queued with.
    /// </summary>
    public event Action? RasterStateChanging;
}

/// <summary>
///     A clip rectangle in target pixels, measured from the bottom-left corner.
/// </summary>
/// <remarks>
///     Bottom-left because that is where the callers compute it and where OpenGL wants it. WebGPU
///     measures from the top-left, so its target flips this against the attachment height.
/// </remarks>
public readonly record struct ScissorRect(int X, int Y, int Width, int Height);
