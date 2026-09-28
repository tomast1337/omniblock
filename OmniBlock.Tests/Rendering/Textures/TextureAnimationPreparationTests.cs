using OmniBlock.Client.DynamicTexture;
using OmniBlock.Client.Rendering.Core.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OmniBlock.Tests.Rendering.Textures;

public sealed class TextureAnimationPreparationTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void Custom_strip_has_one_square_buffer_per_frame_and_cycles_in_order(int size)
    {
        using var context = new TextureAnimationContext(path => path switch
        {
            "terrain.png" => Png(size * 16, size * 16),
            "custom_water_flowing.png" => Strip(),
            _ => null
        });
        var water = new WaterSideSprite();
        water.Setup(context);
        Assert.Equal(size * size * 4, water.Pixels.Length);
        foreach (var expected in new[] { new Rgba32(255, 0, 0), new Rgba32(0, 0, 255), new Rgba32(255, 0, 0) })
        {
            water.tick();
            for (var i = 0; i < water.Pixels.Length; i += 4)
                Assert.Equal(new byte[] { expected.R, expected.G, expected.B, expected.A }, water.Pixels[i..(i + 4)]);
        }
    }

    [Theory]
    [InlineData("water")]
    [InlineData("water_flow")]
    [InlineData("lava")]
    [InlineData("lava_flow")]
    [InlineData("portal")]
    [InlineData("fire_0")]
    [InlineData("fire_1")]
    public void Reload_constructs_independent_procedural_sprites_without_advancing_live_rng(string kind)
    {
        using var context = new TextureAnimationContext(_ => null);
        var active = Sprite(kind);
        var control = Sprite(kind);
        active.RandomForTest = new Random(456);
        control.RandomForTest = new Random(456);
        active.Setup(context);
        control.Setup(context);
        for (var i = 0; i < 7; i++) { active.tick(); control.tick(); }
        var buffer = active.Pixels;
        var pixels = buffer.ToArray();

        var candidate = Assert.Single(DynamicTexture.PrepareReload([active], context));
        Assert.NotSame(active, candidate);
        Assert.NotSame(buffer, candidate.Pixels);
        Assert.Equal(active.GetType(), candidate.GetType());
        Assert.Equal(active.Sprite, candidate.Sprite);
        Assert.Equal(active.Atlas, candidate.Atlas);
        Assert.Equal(active.Replicate, candidate.Replicate);
        candidate.RandomForTest = new Random(987);
        for (var i = 0; i < 9; i++) candidate.tick();
        Assert.Same(buffer, active.Pixels);
        Assert.Equal(pixels, active.Pixels);
        active.tick(); control.tick();
        Assert.Equal(control.Pixels, active.Pixels);
    }

    [Fact]
    public void Late_failure_preserves_every_active_sprite_and_its_frame_position()
    {
        using var valid = new TextureAnimationContext(path => path switch
        {
            "terrain.png" => Png(256, 256),
            "custom_water_flowing.png" => Strip(),
            _ => null
        });
        DynamicTexture[] active = [new WaterSideSprite(), new FireSprite("fire_layer_0", "custom_fire_e_w.png")];
        foreach (var sprite in active) { sprite.Setup(valid); sprite.tick(); }
        var buffers = active.Select(s => s.Pixels).ToArray();
        var before = buffers.Select(p => p.ToArray()).ToArray();
        using var invalid = new TextureAnimationContext(path => path == "custom_fire_e_w.png"
            ? new MemoryStream([1, 2, 3]) : null);
        var error = Assert.Throws<InvalidDataException>(() => DynamicTexture.PrepareReload(active, invalid));
        Assert.Contains("custom_fire_e_w.png", error.Message);
        for (var i = 0; i < active.Length; i++)
        {
            Assert.Same(buffers[i], active[i].Pixels);
            Assert.Equal(before[i], active[i].Pixels);
        }
        active[0].tick();
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, active[0].Pixels[..4]);
    }

    [Fact]
    public void Removing_custom_strip_restores_procedural_size_without_retaining_old_frames()
    {
        using var valid = new TextureAnimationContext(path => path == "terrain.png" ? Png(512, 512) : Strip());
        var active = new WaterSideSprite();
        active.Setup(valid);
        active.tick();
        using var missing = new TextureAnimationContext(_ => null);
        var candidate = Assert.Single(DynamicTexture.PrepareReload([active], missing));
        Assert.Equal(1024, candidate.Pixels.Length);
        Assert.Equal(4096, active.Pixels.Length);
        candidate.tick();
        Assert.NotEqual(active.Pixels[..4], candidate.Pixels[..4]);
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(2, 514)]
    [InlineData(513, 513)]
    public void Malformed_or_excessive_strips_are_rejected_with_resource_name(int width, int height)
    {
        using var context = new TextureAnimationContext(_ => Png(width, height));
        var error = Assert.Throws<InvalidDataException>(() => context.ReadStrip("bad.png", DynamicTexture.FxImage.Terrain));
        Assert.Contains("bad.png", error.Message);
    }

    [Fact]
    public void Candidate_resource_io_failure_is_not_treated_as_optional_absence()
    {
        using var context = new TextureAnimationContext(_ => throw new IOException("failed read"));
        var error = Assert.Throws<InvalidDataException>(() => context.ReadStrip("water.png", DynamicTexture.FxImage.Terrain));
        Assert.Contains("water.png", error.Message);
        Assert.IsType<IOException>(error.InnerException);
    }

    [Fact]
    public void Item_tiles_use_candidate_atlas_coordinates_and_are_independent_of_image_lifetime()
    {
        var reads = 0;
        var context = new TextureAnimationContext(path =>
        {
            Assert.Equal("gui/items.png", path);
            reads++;
            return Png(512, 512, image => image[32, 32] = new Rgba32(10, 20, 30, 40));
        });
        var tile = context.ReadItemTile(17);
        var second = context.ReadItemTile(17);
        context.Dispose();
        context.Dispose();
        Assert.Equal(1, reads);
        Assert.Equal(32, tile.Size);
        Assert.Equal((40 << 24) | (10 << 16) | (20 << 8) | 30, tile.Pixels[0]);
        Assert.NotSame(tile.Pixels, second.Pixels);
        Assert.Equal(tile.Pixels, second.Pixels);
        Assert.Throws<ObjectDisposedException>(() => context.ReadItemTile(17));
    }

    [Theory]
    [InlineData(256, 128)]
    [InlineData(255, 255)]
    public void Invalid_item_atlas_is_rejected(int width, int height)
    {
        using var context = new TextureAnimationContext(_ => Png(width, height));
        Assert.Contains("gui/items.png", Assert.Throws<InvalidDataException>(() => context.ReadItemTile(0)).Message);
    }

    [Theory]
    [InlineData(16, 8)]
    [InlineData(3, 3)]
    [InlineData(1, 1)]
    public void Clock_dial_requires_square_power_of_two_dimensions(int width, int height)
    {
        using var context = new TextureAnimationContext(_ => Png(width, height));
        Assert.Contains("misc/dial.png", Assert.Throws<InvalidDataException>(() => context.ReadClockDial()).Message);
    }

    [Fact]
    public void Clock_dial_keeps_pixel_channels_and_alpha()
    {
        using var context = new TextureAnimationContext(_ => Png(32, 32, image => image[0, 0] = new Rgba32(8, 9, 10, 11)));
        var dial = context.ReadClockDial();
        Assert.Equal(32, dial.Size);
        Assert.Equal((11 << 24) | (8 << 16) | (9 << 8) | 10, dial.Pixels[0]);
    }

    private static DynamicTexture Sprite(string kind)
    {
        var blocks = new FakeWorldContext().Content.Blocks;
        return kind switch
        {
            "water" => new WaterSprite(blocks), "water_flow" => new WaterSideSprite(),
            "lava" => new LavaSprite(blocks), "lava_flow" => new LavaSideSprite(),
            "portal" => new NetherPortalSprite(blocks),
            "fire_0" => new FireSprite("fire_layer_0", "custom_fire_e_w.png"),
            "fire_1" => new FireSprite("fire_layer_1", "custom_fire_n_s.png"),
            _ => throw new ArgumentException("Unknown test sprite", nameof(kind))
        };
    }

    private static Stream Strip() => Png(2, 4, image =>
    {
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 2; x++)
            image[x, y] = y < 2 ? new Rgba32(255, 0, 0) : new Rgba32(0, 0, 255);
    });

    private static Stream Png(int width, int height, Action<Image<Rgba32>>? fill = null)
    {
        using var image = new Image<Rgba32>(width, height);
        fill?.Invoke(image);
        var stream = new MemoryStream();
        image.SaveAsPng(stream);
        stream.Position = 0;
        return stream;
    }
}
