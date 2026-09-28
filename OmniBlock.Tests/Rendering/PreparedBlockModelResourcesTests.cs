using System.Text;
using OmniBlock.Client.Rendering.Blocks.Models;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Core.Textures.Atlas;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OmniBlock.Tests.Rendering;

public sealed class PreparedBlockModelResourcesTests
{
    private static readonly RenderResourceId Model = RenderResourceId.Parse("example:block/test");
    private static readonly RenderResourceId Stone = RenderResourceId.Parse("example:block/stone");
    private static readonly RenderResourceId Dirt = RenderResourceId.Parse("example:block/dirt");
    private const string Json = """
        {"elements":[{"from":[0,0,0],"to":[16,16,16],"faces":{
          "up":{"texture":"example:block/stone"},"down":{"texture":"example:block/dirt"}
        }}]}
        """;

    [Fact]
    public void Candidate_preserves_fixed_layers_and_binds_models_to_its_own_pixels()
    {
        var fixedLayers = new Dictionary<RenderResourceId, int> { [Stone] = 7 };
        var candidate = Prepare(fixedLayers);
        fixedLayers[Stone] = 42;
        Assert.Equal(7, candidate.Layers[Stone]);
        Assert.Equal(1, candidate.Layers[Dirt]);
        Assert.Equal(8, candidate.LayerCount);
        Assert.Equal(16, candidate.LayerSize);
        foreach (var quad in candidate.Models.Get(Model).Quads)
        {
            Assert.Equal(candidate.Layers[quad.Texture], quad.ArrayLayer);
            AssertPixel(candidate, quad.ArrayLayer, quad.Texture == Stone ? new Rgba32(255, 0, 0, 255) : new Rgba32(0, 0, 255, 255));
        }
        using var missing = MissingTextureImage.Generate(16);
        var expectedMissing = new byte[16 * 16 * 4];
        missing.CopyPixelDataTo(expectedMissing);
        Assert.Equal(expectedMissing, candidate.Pixels[..expectedMissing.Length].ToArray());
        Assert.Equal(candidate.Pixels[..(16 * 16 * 4)].ToArray(), candidate.Pixels.Slice(2 * 16 * 16 * 4, 16 * 16 * 4).ToArray());
    }

    [Fact]
    public void New_layer_allocation_does_not_depend_on_definition_or_texture_variable_order()
    {
        var first = Prepare();
        var reversed = Json.Replace("\"up\":{\"texture\":\"example:block/stone\"},\"down\":{\"texture\":\"example:block/dirt\"}",
            "\"down\":{\"texture\":\"example:block/dirt\"},\"up\":{\"texture\":\"example:block/stone\"}");
        Assert.NotEqual(Json, reversed);
        var second = Prepare(json: reversed);
        Assert.Equal(1, first.Layers[Dirt]);
        Assert.Equal(2, first.Layers[Stone]);
        Assert.Equal(first.Models.Get(Model).Quads.ToArray(), second.Models.Get(Model).Quads.ToArray());
        Assert.Equal(first.Pixels.ToArray(), second.Pixels.ToArray());
    }

    [Fact]
    public void Override_pixels_win_and_all_layers_resize_to_the_same_edge()
    {
        var streams = new List<TrackedStream>();
        var candidate = Prepare(openOverride: path =>
        {
            if (!path.EndsWith("stone.png", StringComparison.Ordinal)) return null;
            var stream = new TrackedStream(Png(new Rgba32(0, 255, 0, 127), 32));
            streams.Add(stream);
            return stream;
        });
        Assert.Equal(32, candidate.LayerSize);
        Assert.Equal(TextureSource.Pack, candidate.Sources[Stone]);
        Assert.Equal(TextureSource.Default, candidate.Sources[Dirt]);
        AssertPixel(candidate, candidate.Layers[Stone], new Rgba32(0, 255, 0, 127));
        AssertPixel(candidate, candidate.Layers[Dirt], new Rgba32(0, 0, 255, 255));
        Assert.All(streams, stream => Assert.True(stream.Disposed));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(256)]
    public void Invalid_fixed_layers_fail_before_reading_resources(int layer)
    {
        Assert.Throws<InvalidDataException>(() => PreparedBlockModelResources.Build([Model],
            new Dictionary<RenderResourceId, int> { [Stone] = layer },
            _ => throw new Exception("must not read"), _ => throw new Exception("must not read")));
    }

    [Fact]
    public void Duplicate_fixed_layers_are_rejected() => Assert.Throws<InvalidDataException>(() => Prepare(
        new Dictionary<RenderResourceId, int> { [Stone] = 1, [Dirt] = 1 }));

    [Fact]
    public void More_than_255_textures_cannot_wrap_byte_sized_layer_bindings()
    {
        var bindings = Enumerable.Range(1, 255).ToDictionary(i => RenderResourceId.Parse("example:t" + i), i => i);
        Assert.Contains("255", Assert.Throws<InvalidDataException>(() => Prepare(bindings)).Message);
    }

