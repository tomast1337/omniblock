namespace BetaSharp.Client.Rendering.Core.Textures;

public class TextureHandle
{
    public Texture2D? Texture { get; internal set; }
    public int Id => (int)(Texture?.Id ?? 0u);

    internal TextureHandle(Texture2D? texture)
    {
        Texture = texture;
    }

    public void Bind()
    {
        Texture?.Bind();
    }

    public override string ToString()
    {
        return $"TextureHandle(Id={Id}, Source={Texture?.Source ?? "null"})";
    }
}
