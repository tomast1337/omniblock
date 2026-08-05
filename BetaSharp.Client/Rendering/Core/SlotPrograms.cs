using BetaSharp.Client.Options;

namespace BetaSharp.Client.Rendering.Core;

/// <summary>Which program each <see cref="ProgramSlot" /> currently resolves to.</summary>
/// <remarks>
///     <para>
///         A slot with no program of its own resolves to the nearest ancestor that has one, walking
///         <see cref="ProgramSlots.ResolutionChain" />. Every chain ends at
///         <see cref="ProgramSlot.Basic" /> and that one is always claimed, so naming a slot is safe
///         before its own shader exists — it draws under an ancestor's until one shows up.
///     </para>
///     <para>
///         Static because a draw reaches this through <see cref="Tessellator" />, which is a
///         singleton. It stops being static when the last draw stops going through the Tessellator.
///     </para>
/// </remarks>
public static class SlotPrograms
{
    /// <summary>
    ///     Binds nothing and uploads nothing, for a draw whose caller has already bound a program of
    ///     its own and set its uniforms — the sky. Not a fallback: with no program bound it draws
    ///     with none.
    /// </summary>
    public static ISlotProgram CallerBound { get; } = new CallerBoundProgram();

    private static readonly Dictionary<ProgramSlot, ISlotProgram> s_programs = [];

    public static void Register(ProgramSlot slot, ISlotProgram program) => s_programs[slot] = program;

    /// <summary>
    ///     The program a draw asking for <paramref name="slot" /> should use, given the vertex format
    ///     its buffer is in.
    /// </summary>
    /// <remarks>
    ///     The layout is part of resolution, not a check afterwards. A slot's chain can pass through
    ///     a program written for a different vertex format — every block-shaped slot names terrain as
    ///     its parent, and terrain's program reads <see cref="ChunkVertex" /> — and taking it would
    ///     draw whatever the wrong attribute offsets happen to spell. Skipping it lands on the
    ///     nearest ancestor that can actually read the buffer.
    /// </remarks>
    public static ISlotProgram Resolve(ProgramSlot slot, VertexLayoutKind vertexLayout)
    {
        foreach (ProgramSlot candidate in ProgramSlots.ResolutionChain(slot))
        {
            if (s_programs.TryGetValue(candidate, out ISlotProgram? program) && program.VertexLayout == vertexLayout)
            {
                return program;
            }
        }

        throw new InvalidOperationException(
            $"No program for slot {slot} or any of its ancestors reads {vertexLayout} vertices. " +
            "Basic claims the root of every chain, so this means a draw ran before " +
            "SlotPrograms.Initialize, or a layout nothing has a program for.");
    }

    /// <summary>Builds the programs that exist so far and claims their slots.</summary>
    /// <remarks>
    ///     Runs as soon as the GL context is up, and must: a draw before this has no program to
    ///     resolve to and says so rather than drawing wrongly.
    /// </remarks>
    public static void Initialize(GameOptions options)
    {
        Register(ProgramSlot.Basic, new BasicSlotProgram(options));
        Register(ProgramSlot.Textured, new TexturedSlotProgram(options));
        Register(ProgramSlot.TexturedLit, new TexturedLitSlotProgram(options));
        Register(ProgramSlot.Terrain, new TerrainSlotProgram(options));
    }

    public static void Dispose()
    {
        foreach (IDisposable program in s_programs.Values.OfType<IDisposable>())
        {
            program.Dispose();
        }

        s_programs.Clear();
    }

    private sealed class CallerBoundProgram : ISlotProgram
    {
        /// <summary>
        ///     Whatever the caller bound. Never reached through <see cref="Resolve" /> — it is handed
        ///     out by name — so its layout is only here to satisfy the interface.
        /// </summary>
        public VertexLayoutKind VertexLayout => VertexLayoutKind.Generic;

        public void Activate()
        {
        }

        public void Deactivate()
        {
        }
    }
}
