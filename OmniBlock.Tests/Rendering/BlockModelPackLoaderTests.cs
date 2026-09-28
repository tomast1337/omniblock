using System.IO.Compression;
using System.Text;
using OmniBlock.Client.Rendering.Blocks.Models;

namespace OmniBlock.Tests.Rendering;

public sealed class BlockModelPackLoaderTests
{
    private static readonly RenderResourceId Child = RenderResourceId.Parse("example:block/stone");
    private static readonly RenderResourceId Parent = RenderResourceId.Parse("example:block/templates/cube");
    private const string ChildJson = """{"parent":"example:block/templates/cube","textures":{"all":"example:block/stone"}}""";
    private const string ParentJson = """
        {"elements":[{"from":[0,0,0],"to":[16,16,16],"faces":{"up":{"texture":"#all"}}}]}
        """;

    [Fact]
    public void Render_paths_are_namespaced_and_do_not_change_gameplay_identity_rules()
    {
        Assert.Equal("assets/example/models/block/stone.json", Child.ModelPath);
        Assert.Equal(Child, RenderResourceId.Parse(Child.ToString()));
        Assert.NotEqual(Child, RenderResourceId.Parse("other:block/stone"));
        Assert.Throws<ArgumentException>(() => ResourceLocation.Parse("example:block/stone"));
    }

    [Theory]
    [InlineData("block/stone")]
    [InlineData("example:/block/stone")]
    [InlineData("example:block/stone/")]
    [InlineData("example:block//stone")]
    [InlineData("example:block/../stone")]
    [InlineData("example:./stone")]
    [InlineData("..:block/stone")]
    [InlineData("example:block\\stone")]
    [InlineData("example:block/%2e%2e/stone")]
    [InlineData("Example:block/stone")]
    [InlineData("example:Block/stone")]
    [InlineData("example:block:stone")]
    [InlineData("example:block/stone\0")]
    public void Unsafe_or_ambiguous_paths_are_rejected(string name) => Assert.Throws<FormatException>(() => RenderResourceId.Parse(name));

