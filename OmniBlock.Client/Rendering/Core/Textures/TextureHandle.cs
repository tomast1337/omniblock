namespace OmniBlock.Client.Rendering.Core.Textures;

public class TextureHandle
{
    internal TextureHandle(Texture2D? texture) => Texture = texture;
    public Texture2D? Texture { get; internal set; }
    public int Id => (int)(Texture?.Id ?? 0u);

    public void Bind() => Texture?.Bind();

    public override string ToString() => $"TextureHandle(Id={Id}, Source={Texture?.Source ?? "null"})";
}
