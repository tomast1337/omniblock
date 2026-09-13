using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Items;
using OmniBlock.Processes;

namespace OmniBlock.Tests.Catalog;

public sealed class ContentIdAllocationTests
{
    [Fact]
    public void Automatic_ids_are_deterministic_and_do_not_displace_explicit_ids()
    {
        var explicitBlock = Definition("base", 1);
        var zebra = Definition("zebra");
        var apple = Definition("apple");

        var forward = ContentIdAllocator.AssignBlockIds([explicitBlock, zebra, apple]);
        var reverse = ContentIdAllocator.AssignBlockIds([apple, zebra, explicitBlock]);

        Assert.Equal(0, Find(forward, "apple").ProtocolId);
        Assert.Equal(2, Find(forward, "zebra").ProtocolId);
        Assert.Equal(Find(forward, "apple").ProtocolId, Find(reverse, "apple").ProtocolId);
        Assert.Equal(Find(forward, "zebra").ProtocolId, Find(reverse, "zebra").ProtocolId);
    }

    [Fact]
    public void Saved_manifest_ids_take_priority_for_automatic_entries()
    {
        var saved = Manifest(("example:apple", 42));

        var assigned = ContentIdAllocator.AssignBlockIds([Definition("apple", ns: "example")], saved);

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
        var first = Manifest(("example:a", 4), ("example:b", 9));
        var reordered = Manifest(("example:b", 9), ("example:a", 4));
        var remapped = Manifest(("example:a", 5), ("example:b", 9));

        Assert.Equal(first.Fingerprint, reordered.Fingerprint);
        Assert.NotEqual(first.Fingerprint, remapped.Fingerprint);
    }

    [Fact]
    public void Compatibility_distinguishes_world_loading_from_network_sync()
    {
        var saved = Manifest(("base:stone", 1), ("mod:machine", 40));
        var missingMod = Manifest(("base:stone", 1));
        var withAdditionalMod = Manifest(
            ("base:stone", 1), ("mod:machine", 40), ("other:new_block", 41));

        var missing = missingMod.CompareTo(saved);
        var additional = withAdditionalMod.CompareTo(saved);

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
        Assert.Equal(TestItemCatalog.LoadDefinitions().Count, ContentRuntime.Current.Manifest.ItemIds.Count);
        Assert.Equal(160, ContentRuntime.Current.Manifest.Processes.Count);
        Assert.Equal(ContentRuntime.Current.EntityTypes.Count, ContentRuntime.Current.Manifest.Entities.Count);
    }

    [Fact]
    public void Ordinary_items_receive_deterministic_automatic_ids_without_displacing_explicit_ids()
    {
        var explicitItem = ItemDefinition("base", 256);
        var zebra = ItemDefinition("zebra");
        var apple = ItemDefinition("apple");

        var forward = ContentIdAllocator.AssignItemIds([explicitItem, zebra, apple]);
        var reverse = ContentIdAllocator.AssignItemIds(
            [ItemDefinition("apple"), ItemDefinition("zebra"), ItemDefinition("base", 256)]);

        Assert.Equal(257, forward.Single(item => item.Name == "apple").ProtocolId);
        Assert.Equal(258, forward.Single(item => item.Name == "zebra").ProtocolId);
        Assert.Equal(forward.Single(item => item.Name == "apple").ProtocolId,
            reverse.Single(item => item.Name == "apple").ProtocolId);
    }

    [Fact]
    public void Saved_item_ids_take_priority_and_round_trip_through_world_manifest_nbt()
    {
        var saved = Manifest([], [("example:wand", 900)]);
        var assigned = ContentIdAllocator.AssignItemIds([ItemDefinition("wand", ns: "example")], saved);
        var restored = ContentCatalogManifest.FromNbt(saved.ToNbt());

        Assert.Equal(900, Assert.Single(assigned).ProtocolId);
        Assert.Equal(900, restored.ItemIds[ResourceLocation.Parse("example:wand")]);
        Assert.Equal(saved.Fingerprint, restored.Fingerprint);
    }

    [Fact]
    public void Item_changes_affect_fingerprints_and_report_missing_mod_content()
    {
        var required = Manifest([], [("base:stick", 280), ("example:wand", 900)]);
        var missingMod = Manifest([], [("base:stick", 280)]);
        var remapped = Manifest([], [("base:stick", 281), ("example:wand", 900)]);

        var missing = missingMod.CompareTo(required);
        var changed = remapped.CompareTo(required);

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
        var first = JsonSerializer.Deserialize<ProcessDefinition>(
            """{"type":"example:crusher","energy":4000,"input":{"count":1,"item":"base:ore"}}""")!;
        var reordered = JsonSerializer.Deserialize<ProcessDefinition>(
            """{ "input": { "item":"base:ore", "count":1 }, "energy":4000, "type":"example:crusher" }""")!;
        var changed = JsonSerializer.Deserialize<ProcessDefinition>(
            """{"type":"example:crusher","energy":5000,"input":{"count":1,"item":"base:ore"}}""")!;

        Assert.Equal(first.ComputeCanonicalHash(), reordered.ComputeCanonicalHash());
        Assert.NotEqual(first.ComputeCanonicalHash(), changed.ComputeCanonicalHash());
    }

