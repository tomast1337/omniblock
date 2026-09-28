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
        UpdateAvailableTexturePacks(initial: true);
        SelectedTexturePack.func_6482_a();
    }

    public List<TexturePack> AvailableTexturePacks { get; private set; } = [];

    public bool setTexturePack(TexturePack texturePack)
    {
        if (texturePack == SelectedTexturePack) return false;
        var name = texturePack.TexturePackFileName ?? "Default";
        try
        {
            // Keep legacy lazy resource reads ready, without closing or changing the active pack.
            if (texturePack is ZippedTexturePack zipped) zipped.OpenForSelection();
            else texturePack.func_6482_a();
            if (!_game.TextureManager.TryReload(texturePack, () => _game.Options.SaveTexturePackSelection(name)))
            {
                texturePack.CloseTexturePackFile();
                return false;
            }
        }
        catch (Exception ex)
        {
            texturePack.CloseTexturePackFile();
            _logger.LogError(ex, "Failed to prepare texture pack {Pack}.", name);
            return false;
        }
        var previous = SelectedTexturePack;
        _currentTexturePack = name;
        SelectedTexturePack = texturePack;
        _game.Options.Skin = name;
        try { previous.CloseTexturePackFile(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to close retired texture pack."); }
        return true;
    }

    public void updateAvaliableTexturePacks() => UpdateAvailableTexturePacks(initial: false);

    private void UpdateAvailableTexturePacks(bool initial)
    {
        List<TexturePack> availablePacks = [];
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

                    if (initial && cachedPack.TexturePackFileName == _currentTexturePack)
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

        // A directory refresh is not a resource transaction. Keep a removed/updated active pack
        // alive until the user explicitly selects a replacement that successfully prepares.
        if (!availablePacks.Contains(SelectedTexturePack)) availablePacks.Add(SelectedTexturePack);

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
