using BetaSharp.Client.Options;

namespace BetaSharp.Client.Rendering.Core;

/// <summary><see cref="ProgramSlot.Textured" />: textured and tinted, shaded, under fog.</summary>
/// <remarks>
///     <para>
///         Adds the texture and its matrix to what <see cref="BasicSlotProgram" /> draws. What
///         separates it from <see cref="ProgramSlot.TexturedLit" /> is which draws carry which name,
///         not what either program can do — the two defaults are the same shader, and exist apart so
///         a pack can replace one without replacing both.
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

    public VertexLayoutKind VertexLayout => VertexLayoutKind.Generic;

    public void Activate()
    {
        _shader.Bind();
        SlotUniforms.UploadTransforms(_shader);
        SlotUniforms.UploadFogAndAlpha(_shader);
        SlotUniforms.UploadLighting(_shader);
        SlotUniforms.UploadWorldLight(_shader);
        _shader.SetUniformMatrix4("textureMatrix", GLManager.TextureMatrix.Top);
        _shader.SetUniform1("textureSampler", 0);
        _shader.SetUniform1("arraySampler", TextureArrayUnits.Terrain);
    }

    /// <inheritdoc cref="BasicSlotProgram.Deactivate" />
    public void Deactivate() => GLManager.GL.UseProgram(0);

    public void Dispose() => _shader.Dispose();
}
