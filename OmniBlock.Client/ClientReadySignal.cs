namespace OmniBlock.Client;

/// <summary>
///     One-shot lifecycle signal reached after the initial main menu has been initialized.
/// </summary>
internal sealed class ClientReadySignal
{
    private Action? _reached;

    public bool IsReady { get; private set; }

    /// <summary>
    ///     Registers work that requires a fully initialized client. Late registrations run
    ///     immediately, which keeps callers from missing the one-shot transition.
    /// </summary>
    public void WhenReady(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (IsReady)
        {
            callback();
            return;
        }

        _reached += callback;
    }

    public void Remove(Action callback) => _reached -= callback;

    public void Signal()
    {
        if (IsReady)
        {
            return;
        }

        IsReady = true;
        var reached = _reached;
        _reached = null;
        reached?.Invoke();
    }
}
