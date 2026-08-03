namespace BetaSharp.Client.Rendering.Core;

/// <summary>
///     How a draw's output is combined with what is already in the colour buffer.
/// </summary>
/// <remarks>
///     A closed set rather than the source and destination factors, because only these six
///     combinations are used and naming them says what each one is for. It is also the form a
///     backend without global blend state needs: these become entries in a pipeline descriptor
///     chosen at draw time, not a function called before one.
/// </remarks>
public enum BlendMode
{
    /// <summary>Writes straight over the destination.</summary>
    None,

    /// <summary>Ordinary transparency. <c>SrcAlpha, OneMinusSrcAlpha</c>.</summary>
    Alpha,

    /// <summary>Light that only ever brightens, for the sky's sun and moon. <c>One, One</c>.</summary>
    Additive,

    /// <summary>Additive, faded in by the source's alpha. <c>SrcAlpha, One</c>.</summary>
    AdditiveByAlpha,

    /// <summary>Weighted by what is already there rather than by the source. <c>SrcAlpha, DstAlpha</c>.</summary>
    SourceToDestinationAlpha,

    /// <summary>Darkens by multiplying, which is how the world is tinted. <c>DstColor, SrcColor</c>.</summary>
    Multiply,

    /// <summary>
    ///     Inverts what is behind it, so the crosshair stays visible against any background.
    ///     <c>OneMinusDstColor, OneMinusSrcColor</c>.
    /// </summary>
    Invert,

    /// <summary>
    ///     Scales the destination down without adding anything, which is how the vignette darkens
    ///     the edges of the screen. <c>Zero, OneMinusSrcColor</c>.
    /// </summary>
    Darken
}

/// <summary>Which faces are discarded before rasterisation.</summary>
public enum CullMode
{
    None,
    Back
}

/// <summary>The comparison a fragment's depth must pass to be kept.</summary>
public enum DepthCompare
{
    LessOrEqual,
    Equal
}

/// <summary>
///     The fixed part of how a draw is rasterised: blending, depth, culling and write masks.
/// </summary>
/// <remarks>
///     <para>
///         Grouped into one value because that is what it becomes. WebGPU has no
///         <c>Enable</c>/<c>Disable</c>, no <c>BlendFunc</c>, no <c>DepthMask</c>; all of it is
///         baked into an immutable pipeline object built up front and selected per draw. A
///         <see cref="RenderState" /> maps onto such a descriptor directly, where a scattered
///         sequence of state calls does not.
///     </para>
///     <para>
///         Deliberately excludes the capabilities that look like pipeline state and are not.
///         Texturing, lighting, fog and the alpha test are already shader uniforms underneath, so
///         they stay uniforms in any backend and do not belong in a pipeline descriptor.
///     </para>
/// </remarks>
public readonly record struct RenderState
{
    public BlendMode Blend { get; init; }
    public bool DepthTest { get; init; }
    public bool DepthWrite { get; init; }
    public DepthCompare DepthCompare { get; init; }
    public CullMode Cull { get; init; }
    public bool ColorWrite { get; init; }

    /// <summary>Solid geometry: depth tested and written, backs discarded, no blending.</summary>
    public static readonly RenderState Opaque = new()
    {
        Blend = BlendMode.None,
        DepthTest = true,
        DepthWrite = true,
        DepthCompare = DepthCompare.LessOrEqual,
        Cull = CullMode.Back,
        ColorWrite = true
    };

    /// <summary>
    ///     See-through geometry. Still depth tested, but it does not write depth, so two translucent
    ///     surfaces do not hide each other according to which was drawn first.
    /// </summary>
    public static readonly RenderState Translucent = Opaque with
    {
        Blend = BlendMode.Alpha,
        DepthWrite = false
    };

    /// <summary>Glows, drawn over the scene without occluding it.</summary>
    public static readonly RenderState Additive = Translucent with { Blend = BlendMode.Additive };

    /// <summary>
    ///     Entity models, drawn without culling.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Their boxes are not reliably wound outwards, which is why
    ///         <c>LivingEntityRenderer.DoRenderLiving</c> turns culling off before drawing one.
    ///     </para>
    ///     <para>
    ///         It never turns it back on. Nothing else in the entity tree does either, so culling
    ///         stays off until the next frame resets it, and whether it was on when some other
    ///         entity drew depended on whether a mob happened to draw first. Naming the state makes
    ///         that answer the same every frame. It settles on off, which is both what the models
    ///         want and the direction that cannot lose geometry: culling wrongly left on hides
    ///         faces, wrongly left off only draws ones that were already there.
    ///     </para>
    /// </remarks>
    public static readonly RenderState Entity = Opaque with { Cull = CullMode.None };

    /// <summary>Flat geometry with no meaningful facing, such as interface and text quads.</summary>
    public static readonly RenderState Interface = new()
    {
        Blend = BlendMode.Alpha,
        DepthTest = false,
        DepthWrite = false,
        DepthCompare = DepthCompare.LessOrEqual,
        Cull = CullMode.None,
        ColorWrite = true
    };
}
