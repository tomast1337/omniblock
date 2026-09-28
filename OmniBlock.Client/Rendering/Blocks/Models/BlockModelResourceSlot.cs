namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>
/// Render-thread owned publication point. A caller supplies fully prepared resources; one reference
/// swaps models and textures together. Texture disposal already defers native release until submit.
/// Pack selection/options are deliberately outside this slot's responsibility.
/// </summary>
internal sealed class BlockModelResourceSlot : IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private bool _disposed;
    public UploadedBlockModelResources? Current { get; private set; }
    public long Generation { get; private set; }

    public void Replace(Func<UploadedBlockModelResources> prepare)
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(prepare);
        var candidate = prepare() ?? throw new InvalidOperationException("Preparation returned no resource candidate.");
        ValidateCandidate(candidate);
        var previous = Current;
        Current = candidate;
        Generation++;
        previous?.Dispose();
    }

    internal void ValidateCandidate(UploadedBlockModelResources candidate)
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(candidate, Current)) throw new InvalidOperationException("Replacement must be independently owned.");
        if (candidate.Texture.Id == 0) throw new ObjectDisposedException(nameof(candidate));
    }

    public void Dispose()
    {
        CheckThread();
        if (_disposed) return;
        _disposed = true;
        Current?.Dispose();
        Current = null;
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Model resource publication requires its owning render thread.");
    }
}
