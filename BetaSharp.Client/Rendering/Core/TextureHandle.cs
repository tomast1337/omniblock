namespace BetaSharp.Client.Rendering.Core;

public class TextureHandle
{
    private readonly TextureManager _manager;
    public int Id { get; internal set; }

    internal TextureHandle(TextureManager manager, int id)
    {
        _manager = manager;
        Id = id;
    }

    public void Bind()
    {
        _manager.BindTexture(Id);
    }

    public override string ToString()
    {
        return $"TextureHandle(Id={Id})";
    }
}
