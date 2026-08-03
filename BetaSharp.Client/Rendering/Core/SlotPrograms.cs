using BetaSharp.Client.Options;

namespace BetaSharp.Client.Rendering.Core;

/// <summary>Which program each <see cref="ProgramSlot" /> currently resolves to.</summary>
/// <remarks>
///     <para>
///         A slot with no program of its own resolves to the nearest ancestor that has one, walking
///         <see cref="ProgramSlots.ResolutionChain" />. A slot whose whole chain is empty resolves to
///         <see cref="FixedFunction" />, which draws exactly as everything drew before slots existed.
///         So routing a draw to a slot is safe before that slot's shader is written: it changes
///         nothing until a program shows up to claim it.
///     </para>
///     <para>
///         Static because the fallback shader it is displacing is, and because a draw reaches this
///         through <see cref="Tessellator" />, which is a singleton for the same reason. It stops
///         being static when the last draw stops going through the Tessellator.
///     </para>
/// </remarks>
public static class SlotPrograms
{
    /// <summary>
    ///     Stands in for a slot nothing has claimed. Binds nothing, so the draw falls through to the
    ///     fixed-function shader that <c>FixedFunctionPipeline.DrawArrays</c> activates on its own.
    /// </summary>
    public static ISlotProgram FixedFunction { get; } = new FixedFunctionSlotProgram();

    private static readonly Dictionary<ProgramSlot, ISlotProgram> s_programs = [];

    public static void Register(ProgramSlot slot, ISlotProgram program) => s_programs[slot] = program;

    /// <summary>The program a draw asking for <paramref name="slot" /> should use.</summary>
    public static ISlotProgram Resolve(ProgramSlot slot)
    {
        foreach (ProgramSlot candidate in ProgramSlots.ResolutionChain(slot))
        {
            if (s_programs.TryGetValue(candidate, out ISlotProgram? program))
            {
                return program;
            }
        }

        return FixedFunction;
    }

    /// <summary>Builds the programs that exist so far and claims their slots.</summary>
    /// <remarks>
    ///     Runs once the GL context is up. Anything drawn before this — the loading screen's first
    ///     frames — resolves to <see cref="FixedFunction" /> and looks the same, which is the point
    ///     of having a fallback at all rather than an initialisation order to get right.
    /// </remarks>
    public static void Initialize(GameOptions options)
    {
        Register(ProgramSlot.Basic, new BasicSlotProgram(options));
        Register(ProgramSlot.Textured, new TexturedSlotProgram(options));
    }

    public static void Dispose()
    {
        foreach (IDisposable program in s_programs.Values.OfType<IDisposable>())
        {
            program.Dispose();
        }

        s_programs.Clear();
    }

    private sealed class FixedFunctionSlotProgram : ISlotProgram
    {
        public void Activate()
        {
        }

        public void Deactivate()
        {
        }
    }
}
