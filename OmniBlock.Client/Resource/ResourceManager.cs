namespace OmniBlock.Client.Resource;

public class ResourceManager : IDisposable
{
    private readonly List<IResourceLoader> _loaders = [];

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var loader in _loaders)
        {
            if (loader is IDisposable disposable)
                disposable.Dispose();
        }
    }

    public ResourceManager Add(IResourceLoader loader)
    {
        _loaders.Add(loader);
        return this;
    }

    public async Task LoadAllAsync()
    {
        foreach (var loader in _loaders)
        {
            await loader.LoadAsync();
        }
    }
}
