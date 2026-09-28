using OmniBlock.Client.Rendering.Blocks.Models;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OmniBlock.Tests.Rendering;

public sealed class BlockModelPublicationTests
{
    private static readonly AtlasTileMap Map = new()
    {
        GridWidth = 2, GridHeight = 1, TileSize = 16,
        Tiles = [new AtlasTile("stone", 0, 0), new AtlasTile("dirt", 1, 0)]
    };
    private const string StonePath = "assets/omniblock/textures/stone.png";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Override_precedence_is_canonical_then_legacy_named_then_grid(int selected)
    {
        using var source = new BlockModelPackResources(Map, path => path switch
        {
            StonePath when selected == 0 => Png(16, 16, new Rgba32(255, 0, 0)),
            "textures/terrain/stone.png" when selected <= 1 => Png(16, 16, new Rgba32(0, 255, 0)),
            "terrain.png" => Png(32, 16, new Rgba32(0, 0, 255)),
            _ => null
        }, _ => null, () => throw new Exception("must not use builtin"));
        using var stream = source.OpenOverride(StonePath);
        Assert.NotNull(stream);
        using var image = Image.Load<Rgba32>(stream);
        Assert.Equal(16, image.Width);
        Assert.Equal(selected switch { 0 => new Rgba32(255, 0, 0), 1 => new Rgba32(0, 255, 0), _ => new Rgba32(0, 0, 255) }, image[0, 0]);
        Assert.Equal(1, source.FixedLayers()[RenderResourceId.Parse("omniblock:stone")]);
    }

    [Fact]
    public void Builtin_atlas_is_loaded_once_and_tiles_use_their_declared_coordinates()
    {
        var loads = 0;
        using var source = new BlockModelPackResources(Map, _ => null, _ => null, () =>
        {
            loads++;
            var grid = new Image<Rgba32>(32, 16, new Rgba32(1, 2, 3));
            grid[16, 0] = new Rgba32(4, 5, 6);
            return grid;
        });
        using var stone = source.OpenBuiltin(StonePath);
        using var dirt = source.OpenBuiltin("assets/omniblock/textures/dirt.png");
        Assert.NotNull(stone);
        Assert.NotNull(dirt);
        using var stoneImage = Image.Load<Rgba32>(stone);
        using var dirtImage = Image.Load<Rgba32>(dirt);
        Assert.Equal(new Rgba32(1, 2, 3), stoneImage[0, 0]);
        Assert.Equal(new Rgba32(4, 5, 6), dirtImage[0, 0]);
        Assert.Equal(1, loads);
        Assert.Null(source.OpenBuiltin("assets/other/textures/stone.png"));
    }

    [Fact]
    public void Broken_grid_override_does_not_fall_back_to_builtin()
    {
        using var source = new BlockModelPackResources(Map,
            path => path == "terrain.png" ? Png(31, 16, new Rgba32(0, 0, 0)) : null,
            _ => null, () => throw new Exception("must not use builtin"));
        Assert.Throws<InvalidDataException>(() => source.OpenOverride(StonePath));
    }

    [Fact]
    public void Slot_replaces_one_pair_and_disposes_only_previous_or_shutdown_resources()
    {
        using var slot = new BlockModelResourceSlot();
        var first = Pair();
        var second = Pair();
        slot.Replace(() => first);
        Assert.Equal(1, slot.Generation);
        Assert.Throws<InvalidDataException>(() => slot.Replace(() => throw new InvalidDataException("failed prepare")));
        Assert.Same(first, slot.Current);
        Assert.NotEqual(0u, first.Texture.Id);
        Assert.Equal(1, slot.Generation);
        slot.Replace(() => second);
        Assert.Same(second, slot.Current);
        Assert.Equal(0u, first.Texture.Id);
        Assert.Equal(2, slot.Generation);
        slot.Dispose();
        Assert.Equal(0u, second.Texture.Id);
        Assert.Null(slot.Current);
        Assert.Throws<ObjectDisposedException>(() => slot.Replace(Pair));
    }

    [Fact]
    public void Wrong_thread_is_rejected_before_allocating_a_candidate()
    {
        using var slot = new BlockModelResourceSlot();
        Exception? error = null;
        var invoked = false;
        var thread = new Thread(() =>
        {
            try { slot.Replace(() => { invoked = true; return Pair(); }); }
            catch (Exception ex) { error = ex; }
        });
        thread.Start();
        thread.Join();
        Assert.IsType<InvalidOperationException>(error);
        Assert.False(invoked);
        Assert.Null(slot.Current);
    }

    private static UploadedBlockModelResources Pair() => new(
        BlockModelCatalog.Build([], _ => 1), new TextureArray("publication-test"));

    private static MemoryStream Png(int width, int height, Rgba32 color)
    {
        using var image = new Image<Rgba32>(width, height, color);
        var stream = new MemoryStream();
        image.SaveAsPng(stream);
        stream.Position = 0;
        return stream;
    }
}
