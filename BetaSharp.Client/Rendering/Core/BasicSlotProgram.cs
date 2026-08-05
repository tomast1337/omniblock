using BetaSharp.Client.Options;

namespace BetaSharp.Client.Rendering.Core;

/// <summary><see cref="ProgramSlot.Basic" />: untextured geometry, tinted, shaded, under fog.</summary>
/// <remarks>
///     Everything the fixed-function shader did short of sampling a texture. It reads the lights
///     because untextured geometry can be lit — the debug shapes are drawn inside the entity pass,
///     with normals and nothing turning lighting off — and a slot that could not shade them would
///     leave them with nothing to name.
/// </remarks>
internal sealed class BasicSlotProgram : ISlotProgram, IDisposable
{
    private readonly Shader _shader;

    public BasicSlotProgram(GameOptions options) =>
        _shader = new Shader(
            options.ShaderOptions.GetOrCreate("gbuffers_basic"),
            "shaders/gbuffers_basic.vert",
            "shaders/gbuffers_basic.frag");

    public VertexLayoutKind VertexLayout => VertexLayoutKind.Generic;

    public void Activate()
    {
        _shader.Bind();
        SlotUniforms.UploadTransforms(_shader);
        SlotUniforms.UploadFogAndAlpha(_shader);
        SlotUniforms.UploadLighting(_shader);
    }

    /// <summary>
    ///     Unbinds rather than restoring, so a draw that names no slot draws with no program and
    ///     shows it, rather than silently inheriting whatever the last one left bound.
    /// </summary>
    public void Deactivate() => GLManager.GL.UseProgram(0);

    public void Dispose() => _shader.Dispose();
}
