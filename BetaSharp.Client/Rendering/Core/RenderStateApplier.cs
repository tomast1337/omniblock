namespace OmniBlock.Client.Rendering.Core;

/// <summary>
///     Puts a <see cref="RenderState" /> into effect, issuing only the calls that change something.
/// </summary>
/// <remarks>
///     <para>
///         The cache is the only record of what a submission's pipeline was chosen under, since
///         WebGPU bakes blend, depth, cull and write mask into the pipeline rather than holding them
///         as ambient state. A deferred renderer reads <see cref="Current" /> to record the state a
///         draw was submitted under and reproduce it later.
///     </para>
///     <para>
///         Foreign code that changes this state out of the applier's reach — the ImGui backend is
///         the one case in this codebase — has to say so by calling <see cref="Invalidate" />.
///     </para>
/// </remarks>
public sealed class RenderStateApplier
{
    private RenderState _current;
    private bool _known;

    /// <summary>
    ///     Forgets what is believed to be set, so the next <see cref="Apply" /> writes every field.
    /// </summary>
    public void Invalidate() => _known = false;

    /// <summary>
    ///     The state currently in effect.
    /// </summary>
    /// <remarks>
    ///     Only answerable because the cache is the truth, which is a recent thing. It is what lets a
    ///     deferred renderer record the state a draw was submitted under and reproduce it later,
    ///     rather than drawing under whatever happens to be set when its batch is finally flushed.
    ///     Throws rather than guessing after <see cref="Invalidate" />: a caller that needs this
    ///     needs it to be right, and there is no honest answer before the first <see cref="Apply" />.
    /// </remarks>
    public RenderState Current => _known
        ? _current
        : throw new InvalidOperationException(
            "The render state is not known. Something set it outside the applier and invalidated the cache; apply a state before asking what is set.");

    public void Apply(in RenderState state)
    {
        if (_known && _current == state)
        {
            return;
        }

        // WebGPU bakes all seven of these into the pipeline a draw is submitted with, so there is
        // nothing to issue here — but Current still has to be right, since that is what the
        // pipeline for a submission is chosen from.
        _current = state;
        _known = true;
    }
}
