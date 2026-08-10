namespace OmniBlock.Client.Rendering.Core;

/// <summary>The primitive a run of vertices is assembled into.</summary>
/// <remarks>
///     Named rather than carried as a GL enum because the value has to mean the same thing to a
///     backend that has never heard of GL. Quads are absent deliberately: the Tessellator expands
///     them into triangles as vertices are added, so no submission is ever made of them.
/// </remarks>
public enum DrawTopology
{
    Points,
    Lines,
    LineStrip,
    Triangles,
    TriangleStrip,

    /// <summary>
    ///     Has no WebGPU counterpart. One draw in the client uses it; a backend that cannot assemble
    ///     it has to say so rather than draw something else.
    /// </summary>
    TriangleFan,
}

/// <summary>Which optional attributes the vertices in a submission actually carry.</summary>
/// <remarks>
///     Position, array layer and light are always present, so they are not listed. The three here
///     are the ones a draw may leave unwritten, in which case the vertex still has the field but it
///     holds nothing, and the backend must not point an attribute at it.
/// </remarks>
[Flags]
public enum VertexChannels
{
    None = 0,
    Texture = 1,
    Color = 2,
    Normal = 4,
}

/// <summary>The interleaved vertex layout <see cref="Tessellator" /> writes.</summary>
/// <remarks>
///     Shared rather than spelled out at each draw site, because a reader that binds different
///     offsets than the writer produced does not fail — it draws garbage. Anything that submits
///     Tessellator output goes through here.
/// </remarks>
public static class TessellatorVertexLayout
{
    public const int Stride = 36;
}

/// <summary>
///     A batch of geometry and everything a backend needs to draw it.
/// </summary>
/// <remarks>
///     <para>
///         This is the seam between the renderers and the graphics API — it says what is being
///         drawn rather than how to draw it, so a backend implements it by interpreting the command
///         instead of replaying a recorded list of calls.
///     </para>
///     <para>
///         What it deliberately does not carry is the ambient state — matrices, tint, fog, lighting,
///         blend and depth. Those live in <see cref="RenderContext" /> and
///         <see cref="RenderStateApplier.Current" />, and a target reads them at submission. That is
///         the same snapshot for every caller here, because every draw through this seam is
///         immediate: the vertices are handed over and drawn before anything else changes the state.
///     </para>
/// </remarks>
public readonly ref struct DrawCommand
{
    /// <summary>The interleaved vertex data, in <see cref="TessellatorVertexLayout" />.</summary>
    public required ReadOnlySpan<byte> Vertices { get; init; }

    public required int VertexCount { get; init; }

    public required DrawTopology Topology { get; init; }

    public required VertexChannels Channels { get; init; }

    /// <summary>The slot to resolve a program from, or null when the caller has bound its own.</summary>
    public required ProgramSlot? Slot { get; init; }
}

/// <summary>Whatever is currently able to turn a <see cref="DrawCommand" /> into pixels.</summary>
/// <remarks>
///     One implementation per backend. The OpenGL one exists for the whole run; the WebGPU one is
///     scoped to a render pass, because in WebGPU there is nowhere to submit a draw outside one.
/// </remarks>
public interface IDrawTarget
{
    /// <summary>Draws the command now, under the state currently in effect.</summary>
    void Submit(in DrawCommand command);

    /// <summary>
    ///     Uploads the command's vertices somewhere that outlives the frame, for geometry built once
    ///     and drawn forever.
    /// </summary>
    IStaticMesh Capture(in DrawCommand command);
}

/// <summary>
///     Geometry built once and drawn many times, held in a buffer of its own.
/// </summary>
/// <remarks>
///     <para>
///         Replaces what a display list was used for. The two are equivalent for this purpose: a
///         list recorded the vertex submission and replayed it, and the transform was applied by
///         whoever called it rather than being captured, so holding the vertices in a buffer and
///         re-submitting them draws the same thing under the same matrices.
///     </para>
///     <para>
///         They are not equivalent in general — a list could record arbitrary state changes — but
///         nothing here recorded any. Display lists have no counterpart in GL core profiles or in
///         WebGPU, so this had to stop being the mechanism either way.
///     </para>
/// </remarks>
public interface IStaticMesh : IDisposable
{
    /// <inheritdoc cref="Tessellator.draw(ProgramSlot)" />
    void Draw(ProgramSlot slot);

    /// <inheritdoc cref="Tessellator.drawWithBoundProgram" />
    void DrawWithBoundProgram();
}
