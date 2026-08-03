using BetaSharp.Client.Options;

namespace BetaSharp.Client.Rendering.Core;

/// <summary><see cref="ProgramSlot.Textured" />: textured and tinted, unlit, under fog.</summary>
/// <remarks>
///     <para>
///         Adds the texture and its matrix to what <see cref="BasicSlotProgram" /> draws, and
///         nothing else. Still no lights and no normal matrix, which is the line between this and
///         <see cref="ProgramSlot.TexturedLit" />: a draw that belongs here has to have turned
///         lighting off, and one that has not is reported rather than quietly shaded differently.
///     </para>
///     <para>
///         The texture unit is fixed at 0 and the caller binds to it. Which texture is not this
///         program's business — the call sites bind through <c>TextureManager</c> and always did.
///     </para>
/// </remarks>
internal sealed class TexturedSlotProgram : ISlotProgram, IDisposable
{
    private readonly Shader _shader;

    public TexturedSlotProgram(GameOptions options) =>
        _shader = new Shader(
            options.ShaderOptions.GetOrCreate("gbuffers_textured"),
            "shaders/gbuffers_textured.vert",
            "shaders/gbuffers_textured.frag");

    public void Activate()
    {
        SlotUniforms.ReportIfLit(nameof(ProgramSlot.Textured));

        _shader.Bind();
        SlotUniforms.UploadTransforms(_shader);
        SlotUniforms.UploadFogAndAlpha(_shader);
        _shader.SetUniformMatrix4("textureMatrix", GLManager.TextureMatrix.Top);
        _shader.SetUniform1("textureSampler", 0);
    }

    /// <inheritdoc cref="BasicSlotProgram.Deactivate" />
    public void Deactivate() => GLManager.GL.UseProgram(0);

    public void Dispose() => _shader.Dispose();
}
