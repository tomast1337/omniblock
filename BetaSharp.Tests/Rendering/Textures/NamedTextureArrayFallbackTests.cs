using OmniBlock.Client.Rendering.Core.Textures.Atlas;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace OmniBlock.Tests.Rendering.Textures;

public class NamedTextureArrayFallbackTests
{
    private static Stream EncodePng(Color color)
    {
        using var image = new Image<Rgba32>(4, 4);
        image.Mutate(ctx => ctx.BackgroundColor(color));
        var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void APackOverrideWinsWhenOnePresent()
    {
        Dictionary<string, Image<Rgba32>> defaults = new()
        {
            ["grass_block_top"] = new Image<Rgba32>(16, 16)
        };

        ResolvedTexture result = TextureFallbackChain.Resolve(
            "grass_block_top",
            _ => EncodePng(Color.Lime),
            defaults,
            fallbackSize: 16);

        Assert.Equal(TextureSource.Pack, result.Source);
        Assert.Equal(Color.Lime.ToPixel<Rgba32>(), result.Image[0, 0]);

        result.Image.Dispose();
        foreach (Image<Rgba32> image in defaults.Values) image.Dispose();
    }

    [Fact]
    public void TheDefaultWinsWhenThePackDoesNotOverrideTheName()
    {
        var defaultImage = new Image<Rgba32>(16, 16);
        defaultImage.Mutate(ctx => ctx.BackgroundColor(Color.Blue));
        Dictionary<string, Image<Rgba32>> defaults = new() { ["grass_block_top"] = defaultImage };

        ResolvedTexture result = TextureFallbackChain.Resolve(
            "grass_block_top",
            _ => null,
            defaults,
            fallbackSize: 16);

        Assert.Equal(TextureSource.Default, result.Source);
        Assert.Equal(Color.Blue.ToPixel<Rgba32>(), result.Image[0, 0]);

        // The chain must hand back an independent copy — disposing it must not take the shared
        // default tile down with it, since the same default backs every other name that falls
        // through to it too.
        result.Image.Dispose();
        Assert.Equal(Color.Blue.ToPixel<Rgba32>(), defaultImage[0, 0]);
        defaultImage.Dispose();
    }

    [Fact]
    public void TheCheckerboardWinsWhenNeitherPackNorDefaultHaveTheName()
    {
        Dictionary<string, Image<Rgba32>> defaults = [];

        ResolvedTexture result = TextureFallbackChain.Resolve(
            "totally_unknown_texture",
            _ => null,
            defaults,
            fallbackSize: 8);

        Assert.Equal(TextureSource.Fallback, result.Source);
        Assert.Equal(8, result.Image.Width);
        Assert.Equal(8, result.Image.Height);

        result.Image.Dispose();
    }

    [Fact]
    public void ACorruptPackOverrideFallsThroughToTheDefaultInsteadOfThrowing()
    {
        var defaultImage = new Image<Rgba32>(16, 16);
        defaultImage.Mutate(ctx => ctx.BackgroundColor(Color.Blue));
        Dictionary<string, Image<Rgba32>> defaults = new() { ["grass_block_top"] = defaultImage };

        ResolvedTexture result = TextureFallbackChain.Resolve(
            "grass_block_top",
            _ => new MemoryStream([1, 2, 3, 4]), // not a valid image
            defaults,
            fallbackSize: 16);

        Assert.Equal(TextureSource.Default, result.Source);

        result.Image.Dispose();
        defaultImage.Dispose();
    }
}
