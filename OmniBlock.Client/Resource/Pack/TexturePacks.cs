using Microsoft.Extensions.Logging;

namespace OmniBlock.Client.Resource.Pack;

public class TexturePacks
{
    private readonly TexturePack _defaultTexturePack = new BuiltInTexturePack();
    private readonly OmniBlock _game;
    private readonly ILogger _logger = Log.Instance.For<TexturePacks>();
    private readonly DirectoryInfo _texturePackDir;
    private readonly Dictionary<string, TexturePack> _texturePacks = [];
    private string? _currentTexturePack;
    public TexturePack SelectedTexturePack;

    public TexturePacks(OmniBlock game, DirectoryInfo texturePackDir)
    {
        _game = game;
        SelectedTexturePack = _defaultTexturePack;
        _texturePackDir = new DirectoryInfo(Path.Combine(texturePackDir.FullName, "texturepacks"));
        if (!_texturePackDir.Exists)
        {
            _texturePackDir.Create();
        }

        _currentTexturePack = game.Options.Skin;
        updateAvaliableTexturePacks();
        SelectedTexturePack.func_6482_a();
    }

    public List<TexturePack> AvailableTexturePacks { get; private set; } = [];

    public bool setTexturePack(TexturePack texturePack)
    {
        if (texturePack == SelectedTexturePack)
        {
            return false;
        }

        SelectedTexturePack.CloseTexturePackFile();
        _currentTexturePack = texturePack.TexturePackFileName;
        SelectedTexturePack = texturePack;

        _game.Options.Skin = _currentTexturePack ?? "Default";
        _game.Options.SaveOptions();

        SelectedTexturePack.func_6482_a();
        return true;
    }

    public void updateAvaliableTexturePacks()
    {
        List<TexturePack> availablePacks = [];
        SelectedTexturePack = _defaultTexturePack;
        availablePacks.Add(_defaultTexturePack);

        if (_texturePackDir.Exists)
        {
            foreach (var file in _texturePackDir.GetFiles("*.zip"))
            {
                var signature = $"{file.Name}:{file.Length}:{file.LastWriteTimeUtc.Ticks}";

                try
                {
                    if (!_texturePacks.TryGetValue(signature, out var cachedPack))
                    {
                        ZippedTexturePack newPack = new(file)
                        {
                            Signature = signature
                        };
                        _texturePacks[signature] = newPack;
                        newPack.func_6485_a(_game);
                        cachedPack = newPack;
                    }

                    if (cachedPack.TexturePackFileName == _currentTexturePack)
                    {
                        SelectedTexturePack = cachedPack;
                    }

                    availablePacks.Add(cachedPack);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load texture pack {File}", file.Name);
                }
            }
        }

        SelectedTexturePack ??= _defaultTexturePack;

        foreach (var oldPack in AvailableTexturePacks)
        {
            if (!availablePacks.Contains(oldPack))
            {
                oldPack.Unload(_game.TextureManager);
                if (oldPack.Signature != null)
                {
                    _texturePacks.Remove(oldPack.Signature);
                }
            }
        }

        AvailableTexturePacks = availablePacks;
    }
}
