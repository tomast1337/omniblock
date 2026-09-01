using OmniBlock.Blocks;
using OmniBlock.Registries;

namespace OmniBlock.Tests.Catalog;

public sealed class ContentIdAllocationTests
{
    [Fact]
    public void Automatic_ids_are_deterministic_and_do_not_displace_explicit_ids()
    {
        BlockDefinition explicitBlock = Definition("base", 1);
        BlockDefinition zebra = Definition("zebra");
        BlockDefinition apple = Definition("apple");

        List<BlockDefinition> forward = ContentIdAllocator.AssignBlockIds([explicitBlock, zebra, apple]);
        List<BlockDefinition> reverse = ContentIdAllocator.AssignBlockIds([apple, zebra, explicitBlock]);

        Assert.Equal(0, Find(forward, "apple").ProtocolId);
        Assert.Equal(2, Find(forward, "zebra").ProtocolId);
        Assert.Equal(Find(forward, "apple").ProtocolId, Find(reverse, "apple").ProtocolId);
        Assert.Equal(Find(forward, "zebra").ProtocolId, Find(reverse, "zebra").ProtocolId);
    }

    [Fact]
    public void Saved_manifest_ids_take_priority_for_automatic_entries()
    {
        ContentCatalogManifest saved = Manifest(("example:apple", 42));

        List<BlockDefinition> assigned = ContentIdAllocator.AssignBlockIds([Definition("apple", ns: "example")], saved);

        Assert.Equal(42, Assert.Single(assigned).ProtocolId);
    }

    [Fact]
    public void Duplicate_names_and_explicit_ids_fail_loudly()
    {
        Assert.Throws<InvalidOperationException>(() => ContentIdAllocator.AssignBlockIds(
            [Definition("same"), Definition("same")]));
        Assert.Throws<InvalidOperationException>(() => ContentIdAllocator.AssignBlockIds(
            [Definition("first", 7), Definition("second", 7)]));
    }

    [Fact]
    public void Fingerprints_are_order_independent_and_change_when_ids_change()
    {
        ContentCatalogManifest first = Manifest(("example:a", 4), ("example:b", 9));
        ContentCatalogManifest reordered = Manifest(("example:b", 9), ("example:a", 4));
        ContentCatalogManifest remapped = Manifest(("example:a", 5), ("example:b", 9));

        Assert.Equal(first.Fingerprint, reordered.Fingerprint);
        Assert.NotEqual(first.Fingerprint, remapped.Fingerprint);
    }

    [Fact]
    public void Compatibility_distinguishes_world_loading_from_network_sync()
    {
        ContentCatalogManifest saved = Manifest(("base:stone", 1), ("mod:machine", 40));
        ContentCatalogManifest missingMod = Manifest(("base:stone", 1));
        ContentCatalogManifest withAdditionalMod = Manifest(
            ("base:stone", 1), ("mod:machine", 40), ("other:new_block", 41));

        CatalogCompatibility missing = missingMod.CompareTo(saved);
        CatalogCompatibility additional = withAdditionalMod.CompareTo(saved);

        Assert.False(missing.CanLoadWorld);
        Assert.Contains(ResourceLocation.Parse("mod:machine"), missing.MissingEntries);
        Assert.True(additional.CanLoadWorld);
        Assert.False(additional.CanSynchronizeClient);
    }

    [Fact]
    public void Published_runtime_exposes_its_catalog_fingerprint()
    {
        Assert.Equal(64, ContentRuntime.Current.Manifest.Fingerprint.Length);
        Assert.Equal(ContentRuntime.Current.Blocks.Count, ContentRuntime.Current.Manifest.BlockIds.Count);
    }

    private static BlockDefinition Definition(string name, int id = -1, string ns = "omniblock") => new()
    {
        Name = name,
        Namespace = Namespace.Get(ns),
        ProtocolId = id
    };

    private static BlockDefinition Find(IEnumerable<BlockDefinition> definitions, string name) =>
        Assert.Single(definitions, definition => definition.Name == name);

    private static ContentCatalogManifest Manifest(params (string Key, int Id)[] entries) =>
        new(entries.Select(entry =>
            new KeyValuePair<ResourceLocation, int>(ResourceLocation.Parse(entry.Key), entry.Id)));
}