    [Fact]
    public void Process_compatibility_reports_missing_provider_and_changed_definition()
    {
        var required = ProcessManifest(
            ("example:crushing", "example:crusher", "hash-a"),
            ("base:smelting", "omniblock:smelting", "hash-b"));
        var actual = ProcessManifest(
            ("base:smelting", "omniblock:smelting", "hash-c"));

        var compatibility = actual.CompareTo(required);

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
        var withProcess = ProcessManifest(
            ("example:crushing", "example:crusher", "hash-a"));
        var changed = ProcessManifest(
            ("example:crushing", "example:crusher", "hash-b"));
        var restored = ContentCatalogManifest.FromNbt(withProcess.ToNbt());

        Assert.Equal(withProcess.Processes, restored.Processes);
        Assert.Equal(withProcess.Fingerprint, restored.Fingerprint);
        Assert.NotEqual(withProcess.Fingerprint, changed.Fingerprint);
    }

    [Fact]
    public void Entity_manifest_contains_identity_provider_definition_and_wire_mappings()
    {
        var zombie = ContentRuntime.Current.Manifest.Entities[
            ResourceLocation.Parse("omniblock:zombie")];
        var arrow = ContentRuntime.Current.Manifest.Entities[
            ResourceLocation.Parse("omniblock:arrow")];
        var lightning = ContentRuntime.Current.Manifest.Entities[
            ResourceLocation.Parse("omniblock:lightningbolt")];

        Assert.Equal(ResourceLocation.Parse("omniblock:creature"), zombie.ConstructorProviderType);
        Assert.Equal(54, zombie.ProtocolId);
        Assert.Equal(64, zombie.DefinitionHash.Length);
        Assert.Equal(60, arrow.ObjectSpawnId);
        Assert.Equal(1, lightning.GlobalSpawnId);
    }

    [Fact]
    public void Entity_manifest_round_trips_and_reports_missing_or_changed_content()
    {
        var required = EntityManifest(
            ("omniblock:zombie", "omniblock:creature", "hash-a", 54, null, null),
            ("example:drone", "example:machine", "hash-b", 90, 72, null));
        var missingMod = EntityManifest(
            ("omniblock:zombie", "omniblock:creature", "hash-a", 54, null, null));
        var remapped = EntityManifest(
            ("omniblock:zombie", "omniblock:creature", "hash-a", 55, null, null),
            ("example:drone", "example:machine", "hash-b", 90, 72, null));
        var restored = ContentCatalogManifest.FromNbt(required.ToNbt());

        var missing = missingMod.CompareTo(required);
        var changed = remapped.CompareTo(required);
        Assert.Equal(required.Entities, restored.Entities);
        Assert.Equal(required.Fingerprint, restored.Fingerprint);
        Assert.Contains(ResourceLocation.Parse("example:drone"), missing.MissingEntities);
        Assert.Contains(ResourceLocation.Parse("example:machine"), missing.MissingEntityConstructorProviders);
        Assert.Contains("required mods may be absent", missing.Diagnostic);
        Assert.Contains(changed.ChangedEntities, mismatch => mismatch.Key == "omniblock:zombie");
        Assert.True(changed.CanLoadWorld);
        Assert.False(changed.CanSynchronizeClient);
        Assert.False(missing.CanLoadWorld);
        Assert.False(missing.CanSynchronizeClient);
    }

    [Fact]
    public void Entity_definition_updates_can_load_saves_but_constructor_changes_cannot()
    {
        var saved = EntityManifest(
            ("omniblock:cow", "omniblock:creature", "old-definition", 92, null, null));
        var updatedDefaults = EntityManifest(
            ("omniblock:cow", "omniblock:creature", "new-definition", 92, null, null));
        var changedConstructor = EntityManifest(
            ("omniblock:cow", "example:custom_creature", "new-definition", 92, null, null));

        var compatible = updatedDefaults.CompareTo(saved);
        var incompatible = changedConstructor.CompareTo(saved);

        var definitionChange = Assert.Single(compatible.ChangedEntities);
        Assert.True(definitionChange.DefinitionChanged);
        Assert.False(definitionChange.ConstructorProviderChanged);
        Assert.True(compatible.CanLoadWorld);
        Assert.False(compatible.CanSynchronizeClient);

        var constructorChange = Assert.Single(incompatible.ChangedEntities);
        Assert.True(constructorChange.ConstructorProviderChanged);
        Assert.False(incompatible.CanLoadWorld);
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

    private static ContentCatalogManifest EntityManifest(
        params (string Key, string Constructor, string Hash, int Protocol, int? Object, int? Global)[] entities) =>
        new([], [], [], entities.Select(entry => new KeyValuePair<ResourceLocation, EntityCatalogEntry>(
            ResourceLocation.Parse(entry.Key), new EntityCatalogEntry(ResourceLocation.Parse(entry.Constructor), entry.Hash,
                entry.Protocol, entry.Object, entry.Global))));

    private static ItemDefinition ItemDefinition(string name, int id = -1, string ns = "omniblock") => new()
    {
        Name = name,
        Namespace = Namespace.Get(ns),
        ProtocolId = id
    };
}
