using System.Numerics;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Client.Rendering.Entities;

namespace OmniBlock.Tests.Rendering;

public sealed class EntityImpostorCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "omniblock-atlas-test-" + Guid.NewGuid().ToString("N"));
    private static string Key(char c) => new(c, 64);
    private static EntityImpostorCache.Atlas Atlas(char c = 'A') => new(Key(c), 2, new byte[EntityImpostorCache.PixelBytes]);
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public void Exact_round_trip_can_be_loaded_by_a_fresh_cache_owner()
    {
        var atlas = Atlas(); atlas.Pixels[1632] = 153; atlas.Pixels[^1] = 71;
        EntityImpostorCache.Write(_directory, atlas, default);
        var loaded = EntityImpostorCache.Read(_directory, atlas.Key, atlas.Radius, default)!;
        Assert.Equal(atlas.Pixels, loaded.Pixels);
        Assert.Equal(EntityImpostorCache.FileBytes, new FileInfo(EntityImpostorCache.PathFor(_directory, atlas.Key)).Length);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Theory]
    [InlineData(0)] [InlineData(8)] [InlineData(12)] [InlineData(16)] [InlineData(20)]
    [InlineData(24)] [InlineData(28)] [InlineData(32)] [InlineData(36)] [InlineData(70)] [InlineData(110)]
    public void Corrupt_header_identity_checksum_or_pixels_are_rejected(int offset)
    {
        var atlas = Atlas(); EntityImpostorCache.Write(_directory, atlas, default);
        var path = EntityImpostorCache.PathFor(_directory, atlas.Key);
        var bytes = File.ReadAllBytes(path); bytes[offset] ^= 127; File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => EntityImpostorCache.Read(_directory, atlas.Key, atlas.Radius, default));
    }

    [Fact]
    public void Truncated_and_oversized_files_are_rejected_before_reading_payload()
    {
        var atlas = Atlas(); EntityImpostorCache.Write(_directory, atlas, default);
        var path = EntityImpostorCache.PathFor(_directory, atlas.Key);
        using (var stream = File.OpenWrite(path)) stream.SetLength(20);
        Assert.Throws<InvalidDataException>(() => EntityImpostorCache.Read(_directory, atlas.Key, 2, default));
        using (var stream = File.OpenWrite(path)) stream.SetLength(EntityImpostorCache.FileBytes + 1);
        Assert.Throws<InvalidDataException>(() => EntityImpostorCache.Read(_directory, atlas.Key, 2, default));
    }

    [Fact]
    public void Cancelled_replacement_preserves_previous_complete_entry()
    {
        var atlas = Atlas(); EntityImpostorCache.Write(_directory, atlas, default);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var replacement = Atlas(); replacement.Pixels[0] = 44;
        Assert.Throws<OperationCanceledException>(() => EntityImpostorCache.Write(_directory, replacement, cancellation.Token));
        Assert.Equal(0, EntityImpostorCache.Read(_directory, atlas.Key, 2, default)!.Pixels[0]);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void Disk_lru_trims_only_its_owned_digest_files()
    {
        var a = Atlas('A'); var b = Atlas('B'); var c = Atlas('C');
        EntityImpostorCache.Write(_directory, a, default); EntityImpostorCache.Write(_directory, b, default);
        File.SetLastWriteTimeUtc(EntityImpostorCache.PathFor(_directory, a.Key), DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(EntityImpostorCache.PathFor(_directory, b.Key), DateTime.UtcNow.AddHours(-1));
        var unrelated = Path.Combine(_directory, "keep.atlas"); File.WriteAllText(unrelated, "not our cache");
        EntityImpostorCache.Write(_directory, c, default, EntityImpostorCache.FileBytes * 2);
        Assert.False(File.Exists(EntityImpostorCache.PathFor(_directory, a.Key)));
        Assert.True(File.Exists(EntityImpostorCache.PathFor(_directory, b.Key)));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void Unavailable_disk_is_an_io_error_and_does_not_damage_the_atlas()
    {
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "file"); File.WriteAllText(blocker, "keep");
        Assert.Throws<IOException>(() => EntityImpostorCache.Write(blocker, Atlas(), default));
        Assert.Equal("keep", File.ReadAllText(blocker));
        Assert.Throws<ArgumentException>(() => EntityImpostorCache.PathFor(_directory, "../outside"));
    }

    [Fact]
    public void Memory_cache_enforces_byte_entry_lru_limits_and_replacement_accounting()
    {
        var cache = new EntityImpostorMemoryCache(EntityImpostorCache.PixelBytes * 2, 2);
        var a = Atlas('A'); var b = Atlas('B'); var c = Atlas('C');
        cache.Put(a); cache.Put(b); Assert.Same(a, cache.Get(a.Key)); cache.Put(c);
        Assert.Null(cache.Get(b.Key)); Assert.Same(a, cache.Get(a.Key));
        cache.Put(a); Assert.Equal(2, cache.Count); Assert.Equal(EntityImpostorCache.PixelBytes * 2, cache.Bytes);
        cache.Clear(); Assert.Equal(0, cache.Bytes); Assert.Equal(0, cache.Count);
        var small = new EntityImpostorMemoryCache(1); small.Put(a); Assert.Equal(0, small.Count);
    }

    [Fact]
    public void Fingerprint_uses_effective_geometry_texture_sampler_and_capture_bounds()
    {
        CowImpostorGeometry.Vertex[] vertices = [new(Vector3.One, Vector2.One, Vector3.UnitY)];
        Texture2D.CaptureSource skin = new(1, 1, [1, 2, 3, 4], WgpuSamplerDescription.Nearest);
        var key = EntityImpostorCache.Key(vertices, skin, 2);
        Assert.Equal(key, EntityImpostorCache.Key(vertices.ToArray(), skin with { Pixels = skin.Pixels.ToArray() }, 2));
        Assert.NotEqual(key, EntityImpostorCache.Key(vertices, skin with { Pixels = [1, 2, 3, 5] }, 2));
        Assert.NotEqual(key, EntityImpostorCache.Key(vertices, skin with { Sampler = WgpuSamplerDescription.Linear }, 2));
        Assert.NotEqual(key, EntityImpostorCache.Key(vertices, skin, 3));
        vertices[0] = vertices[0] with { Position = Vector3.Zero };
        Assert.NotEqual(key, EntityImpostorCache.Key(vertices, skin, 2));
    }

    [Fact]
    public void Readback_removes_row_padding_without_swapping_rgba_or_flipping_rows()
    {
        var padded = new byte[512]; padded[0] = 1; padded[3] = 2; padded[256] = 3; padded[259] = 4;
        var pixels = new byte[8]; WgpuAtlasReadback.CopyRows(padded, pixels, 1, 2, 256);
        Assert.Equal(new byte[] { 1, 0, 0, 2, 3, 0, 0, 4 }, pixels);
        Assert.Throws<ArgumentException>(() => WgpuAtlasReadback.CopyRows(padded, pixels, 1, 2, 255));
    }
}
