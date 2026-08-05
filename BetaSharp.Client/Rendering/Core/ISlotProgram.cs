namespace BetaSharp.Client.Rendering.Core;

/// <summary>The program a <see cref="ProgramSlot" /> resolves to, and the state it needs uploaded.</summary>
/// <remarks>
///     Deliberately not just a program id. What separates a slot's program from the fallback shader
///     is that it knows which of the ambient state it actually reads, and takes only that — the
///     point of the split is that a draw cannot silently inherit something nobody meant it to.
/// </remarks>
public interface ISlotProgram
{
    /// <summary>Which vertex format this program's attribute declarations expect.</summary>
    /// <remarks>
    ///     A program is written against one layout and reads garbage from any other, so resolution
    ///     has to skip a program whose layout does not match rather than take it for being the most
    ///     specific ancestor with one. The terrain program is the case: <c>gbuffers_block</c> and
    ///     <c>gbuffers_damagedblock</c> both name terrain as their parent, and both are drawn from
    ///     the Tessellator, which does not speak <see cref="ChunkVertex" />.
    /// </remarks>
    VertexLayoutKind VertexLayout { get; }

    /// <summary>Binds the program and uploads what it draws under, immediately before a draw.</summary>
    /// <remarks>
    ///     Called per draw rather than per pass. These are small draws and the state they read —
    ///     the matrix stacks, fog — is whatever the caller had in force at that moment, which is the
    ///     last point it is still the geometry's contemporary.
    /// </remarks>
    void Activate();

    /// <summary>Puts back whatever the surrounding immediate-mode code believes is bound.</summary>
    void Deactivate();
}
