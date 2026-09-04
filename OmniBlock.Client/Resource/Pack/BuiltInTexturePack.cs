using Microsoft.Extensions.Logging;
using OmniBlock.Client.Rendering.Core.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OmniBlock.Client.Resource.Pack;

public class BuiltInTexturePack : TexturePack
{
    private readonly ILogger _logger = Log.Instance.For<BuiltInTexturePack>();
    private readonly Image<Rgba32>? texturePackThumbnail;
    private TextureHandle? _texturePackName;

    public BuiltInTexturePack()
    {
        TexturePackFileName = "Default";
        FirstDescriptionLine = "The default look of Minecraft";

        try
        {
            var content = AssetManager.Instance.GetAsset("pack.png").GetBinaryContent();
            using (var ms = new MemoryStream(content))
            {
                texturePackThumbnail = Image.Load<Rgba32>(ms);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load built in texture pack");
        }
    }

    public override void Unload(TextureManager textureManager)
    {
        if (texturePackThumbnail != null && _texturePackName != null)
        {
            textureManager.Delete(_texturePackName);
        }
    }

    public override TextureHandle GetThumbnailTexture(TextureManager textureManager)
    {
        if (texturePackThumbnail != null && _texturePackName == null)
        {
            _texturePackName = textureManager.Load(texturePackThumbnail);
        }

        return _texturePackName ?? textureManager.GetTextureId("/gui/unknown_pack.png");
    }
}
