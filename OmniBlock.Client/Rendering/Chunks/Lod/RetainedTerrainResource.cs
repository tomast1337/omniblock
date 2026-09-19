namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>
/// Render-thread resource whose catalog owner and displayed snapshot can have different lifetimes.
/// Replacing a catalog entry releases ownership but must not free a mesh still being displayed.
/// </summary>
internal abstract class RetainedTerrainResource : IDisposable
{
    private int _references = 1;
    private bool _ownerReleased;

    public IDisposable Retain()
    {
        ObjectDisposedException.ThrowIf(_references == 0, this);
        _references++;
        return new Lease(this);
    }

    public void Dispose()
    {
        if (_ownerReleased) return;
        _ownerReleased = true;
        Release();
    }

    private void Release()
    {
        if (--_references == 0) DisposeResources();
    }

    protected abstract void DisposeResources();

    private sealed class Lease(RetainedTerrainResource resource) : IDisposable
    {
        private RetainedTerrainResource? _resource = resource;

        public void Dispose()
        {
            var resource = _resource;
            _resource = null;
            resource?.Release();
        }
    }
}