    [Fact]
    public void Zip_override_inherits_builtin_parent_and_closes_all_resources()
    {
        using var zip = new TestZip((Child.ModelPath, ChildJson));
        TrackingStream? builtin = null;
        var catalog = BlockModelPackLoader.BuildFromZip(zip.Path, [Child], path =>
        {
            Assert.Equal(Parent.ModelPath, path);
            return builtin = Bytes(ParentJson);
        }, Resolve);
        Assert.Equal(1, catalog.Count);
        Assert.Equal(RenderResourceId.Parse("example:block/stone"), catalog.Get(Child).Quads[0].Texture);
        Assert.Equal(7, catalog.Get(Child).Quads[0].ArrayLayer);
        Assert.NotNull(builtin);
        Assert.True(builtin.Disposed);
        // No archive lifetime is retained by the compiled candidate.
        using var exclusive = new FileStream(zip.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void Builtin_child_can_inherit_overridden_parent_and_shared_parent_is_read_once()
    {
        var second = RenderResourceId.Parse("example:block/second");
        var reads = new List<string>();
        var streams = new List<TrackingStream>();
        var catalog = BlockModelPackLoader.Build([Child, second], path =>
        {
            reads.Add(path);
            if (path != Parent.ModelPath) return null;
            var stream = Bytes(ParentJson.Replace("\"faces\":", "\"shade\":false,\"faces\":"));
            streams.Add(stream);
            return stream;
        }, _ =>
        {
            var stream = Bytes(ChildJson);
            streams.Add(stream);
            return stream;
        }, Resolve);
        Assert.Equal(1, reads.Count(path => path == Parent.ModelPath));
        Assert.Equal(2, catalog.Count);
        Assert.False(catalog.Get(Child).Quads[0].Shade);
        Assert.All(streams, stream => Assert.True(stream.Disposed));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"parent\":\"example:block/absent\"}")]
    [InlineData("{\"parent\":\"example:block/stone\"}")]
    public void Invalid_zip_override_never_falls_back_to_builtin_for_that_model(string broken)
    {
        using var zip = new TestZip((Child.ModelPath, broken));
        var builtinReads = new List<string>();
        var error = Assert.Throws<InvalidDataException>(() => BlockModelPackLoader.BuildFromZip(zip.Path, [Child], path =>
        {
            builtinReads.Add(path);
            return null;
        }, Resolve));
        Assert.Contains(Child.ToString(), error.Message);
        Assert.DoesNotContain(Child.ModelPath, builtinReads);
    }

    [Fact]
    public void Override_io_failure_is_contextual_and_cannot_replace_active_catalog()
    {
        var active = BlockModelPackLoader.Build([Child], _ => null,
            path => Bytes(path == Child.ModelPath ? ChildJson : ParentJson), Resolve);
        var old = active;
        var fallbackReads = 0;
        var error = Assert.Throws<InvalidDataException>(() => active = BlockModelPackLoader.Build([Child],
            _ => throw new IOException("broken archive"), _ => { fallbackReads++; return null; }, Resolve));
        Assert.Same(old, active);
        Assert.Equal(0, fallbackReads);
        Assert.Contains(Child.ModelPath, error.Message);
        Assert.Contains("override", error.Message);
        Assert.Contains("broken archive", error.Message);
    }

    [Fact]
    public void Duplicate_zip_entries_and_duplicate_roots_fail()
    {
        using var zip = new TestZip((Child.ModelPath, ChildJson), (Child.ModelPath, ChildJson));
        Assert.Contains("duplicate", Assert.Throws<InvalidDataException>(() =>
            BlockModelPackLoader.BuildFromZip(zip.Path, [Child], _ => null, Resolve)).Message);
        Assert.Contains("duplicate", Assert.Throws<InvalidDataException>(() =>
            BlockModelPackLoader.Build([Child, Child], _ => null, _ => null, Resolve)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Strict_utf8_and_bounded_nonseekable_reads_dispose_streams_on_failure(bool oversized)
    {
        using var stream = oversized ? new TrackingStream(new byte[BlockModelPackLoader.MaximumModelBytes + 1]) : new TrackingStream([0xC3, 0x28]);
        var error = Assert.Throws<InvalidDataException>(() => BlockModelPackLoader.Build([Child], _ => stream,
            _ => throw new InvalidOperationException("must not fall back"), Resolve));
        Assert.True(stream.Disposed);
        Assert.Contains(Child.ModelPath, error.Message);
        if (oversized) Assert.Contains("byte limit", error.Message);
    }

    [Fact]
    public void Aggregate_read_budget_is_enforced_before_the_entire_catalog_is_materialized()
    {
        var roots = Enumerable.Range(0, 70).Select(i => RenderResourceId.Parse("example:block/m" + i));
        var payload = Encoding.UTF8.GetBytes("{\"elements\":[]}" + new string(' ', 250000));
        var streams = new List<TrackingStream>();
        var error = Assert.Throws<InvalidDataException>(() => BlockModelPackLoader.Build(roots, _ =>
        {
            var stream = new TrackingStream(payload);
            streams.Add(stream);
            return stream;
        }, _ => null, Resolve));
        Assert.Contains("total byte limit", error.Message);
        Assert.InRange(streams.Count, 1, 69);
        Assert.All(streams, stream => Assert.True(stream.Disposed));
    }

    [Fact]
    public void Character_limit_and_zip_uncompressed_byte_limit_are_separate_gates()
    {
        using var text = Bytes(new string(' ', BlockModelCompiler.MaximumJsonCharacters + 1));
        Assert.Contains("character limit", Assert.Throws<InvalidDataException>(() =>
            BlockModelPackLoader.Build([Child], _ => text, _ => null, Resolve)).Message);
        Assert.True(text.Disposed);
        using var zip = new TestZip((Child.ModelPath, new string(' ', BlockModelPackLoader.MaximumModelBytes + 1)));
        Assert.Contains("ZIP entry exceeds byte limit", Assert.Throws<InvalidDataException>(() =>
            BlockModelPackLoader.BuildFromZip(zip.Path, [Child], _ => null, Resolve)).Message);
    }

    [Fact]
    public void Read_failure_disposes_the_stream_and_does_not_try_a_lower_priority_source()
    {
        using var stream = new BrokenStream();
        var fallbackCalled = false;
        var error = Assert.Throws<InvalidDataException>(() => BlockModelPackLoader.Build([Child], _ => stream,
            _ => { fallbackCalled = true; return null; }, Resolve));
        Assert.True(stream.Disposed);
        Assert.False(fallbackCalled);
        Assert.Contains("read failed", error.Message);
        Assert.Contains(Child.ModelPath, error.Message);
    }

    [Fact]
    public void Utf8_bom_is_accepted_and_missing_model_reports_both_identity_and_path()
    {
        var catalog = BlockModelPackLoader.Build([Child], _ => Bytes("\uFEFF{\"elements\":[]}"), _ => null, Resolve);
        Assert.Empty(catalog.Get(Child).Quads.ToArray());
        var error = Assert.Throws<InvalidDataException>(() => BlockModelPackLoader.Build([Child], _ => null, _ => null, Resolve));
        Assert.Contains("absent", error.Message);
        Assert.Contains(Child.ModelPath, error.Message);
    }

    private static int Resolve(RenderResourceId texture) => texture == RenderResourceId.Parse("example:block/stone")
        ? 7 : throw new KeyNotFoundException(texture.ToString());
    private static TrackingStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class BrokenStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("read failed");
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class TestZip : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omniblock-models-" + Guid.NewGuid() + ".zip");
        public TestZip(params (string Name, string Json)[] entries)
        {
            using var archive = ZipFile.Open(Path, ZipArchiveMode.Create);
            foreach (var (name, json) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(json);
            }
        }
        public void Dispose() => File.Delete(Path);
    }
}
