using BetaSharp.Client.Options;

namespace BetaSharp.Client.Rendering.Core;

/// <summary><see cref="ProgramSlot.Gui" />: interface quads, textured and tinted per vertex.</summary>
/// <remarks>
///     <para>
///         What separates it from <see cref="ProgramSlot.Textured" />, which it falls back to, is
///         the texture id — the interface is one long run of quads out of a single batch, and a pack
///         has nothing else to tell an inventory from a button by. Everything else about the two is
///         the same, minus the fog and world light no interface draw is under.
///     </para>
///     <para>
///         Untextured interface geometry does not come here: it is submitted under
///         <see cref="ProgramSlot.Basic" />, since a program that samples unit 0 has nothing to
///         sample for it.
///     </para>
/// </remarks>
internal sealed class GuiSlotProgram : ISlotProgram, IDisposable
{
    private readonly Shader _shader;

    public GuiSlotProgram(GameOptions options) =>
        _shader = new Shader(
            options.ShaderOptions.GetOrCreate("gui"),
            "shaders/ui.vert",
            "shaders/ui.frag");

    public VertexLayoutKind VertexLayout => VertexLayoutKind.Generic;

    public void Activate()
    {
        _shader.Bind();
        SlotUniforms.UploadTransforms(_shader);
        _shader.SetUniform1("u_Texture", 0);
        _shader.SetUniform1("u_TextureId", GLManager.GuiTextureId);
    }

    /// <inheritdoc cref="BasicSlotProgram.Deactivate" />
    public void Deactivate() => GLManager.GL.UseProgram(0);

    public void Dispose() => _shader.Dispose();
}
