using OmniBlock.Blocks;
using OmniBlock.Items;
using OmniBlock.NBT;
using OmniBlock.Registries;
using OmniBlock.Processes;
using System.Text.Json;

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
        Assert.Equal(DefaultRegistries.Items.Count(), ContentRuntime.Current.Manifest.ItemIds.Count);
        Assert.Equal(160, ContentRuntime.Current.Manifest.Processes.Count);
    }

    [Fact]
    public void Ordinary_items_receive_deterministic_automatic_ids_without_displacing_explicit_ids()
    {
        ItemDefinition explicitItem = ItemDefinition("base", 256);
        ItemDefinition zebra = ItemDefinition("zebra");
        ItemDefinition apple = ItemDefinition("apple");

        List<ItemDefinition> forward = ContentIdAllocator.AssignItemIds([explicitItem, zebra, apple]);
        List<ItemDefinition> reverse = ContentIdAllocator.AssignItemIds(
            [ItemDefinition("apple"), ItemDefinition("zebra"), ItemDefinition("base", 256)]);

        Assert.Equal(257, forward.Single(item => item.Name == "apple").ProtocolId);
        Assert.Equal(258, forward.Single(item => item.Name == "zebra").ProtocolId);
        Assert.Equal(forward.Single(item => item.Name == "apple").ProtocolId,
            reverse.Single(item => item.Name == "apple").ProtocolId);
    }

    [Fact]
    public void Saved_item_ids_take_priority_and_round_trip_through_world_manifest_nbt()
    {
        ContentCatalogManifest saved = Manifest([], [("example:wand", 900)]);
        List<ItemDefinition> assigned = ContentIdAllocator.AssignItemIds([ItemDefinition("wand", ns: "example")], saved);
        ContentCatalogManifest restored = ContentCatalogManifest.FromNbt(saved.ToNbt());

        Assert.Equal(900, Assert.Single(assigned).ProtocolId);
        Assert.Equal(900, restored.ItemIds[ResourceLocation.Parse("example:wand")]);
        Assert.Equal(saved.Fingerprint, restored.Fingerprint);
    }

    [Fact]
    public void Item_changes_affect_fingerprints_and_report_missing_mod_content()
    {
        ContentCatalogManifest required = Manifest([], [("base:stick", 280), ("example:wand", 900)]);
        ContentCatalogManifest missingMod = Manifest([], [("base:stick", 280)]);
        ContentCatalogManifest remapped = Manifest([], [("base:stick", 281), ("example:wand", 900)]);

        CatalogCompatibility missing = missingMod.CompareTo(required);
        CatalogCompatibility changed = remapped.CompareTo(required);

        Assert.NotEqual(required.Fingerprint, missingMod.Fingerprint);
        Assert.Contains(ResourceLocation.Parse("example:wand"), missing.MissingItems);
        Assert.Contains("missing items: example:wand", missing.Diagnostic);
        Assert.False(missing.CanLoadWorld);
        Assert.False(missing.CanSynchronizeClient);
        Assert.Contains(changed.IdMismatches, mismatch => mismatch.Kind == CatalogEntryKind.Item);
    }

    [Fact]
    public void Process_definition_hash_is_canonical_across_whitespace_and_object_property_order()
    {
        ProcessDefinition first = JsonSerializer.Deserialize<ProcessDefinition>(
            """{"type":"example:crusher","energy":4000,"input":{"count":1,"item":"base:ore"}}""")!;
        ProcessDefinition reordered = JsonSerializer.Deserialize<ProcessDefinition>(
            """{ "input": { "item":"base:ore", "count":1 }, "energy":4000, "type":"example:crusher" }""")!;
        ProcessDefinition changed = JsonSerializer.Deserialize<ProcessDefinition>(
            """{"type":"example:crusher","energy":5000,"input":{"count":1,"item":"base:ore"}}""")!;

        Assert.Equal(first.ComputeCanonicalHash(), reordered.ComputeCanonicalHash());
        Assert.NotEqual(first.ComputeCanonicalHash(), changed.ComputeCanonicalHash());
    }

    [Fact]
    public void Process_compatibility_reports_missing_provider_and_changed_definition()
    {
        ContentCatalogManifest required = ProcessManifest(
            ("example:crushing", "example:crusher", "hash-a"),
            ("base:smelting", "omniblock:smelting", "hash-b"));
        ContentCatalogManifest actual = ProcessManifest(
            ("base:smelting", "omniblock:smelting", "hash-c"));

        CatalogCompatibility compatibility = actual.CompareTo(required);

        Assert.Contains(ResourceLocation.Parse("example:crushing"), compatibility.MissingProcesses);
        Assert.Contains(ResourceLocation.Parse("example:crusher"), compatibility.MissingProcessProviders);
        Assert.Contains(compatibility.ChangedProcesses, mismatch => mismatch.Key == "base:smelting");
        Assert.Contains("missing processes: example:crushing", compatibility.Diagnostic);
        Assert.Contains("required process providers: example:crusher", compatibility.Diagnostic);
        Assert.False(compatibility.CanLoadWorld);
        Assert.False(compatibility.CanSynchronizeClient);
    }

    [Fact]
    public void Process_manifest_round_trips_and_affects_catalog_fingerprint()
    {
        ContentCatalogManifest withProcess = ProcessManifest(
            ("example:crushing", "example:crusher", "hash-a"));
        ContentCatalogManifest changed = ProcessManifest(
            ("example:crushing", "example:crusher", "hash-b"));
        ContentCatalogManifest restored = ContentCatalogManifest.FromNbt(withProcess.ToNbt());

        Assert.Equal(withProcess.Processes, restored.Processes);
        Assert.Equal(withProcess.Fingerprint, restored.Fingerprint);
        Assert.NotEqual(withProcess.Fingerprint, changed.Fingerprint);
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

    private static ContentCatalogManifest Manifest((string Key, int Id)[] blocks, (string Key, int Id)[] items) =>
        new(blocks.Select(entry => new KeyValuePair<ResourceLocation, int>(ResourceLocation.Parse(entry.Key), entry.Id)),
            items.Select(entry => new KeyValuePair<ResourceLocation, int>(ResourceLocation.Parse(entry.Key), entry.Id)));

    private static ContentCatalogManifest ProcessManifest(
        params (string Key, string Type, string Hash)[] processes) => new([], [],
        processes.Select(entry => new KeyValuePair<ResourceLocation, ProcessCatalogEntry>(
            ResourceLocation.Parse(entry.Key),
            new ProcessCatalogEntry(ResourceLocation.Parse(entry.Type), entry.Hash))));

    private static ItemDefinition ItemDefinition(string name, int id = -1, string ns = "omniblock") => new()
    {
        Name = name,
        Namespace = Namespace.Get(ns),
        ProtocolId = id
    };
}
