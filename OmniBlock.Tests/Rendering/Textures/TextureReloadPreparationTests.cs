using System.IO.Compression;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Core.Textures.Atlas;
using OmniBlock.Client.Resource.Pack;
using OmniBlock.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OmniBlock.Tests.Rendering.Textures;

public sealed class TextureReloadPreparationTests
{
    [Fact]
    public void Published_arrays_cannot_reenter_the_disposed_preparation_readers()
    {
        using var array = new NamedTextureArray("terrain", new AtlasTileMap(),
            () => throw new Exception("must not read"), () => new BuiltInTexturePack());
        array.Seal();
        Assert.Throws<InvalidOperationException>(() => array.Rebuild());
    }

    [Fact]
    public void Strict_reader_distinguishes_missing_corrupt_and_failed_reads()
    {
        Assert.Null(new ReloadImageReader(_ => null).Read("optional.png"));
        var corrupt = new ReloadImageReader(_ => new MemoryStream([1, 2, 3]));
        Assert.Contains("bad.png", Assert.Throws<InvalidDataException>(() => corrupt.Read("bad.png")).Message);
        var failed = new ReloadImageReader(_ => throw new IOException("disk failure"));
        Assert.IsType<IOException>(Assert.Throws<InvalidDataException>(() => failed.Read("bad.png")).InnerException);
    }

    [Theory]
    [InlineData(256, 128)]
    [InlineData(255, 255)]
    public void Color_maps_cannot_publish_wrong_dimensions(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        Assert.Contains("grass", Assert.Throws<InvalidDataException>(() => ReloadImageReader.ValidateColors(image, "grass")).Message);
    }

    [Fact]
    public void Zip_snapshot_is_independent_of_legacy_open_and_preserves_missing_override()
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-reload-");
        try
        {
            var file = Path.Combine(directory.FullName, "pack.zip");
            using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("test.txt").Open())) writer.Write("candidate");
            var pack = new ZippedTexturePack(new FileInfo(file));
            using var source = new TexturePackSnapshot(pack);
            pack.CloseTexturePackFile();
            using var stream = source.OpenOverride("/test.txt");
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream);
            Assert.Equal("candidate", reader.ReadToEnd());
            Assert.Null(source.OpenOverride("missing.txt"));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void Duplicate_zip_paths_reject_the_candidate()
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-reload-duplicate-");
        try
        {
            var file = Path.Combine(directory.FullName, "pack.zip");
            using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                archive.CreateEntry("terrain.png");
                archive.CreateEntry("terrain.png");
            }
            var error = Assert.Throws<InvalidDataException>(() => new TexturePackSnapshot(new ZippedTexturePack(new FileInfo(file))));
            Assert.Contains("terrain.png", error.Message);
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("assets/../secret")]
    [InlineData("C:/secret")]
    [InlineData("assets\\secret")]
    public void Candidate_paths_cannot_escape_resource_roots(string path)
    {
        using var source = new TexturePackSnapshot(new BuiltInTexturePack());
        Assert.Throws<InvalidDataException>(() => source.OpenOverride(path));
        Assert.Throws<InvalidDataException>(() => TexturePackSnapshot.OpenBuiltin(path));
    }

    [Fact]
    public void Selection_persistence_does_not_mutate_live_option_before_commit()
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-pack-option-");
        try
        {
            var options = new GameOptions(null!, directory.FullName) { Skin = "OldPack" };
            Assert.True(options.SaveTexturePackSelection("NewPack"));
            Assert.Equal("OldPack", options.Skin);
            Assert.Equal("NewPack", new GameOptions(null!, directory.FullName).Skin);
            Assert.Empty(directory.GetFiles("*.tmp"));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void Failed_selection_persistence_reports_failure_and_cleans_candidate_file()
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-pack-option-failure-");
        try
        {
            var options = new GameOptions(null!, directory.FullName) { Skin = "OldPack" };
            Directory.CreateDirectory(Path.Combine(directory.FullName, "options.json"));
            Assert.False(options.SaveTexturePackSelection("NewPack"));
            Assert.Equal("OldPack", options.Skin);
            Assert.Empty(directory.GetFiles());
        }
        finally { directory.Delete(true); }
    }
}
