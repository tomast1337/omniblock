using BetaSharp.Client.Options;

namespace BetaSharp.Client.Rendering.Core;

/// <summary><see cref="ProgramSlot.Basic" />: untextured, unlit geometry under fog.</summary>
/// <remarks>
///     Ten uniforms against the fixed-function shader's twenty. What is gone is everything an
///     untextured draw was never going to read — the texture and its matrix, the two lights, the
///     normal matrix — and the value is not the uniforms saved but that a caller can no longer leave
///     lighting on and have it reach geometry that has no normals.
/// </remarks>
internal sealed class BasicSlotProgram : ISlotProgram, IDisposable
{
    private readonly Shader _shader;

    public BasicSlotProgram(GameOptions options) =>
        _shader = new Shader(
            options.ShaderOptions.GetOrCreate("gbuffers_basic"),
            "shaders/gbuffers_basic.vert",
            "shaders/gbuffers_basic.frag");

    public void Activate()
    {
        SlotUniforms.ReportIfLit(nameof(ProgramSlot.Basic));

        _shader.Bind();
        SlotUniforms.UploadTransforms(_shader);
        SlotUniforms.UploadFogAndAlpha(_shader);
    }

    /// <summary>
    ///     Unbinds rather than restoring, because there is nothing to restore to: the fixed-function
    ///     pipeline activates its own shader when it finds no program bound, and leaving this one
    ///     bound would make it skip that.
    /// </summary>
    public void Deactivate() => GLManager.GL.UseProgram(0);

    public void Dispose() => _shader.Dispose();
}
