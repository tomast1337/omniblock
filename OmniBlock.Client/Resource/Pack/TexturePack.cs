using OmniBlock.Client.Rendering.Core.Textures;

namespace OmniBlock.Client.Resource.Pack;

public abstract class TexturePack
{
    public string? FirstDescriptionLine;
    public string? SecondDescriptionLine;
    public string? Signature;
    public string? TexturePackFileName;

    public virtual void func_6482_a()
    {
    }

    public virtual void CloseTexturePackFile()
    {
    }

    public virtual void func_6485_a(OmniBlock game)
    {
    }

    public virtual void Unload(TextureManager textureManager)
    {
    }

    public virtual TextureHandle GetThumbnailTexture(TextureManager textureManager) => textureManager.GetTextureId("/gui/unknown_pack.png");

    public virtual Stream? GetResourceAsStream(string path)
    {
        try
        {
            var asset = AssetManager.Instance.GetAsset(path);
            if (asset == null) return null;
            return new MemoryStream(asset.GetBinaryContent());
        }
        catch (Exception)
        {
            return null;
        }
    }
}
