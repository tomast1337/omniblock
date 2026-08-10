namespace OmniBlock.Client.Rendering.Core;

/// <summary>Which of the fixed vertex formats a draw's buffer is laid out as.</summary>
/// <remarks>
///     Not a general-purpose descriptor — there are exactly two vertex formats a
///     <see cref="DrawPipeline" /> is ever built for today: the Tessellator/<see cref="StaticMesh" />
///     generic format, and terrain's packed <see cref="ChunkVertex" />. A third layout gets a third
///     member, not a redesign.
/// </remarks>
public enum VertexLayoutKind
{
    Generic,
    Chunk
}

/// <summary>
///     What a draw carries: which program it names, what raster state it draws under, and which
///     vertex format its buffer is laid out as.
/// </summary>
/// <remarks>
///     A GL draw today gets these three from three different places — <see cref="ProgramSlot" />
///     resolves ambient global state at <see cref="ISlotProgram.Activate" /> time, <see cref="RenderState" />
///     is applied once for a whole render pass rather than per draw, and vertex layout is implicit in
///     which method a call site happens to call. Bundling them under one name is what a WebGPU
///     <c>GPURenderPipelineDescriptor</c> already requires explicitly, so this is the seam a future
///     port swaps in against, not a redesign of how any of the three individually work today.
/// </remarks>
public readonly record struct DrawPipeline(ProgramSlot Program, RenderState State, VertexLayoutKind VertexLayout);
