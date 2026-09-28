using OmniBlock.Client.Rendering.Core.Textures;

namespace OmniBlock.Client.DynamicTexture;

internal class CompassSprite : Rendering.Core.Textures.DynamicTexture
{
    private double _angle;
    private double _angleDelta;
    private int[] _compass = new int[256];
    private readonly OmniBlock _game;
    private int _resolution = 16;

    public CompassSprite(OmniBlock game) : base(game.Content.Items.Get("omniblock:compass").GetTextureId(0))
    {
        _game = game;
        Atlas = FxImage.Items;
    }

    internal override Rendering.Core.Textures.DynamicTexture CreateReloadCopy() => new CompassSprite(_game);

    internal override void Setup(TextureAnimationContext context)
    {
        (_resolution, _compass) = context.ReadItemTile(Sprite);
        Pixels = new byte[_resolution * _resolution * 4];
    }

    public override void tick()
    {
        var pixelCount = _resolution * _resolution;

        for (var i = 0; i < pixelCount; ++i)
        {
            var a = (_compass[i] >> 24) & 255;
            var r = (_compass[i] >> 16) & 255;
            var g = (_compass[i] >> 8) & 255;
            var b = (_compass[i] >> 0) & 255;
            Pixels[i * 4 + 0] = (byte)r;
            Pixels[i * 4 + 1] = (byte)g;
            Pixels[i * 4 + 2] = (byte)b;
            Pixels[i * 4 + 3] = (byte)a;
        }

        var targetAngle = 0.0D;
        if (_game.World != null && _game.Player != null)
        {
            var spawnPos = _game.World.Properties.GetSpawnPos();
            var deltaX = spawnPos.X - _game.Player.X;
            var deltaZ = spawnPos.Z - _game.Player.Z;

            targetAngle = (_game.Player.Yaw - 90.0F) * Math.PI / 180.0D - Math.Atan2(deltaZ, deltaX);

            if (_game.World.Dimension.IsNether)
            {
                targetAngle = Random.Shared.NextDouble() * (float)Math.PI * 2.0D;
            }
        }

        double angleDiff;
        for (angleDiff = targetAngle - _angle; angleDiff < -Math.PI; angleDiff += Math.PI * 2.0D)
        {
            ;
        }

        while (angleDiff >= Math.PI)
        {
            angleDiff -= Math.PI * 2.0D;
        }

        if (angleDiff < -1.0D)
        {
            angleDiff = -1.0D;
        }

        if (angleDiff > 1.0D)
        {
            angleDiff = 1.0D;
        }

        _angleDelta += angleDiff * 0.1D;
        _angleDelta *= 0.8D;
        _angle += _angleDelta;

        var sinAngle = Math.Sin(_angle);
        var cosAngle = Math.Cos(_angle);

        var center = (_resolution - 1) / 2.0f;
        var needleScale = _resolution / 16.0f;

        for (var offset = -Math.Max(1, _resolution / 4); offset <= Math.Max(1, _resolution / 4); ++offset)
        {
            var pixelX = (int)(center + 0.5f + cosAngle * offset * 0.3D * needleScale);
            var pixelY = (int)(center - 0.5f - sinAngle * offset * 0.3D * 0.5D * needleScale);

            if (pixelX < 0 || pixelX >= _resolution || pixelY < 0 || pixelY >= _resolution)
            {
                continue;
            }

            var pixelIdx = pixelY * _resolution + pixelX;
            Pixels[pixelIdx * 4 + 0] = 100; // R
            Pixels[pixelIdx * 4 + 1] = 100; // G
            Pixels[pixelIdx * 4 + 2] = 100; // B
            Pixels[pixelIdx * 4 + 3] = 255; // A
        }

        for (var offset = -Math.Max(1, _resolution / 2); offset <= _resolution; ++offset)
        {
            var pixelX = (int)(center + 0.5f + sinAngle * offset * 0.3D * needleScale);
            var pixelY = (int)(center - 0.5f + cosAngle * offset * 0.3D * 0.5D * needleScale);

            if (pixelX < 0 || pixelX >= _resolution || pixelY < 0 || pixelY >= _resolution)
            {
                continue;
            }

            var pixelIdx = pixelY * _resolution + pixelX;
            var isPointyEnd = offset >= 0;

            Pixels[pixelIdx * 4 + 0] = (byte)(isPointyEnd ? 255 : 100);
            Pixels[pixelIdx * 4 + 1] = (byte)(isPointyEnd ? 20 : 100);
            Pixels[pixelIdx * 4 + 2] = (byte)(isPointyEnd ? 20 : 100);
            Pixels[pixelIdx * 4 + 3] = 255;
        }
    }
}
