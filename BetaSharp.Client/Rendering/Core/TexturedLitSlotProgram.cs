using BetaSharp.Client.Options;

namespace BetaSharp.Client.Rendering.Core;

/// <summary><see cref="ProgramSlot.TexturedLit" />: textured, tinted, shaded, under fog.</summary>
/// <remarks>
///     <para>
///         Everything <see cref="TexturedSlotProgram" /> draws plus the two directional lights, which
///         makes it the first slot with nothing missing against the fallback shader. A draw routed
///         here cannot come out shaded differently from how it drew before, whatever state it is
///         under — which is why the slots below it in the chain report when they are used wrongly and
///         this one has nothing to report.
///     </para>
///     <para>
///         Lighting stays a uniform rather than becoming part of what the slot means. Particles and
///         weather switch it off for a pass and back on without changing what they are, and a slot
///         that meant "lit unconditionally" would strand them between two.
///     </para>
/// </remarks>
internal sealed class TexturedLitSlotProgram : ISlotProgram, IDisposable
{
    private readonly Shader _shader;

    public TexturedLitSlotProgram(GameOptions options) =>
        _shader = new Shader(
            options.ShaderOptions.GetOrCreate("gbuffers_textured_lit"),
            "shaders/gbuffers_textured_lit.vert",
            "shaders/gbuffers_textured_lit.frag");

    public void Activate()
    {
        _shader.Bind();
        SlotUniforms.UploadTransforms(_shader);
        SlotUniforms.UploadFogAndAlpha(_shader);
        SlotUniforms.UploadLighting(_shader);
        _shader.SetUniformMatrix4("textureMatrix", GLManager.TextureMatrix.Top);
        _shader.SetUniform1("textureSampler", 0);
    }

    /// <inheritdoc cref="BasicSlotProgram.Deactivate" />
    public void Deactivate() => GLManager.GL.UseProgram(0);

    public void Dispose() => _shader.Dispose();
}
