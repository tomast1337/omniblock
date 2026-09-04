using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OmniBlock.Client.DynamicTexture;

internal class ClockSprite : Rendering.Core.Textures.DynamicTexture
{
    private readonly ILogger<ClockSprite> _logger = Log.Instance.For<ClockSprite>();

    private double _angle;
    private double _angleDelta;
    private int[] _clock = new int[256];
    private int[] _dial = new int[256];
    private int _dialResolution = 16;
    private OmniBlock _game;
    private int _resolution = 16;

    public ClockSprite(OmniBlock game) : base(game.Content.Items.Get("omniblock:clock").GetTextureId(0))
    {
        _game = game;
        Atlas = FxImage.Items;
    }

    public override void Setup(OmniBlock game)
    {
        _game = game;
        var tm = game.TextureManager;
        var atlasPath = "/gui/items.png";

        var handle = tm.GetTextureId(atlasPath);
        if (handle.Texture != null)
        {
            _resolution = handle.Texture.Width / 16;
        }
        else
        {
            _resolution = 16;
        }

        var pixelCount = _resolution * _resolution;
        if (_clock.Length != pixelCount)
        {
            _clock = new int[pixelCount];
            _dial = new int[pixelCount];
            Pixels = new byte[pixelCount * 4];
        }

        try
        {
            using var stream = game.TexturePackList.SelectedTexturePack.GetResourceAsStream("gui/items.png");
            if (stream != null)
            {
                using var atlasImage = Image.Load<Rgba32>(stream);
                var atlasResolution = atlasImage.Width / 16;
                var sourceX = Sprite % 16 * atlasResolution;
                var sourceY = Sprite / 16 * atlasResolution;

                for (var y = 0; y < _resolution; y++)
                {
                    for (var x = 0; x < _resolution; x++)
                    {
                        var srcX = sourceX + x * atlasResolution / _resolution;
                        var srcY = sourceY + y * atlasResolution / _resolution;

                        var pixel = atlasImage[srcX, srcY];
                        _clock[y * _resolution + x] = (pixel.A << 24) | (pixel.R << 16) | (pixel.G << 8) | pixel.B;
                    }
                }
            }

            using var dialStream = game.TexturePackList.SelectedTexturePack.GetResourceAsStream("misc/dial.png");
            if (dialStream != null)
            {
                using var dialImage = Image.Load<Rgba32>(dialStream);
                _dialResolution = dialImage.Width;
                var dialPixelCount = _dialResolution * _dialResolution;

                if (_dial.Length != dialPixelCount)
                {
                    _dial = new int[dialPixelCount];
                }

                for (var y = 0; y < _dialResolution; y++)
                {
                    for (var x = 0; x < _dialResolution; x++)
                    {
                        var pixel = dialImage[x, y];
                        _dial[y * _dialResolution + x] = (pixel.A << 24) | (pixel.R << 16) | (pixel.G << 8) | pixel.B;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading clock sprite");
        }
    }

    public override void tick()
    {
        var targetAngle = 0.0D;
        if (_game.World != null && _game.Player != null)
        {
            var worldTime = _game.World.GetTime(1.0F);
            targetAngle = -worldTime * (float)Math.PI * 2.0F;
            if (_game.World.Dimension.IsNether)
            {
                targetAngle = Random.Shared.NextDouble() * (float)Math.PI * 2.0D;
            }
        }

        var angleDifference = Math.Atan2(Math.Sin(targetAngle - _angle), Math.Cos(targetAngle - _angle));

        while (angleDifference >= Math.PI)
        {
            angleDifference -= Math.PI * 2.0D;
        }

        if (angleDifference < -1.0D)
        {
            angleDifference = -1.0D;
        }

        if (angleDifference > 1.0D)
        {
            angleDifference = 1.0D;
        }

        _angleDelta += angleDifference * 0.1D;
        _angleDelta *= 0.8D;
        _angle += _angleDelta;

        var sinAngle = Math.Sin(_angle);
        var cosAngle = Math.Cos(_angle);

        var pixelCount = _resolution * _resolution;
        var invResMinus1 = 1.0f / (_resolution - 1);

        for (var pixelIdx = 0; pixelIdx < pixelCount; ++pixelIdx)
        {
            var alpha = (_clock[pixelIdx] >> 24) & 255;
            var red = (_clock[pixelIdx] >> 16) & 255;
            var green = (_clock[pixelIdx] >> 8) & 255;
            var blue = (_clock[pixelIdx] >> 0) & 255;

            // Logic to detect the "clock face" area (looks for specific bluish-gray tint)
            if (Math.Abs(red - blue) < 10 && green < 40 && red > 100)
            {
                var relX = -(pixelIdx % _resolution * invResMinus1 - 0.5D);
                var relY = pixelIdx / _resolution * invResMinus1 - 0.5D;
                var origRed = red;

                var dialX = (int)((relX * cosAngle + relY * sinAngle + 0.5D) * _dialResolution);
                var dialY = (int)((relY * cosAngle - relX * sinAngle + 0.5D) * _dialResolution);

                var dialIdx = (dialX & (_dialResolution - 1)) + (dialY & (_dialResolution - 1)) * _dialResolution;

                alpha = (_dial[dialIdx] >> 24) & 255;
                red = ((_dial[dialIdx] >> 16) & 255) * red / 255;
                green = ((_dial[dialIdx] >> 8) & 255) * origRed / 255;
                blue = ((_dial[dialIdx] >> 0) & 255) * origRed / 255;
            }

            Pixels[pixelIdx * 4 + 0] = (byte)red;
            Pixels[pixelIdx * 4 + 1] = (byte)green;
            Pixels[pixelIdx * 4 + 2] = (byte)blue;
            Pixels[pixelIdx * 4 + 3] = (byte)alpha;
        }
    }
}