    [Fact]
    public void Malformed_override_does_not_fall_back_or_change_previous_resources()
    {
        var active = Prepare();
        var previous = active;
        var originalPixels = active.Pixels.ToArray();
        using var invalid = new TrackedStream([1, 2, 3, 4]);
        var error = Assert.Throws<InvalidDataException>(() => active = Prepare(openOverride: path =>
            path.EndsWith("stone.png", StringComparison.Ordinal) ? invalid : null));
        Assert.Contains(Stone.ToString(), error.Message);
        Assert.Contains("assets/example/textures/block/stone.png", error.Message);
        Assert.True(invalid.Disposed);
        Assert.Same(previous, active);
        Assert.Equal(originalPixels, active.Pixels.ToArray());
    }

    [Theory]
    [InlineData(513, 513)]
    [InlineData(32, 16)]
    public void Unsupported_dimensions_fail_before_texture_array_allocation(int width, int height)
    {
        using var overrideStream = new TrackedStream(Png(new Rgba32(1, 2, 3, 255), width, height));
        Assert.Contains("square texture", Assert.Throws<InvalidDataException>(() => Prepare(openOverride: path =>
            path.EndsWith("stone.png", StringComparison.Ordinal) ? overrideStream : null)).Message);
        Assert.True(overrideStream.Disposed);
    }

    [Fact]
    public void Packed_array_budget_counts_unused_fixed_layer_gaps()
    {
        Assert.Contains("Packed texture array", Assert.Throws<InvalidDataException>(() => Prepare(
            new Dictionary<RenderResourceId, int> { [Stone] = 255 },
            openOverride: path => path.EndsWith("stone.png", StringComparison.Ordinal)
                ? new MemoryStream(Png(new Rgba32(1, 2, 3, 255), 512)) : null)).Message);
    }

    [Fact]
    public void Missing_texture_does_not_become_a_silent_checkerboard()
    {
        var error = Assert.Throws<InvalidDataException>(() => PreparedBlockModelResources.Build([Model],
            new Dictionary<RenderResourceId, int>(), _ => null,
            path => path == Model.ModelPath ? new MemoryStream(Encoding.UTF8.GetBytes(Json)) : null));
        Assert.Contains("required texture is missing", error.Message);
    }

    [Fact]
    public void Encoded_byte_budget_is_enforced_and_stream_is_closed()
    {
        using var stream = new TrackedStream(new byte[PreparedBlockModelResources.MaximumTextureBytes + 1]);
        Assert.Contains("byte limit", Assert.Throws<InvalidDataException>(() => Prepare(openOverride: path =>
            path.EndsWith("stone.png", StringComparison.Ordinal) ? stream : null)).Message);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public void Independent_candidates_do_not_share_models_or_mutable_pixel_copies()
    {
        var first = Prepare();
        var second = Prepare();
        Assert.NotSame(first.Models.Get(Model), second.Models.Get(Model));
        var copy = first.Pixels.ToArray();
        copy.AsSpan().Clear();
        Assert.Equal(second.Pixels.ToArray(), first.Pixels.ToArray());
    }

    [Fact]
    public void Upload_without_a_device_fails_instead_of_returning_a_phantom_gpu_candidate()
    {
        var candidate = Prepare();
        var count = TextureArray.ActiveTextureCount;
        Assert.Throws<InvalidOperationException>(() => candidate.Upload(true));
        Assert.Equal(count, TextureArray.ActiveTextureCount);
    }

    private static PreparedBlockModelResources Prepare(Dictionary<RenderResourceId, int>? bindings = null,
        Func<string, Stream?>? openOverride = null, string json = Json) => PreparedBlockModelResources.Build(
        [Model], bindings ?? [], openOverride ?? (_ => null), path =>
            path == Model.ModelPath ? new MemoryStream(Encoding.UTF8.GetBytes(json)) :
            path.EndsWith("stone.png", StringComparison.Ordinal) ? new MemoryStream(Png(new Rgba32(255, 0, 0, 255), 16)) :
            path.EndsWith("dirt.png", StringComparison.Ordinal) ? new MemoryStream(Png(new Rgba32(0, 0, 255, 255), 16)) : null);

    private static byte[] Png(Rgba32 color, int width, int? height = null)
    {
        using var image = new Image<Rgba32>(width, height ?? width, color);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static void AssertPixel(PreparedBlockModelResources candidate, int layer, Rgba32 color)
    {
        var pixels = candidate.Pixels.Slice(layer * candidate.LayerSize * candidate.LayerSize * 4,
            candidate.LayerSize * candidate.LayerSize * 4);
        for (var i = 0; i < pixels.Length; i += 4)
            Assert.Equal(color, new Rgba32(pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]));
    }

    private sealed class TrackedStream(byte[] data) : MemoryStream(data)
    {
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
