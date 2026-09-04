using OmniBlock.Registries.Data;

namespace OmniBlock.Tests;

/// <summary>
///     A custom data-driven type modelling a Minecraft enchantment — used to verify that
///     the registry infrastructure works for any <see cref="IDataAsset" /> type, not just
///     the built-in <see cref="OmniBlock.GameMode.GameMode" />.
/// </summary>
public class TestEnchantment : DataAsset
{
    /// <summary>Maximum level the enchantment can reach (e.g. Sharpness V → 5).</summary>
    public int MaxLevel { get; set; } = 1;

    /// <summary>Whether the enchantment can appear on books.</summary>
    public bool AllowedOnBooks { get; set; } = true;

    /// <summary>The rarity group, e.g. "common", "uncommon", "rare", "very_rare".</summary>
    public string Rarity { get; set; } = "common";
}

// Tests that modify RegistryAccess.s_dynamicEntries must not run in parallel with each other.
[Collection("RegistryAccess")]
public class RegistryAccessTests : IDisposable
{
    // Well-known key for the test enchantment registry.
    private static readonly RegistryKey<TestEnchantment> s_enchKey =
        new(ResourceLocation.Parse("test:enchantment"));

    private readonly string _tempDir;

    public RegistryAccessTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);

        // Ensure a clean dynamic-entry list for each test.
        RegistryAccess.ClearDynamicEntries();
    }

    public void Dispose()
    {
        RegistryAccess.ClearDynamicEntries();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Writes a JSON enchantment file under base assets.</summary>
    private void WriteBaseEnchantment(string name, int maxLevel, string rarity = "common", bool allowedOnBooks = true)
    {
        var dir = Path.Combine(_tempDir, "assets", "enchantment");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"),
            $"{{\"MaxLevel\":{maxLevel},\"AllowedOnBooks\":{allowedOnBooks.ToString().ToLower()},\"Rarity\":\"{rarity}\"}}");
    }

    /// <summary>Writes a JSON enchantment file inside a named datapack.</summary>
    private void WriteDatapackEnchantment(string packName, string ns, string name, int maxLevel, string rarity = "common")
    {
        var dir = Path.Combine(_tempDir, "datapacks", packName, "data", ns, "enchantment");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"),
            $"{{\"MaxLevel\":{maxLevel},\"Rarity\":\"{rarity}\"}}");
    }

    /// <summary>Writes a JSON enchantment file inside a world datapack.</summary>
    private void WriteWorldEnchantment(string packName, string ns, string name, int maxLevel)
    {
        var dir = Path.Combine(_tempDir, "world", "datapacks", packName, "data", ns, "enchantment");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"),
            $"{{\"MaxLevel\":{maxLevel}}}");
    }

    private RegistryDefinition<TestEnchantment> RegisterEnchantmentDefinition(
        LoadLocations locations = LoadLocations.AllData)
    {
        var def = new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment", locations);
        RegistryAccess.AddDynamic(def);
        return def;
    }

    // -------------------------------------------------------------------------
    // RegistryAccess.Empty
    // -------------------------------------------------------------------------

    [Fact]
    public void Empty_returns_null_for_unknown_key()
    {
        var reg = RegistryAccess.Empty.Get(s_enchKey);
        Assert.Null(reg);
    }

    [Fact]
    public void Empty_GetOrThrow_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RegistryAccess.Empty.GetOrThrow(s_enchKey));
    }

    // -------------------------------------------------------------------------
    // RegistryAccess.Build() — base assets
    // -------------------------------------------------------------------------

    [Fact]
    public void Build_discovers_registered_definitions_and_loads_base_assets()
    {
        WriteBaseEnchantment("sharpness", 5);
        WriteBaseEnchantment("silk_touch", 1, "very_rare");

        RegisterEnchantmentDefinition();
        var ra = RegistryAccess.Build(_tempDir);

        var reg = ra.GetOrThrow(s_enchKey);

        var sharpness = reg.GetValue(ResourceLocation.Parse("omniblock:sharpness"));
        var silkTouch = reg.GetValue(ResourceLocation.Parse("omniblock:silk_touch"));

        Assert.NotNull(sharpness);
        Assert.Equal(5, sharpness.MaxLevel);
        Assert.Equal("common", sharpness.Rarity);

        Assert.NotNull(silkTouch);
        Assert.Equal(1, silkTouch.MaxLevel);
        Assert.Equal("very_rare", silkTouch.Rarity);
    }

    [Fact]
    public void Build_with_no_definitions_returns_empty_registry_access()
    {
        // No AddDynamic calls.
        var ra = RegistryAccess.Build(_tempDir);
        Assert.Null(ra.Get(s_enchKey));
    }

    [Fact]
    public void Build_does_not_include_unregistered_definition_in_registry()
    {
        WriteBaseEnchantment("sharpness", 5);
        // Deliberately do NOT call RegisterEnchantmentDefinition.

        var ra = RegistryAccess.Build(_tempDir);

        Assert.Null(ra.Get(s_enchKey));
    }

    // -------------------------------------------------------------------------
    // RegistryAccess — IReadableRegistry<T> surface on DataAssetLoader<T>
    // -------------------------------------------------------------------------

    [Fact]
    public void Registry_Keys_contains_loaded_resource_locations()
    {
        WriteBaseEnchantment("sharpness", 5);
        WriteBaseEnchantment("fortune", 3);

        RegisterEnchantmentDefinition();
        var ra = RegistryAccess.Build(_tempDir);
        var reg = ra.GetOrThrow(s_enchKey);

        Assert.Contains(ResourceLocation.Parse("omniblock:sharpness"), reg.Keys);
        Assert.Contains(ResourceLocation.Parse("omniblock:fortune"), reg.Keys);
    }

    [Fact]
    public void Registry_ContainsKey_returns_true_for_loaded_entry()
    {
        WriteBaseEnchantment("sharpness", 5);

        RegisterEnchantmentDefinition();
        var ra = RegistryAccess.Build(_tempDir);
        var reg = ra.GetOrThrow(s_enchKey);

        Assert.True(reg.ContainsKey(ResourceLocation.Parse("omniblock:sharpness")));
    }

    [Fact]
    public void Registry_GetHolder_returns_holder_for_loaded_entry()
    {
        WriteBaseEnchantment("sharpness", 5);

        RegisterEnchantmentDefinition(LoadLocations.Assets);
        var ra = RegistryAccess.Build(_tempDir);
        var reg = ra.GetOrThrow(s_enchKey);

        var holder = reg.Get(ResourceLocation.Parse("omniblock:sharpness"));

        Assert.NotNull(holder);
        Assert.Equal(5, holder.Value.MaxLevel);
    }

    [Fact]
    public void Registry_enumerates_resolved_entries()
    {
        WriteBaseEnchantment("sharpness", 5);
        WriteBaseEnchantment("fortune", 3);

        RegisterEnchantmentDefinition(LoadLocations.Assets);
        var ra = RegistryAccess.Build(_tempDir);
        var reg = ra.GetOrThrow(s_enchKey);

        var names = reg.Select(e => e.Name).OrderBy(n => n).ToList();
        Assert.Equal(["fortune", "sharpness"], names);
    }

    // -------------------------------------------------------------------------
    // RegistryAccess — global datapack layering
    // -------------------------------------------------------------------------

    [Fact]
    public void Build_with_datapack_path_loads_datapack_entries()
    {
        WriteDatapackEnchantment("mypack", "omniblock", "looting", 3);

        RegisterEnchantmentDefinition();
        var ra = RegistryAccess.Build(_tempDir, _tempDir);

        var reg = ra.GetOrThrow(s_enchKey);
        var looting = reg.GetValue(ResourceLocation.Parse("omniblock:looting"));

        Assert.NotNull(looting);
        Assert.Equal(3, looting.MaxLevel);
    }

    [Fact]
    public void Datapack_entry_overrides_base_asset_via_merge()
    {
        WriteBaseEnchantment("sharpness", 5);
        // Datapack bumps max level but leaves rarity alone (merge semantics).
        WriteDatapackEnchantment("mypack", "omniblock", "sharpness", 10);

        RegisterEnchantmentDefinition();
        var ra = RegistryAccess.Build(_tempDir, _tempDir);

        var reg = ra.GetOrThrow(s_enchKey);
        var sharpness = reg.GetValue(ResourceLocation.Parse("omniblock:sharpness"));

        Assert.NotNull(sharpness);
        Assert.Equal(10, sharpness.MaxLevel);
        // Rarity should survive the merge since the datapack did not specify it.
        Assert.Equal("common", sharpness.Rarity);
    }

    // -------------------------------------------------------------------------
    // RegistryAccess.WithWorldDatapacks / WithoutWorldDatapacks
    // -------------------------------------------------------------------------

    [Fact]
    public void WithWorldDatapacks_adds_world_only_entries_to_active_registry()
    {
        WriteBaseEnchantment("sharpness", 5);
        WriteWorldEnchantment("worldpack", "omniblock", "mending", 1);

        RegisterEnchantmentDefinition();
        var serverRa = RegistryAccess.Build(_tempDir);
        var worldRa = serverRa.WithWorldDatapacks(Path.Combine(_tempDir, "world"));

        var worldReg = worldRa.GetOrThrow(s_enchKey);
        Assert.NotNull(worldReg.Get(ResourceLocation.Parse("omniblock:mending")));
    }

    [Fact]
    public void WithWorldDatapacks_does_not_pollute_server_level_registry()
    {
        WriteBaseEnchantment("sharpness", 5);
        WriteWorldEnchantment("worldpack", "omniblock", "mending", 1);

        RegisterEnchantmentDefinition();
        var serverRa = RegistryAccess.Build(_tempDir);
        _ = serverRa.WithWorldDatapacks(Path.Combine(_tempDir, "world"));

        // The original serverRa must not contain the world-only enchantment.
        var serverReg = serverRa.GetOrThrow(s_enchKey);
        Assert.Null(serverReg.Get(ResourceLocation.Parse("omniblock:mending")));
    }

    [Fact]
    public void WithWorldDatapacks_overrides_base_entry_in_active_registry()
    {
        WriteBaseEnchantment("sharpness", 5);
        WriteWorldEnchantment("worldpack", "omniblock", "sharpness", 8);

        RegisterEnchantmentDefinition();
        var serverRa = RegistryAccess.Build(_tempDir);
        var worldRa = serverRa.WithWorldDatapacks(Path.Combine(_tempDir, "world"));

        var worldReg = worldRa.GetOrThrow(s_enchKey);
        Assert.Equal(8, worldReg.GetValue(ResourceLocation.Parse("omniblock:sharpness"))!.MaxLevel);

        // Server-level is unchanged.
        var serverReg = serverRa.GetOrThrow(s_enchKey);
        Assert.Equal(5, serverReg.GetValue(ResourceLocation.Parse("omniblock:sharpness"))!.MaxLevel);
    }

    [Fact]
    public void WithoutWorldDatapacks_returns_server_level_state_without_disk_io()
    {
        WriteBaseEnchantment("sharpness", 5);
        WriteWorldEnchantment("worldpack", "omniblock", "mending", 1);

        RegisterEnchantmentDefinition();
        var serverRa = RegistryAccess.Build(_tempDir);
        var worldRa = serverRa.WithWorldDatapacks(Path.Combine(_tempDir, "world"));

        // Remove the world dir entirely to prove WithoutWorldDatapacks does no disk I/O.
        Directory.Delete(Path.Combine(_tempDir, "world"), true);

        var strippedRa = worldRa.WithoutWorldDatapacks();

        var reg = strippedRa.GetOrThrow(s_enchKey);
        Assert.NotNull(reg.Get(ResourceLocation.Parse("omniblock:sharpness")));
        Assert.Null(reg.Get(ResourceLocation.Parse("omniblock:mending")));
    }

    [Fact]
    public void Multiple_WithWorldDatapacks_calls_are_independent_clones()
    {
        WriteBaseEnchantment("sharpness", 5);

        var worldADir = Path.Combine(_tempDir, "worldA");
        var worldBDir = Path.Combine(_tempDir, "worldB");
        var packDataA = Path.Combine(worldADir, "datapacks", "pack", "data", "omniblock", "enchantment");
        var packDataB = Path.Combine(worldBDir, "datapacks", "pack", "data", "omniblock", "enchantment");
        Directory.CreateDirectory(packDataA);
        Directory.CreateDirectory(packDataB);
        File.WriteAllText(Path.Combine(packDataA, "mending.json"), "{\"MaxLevel\":1}");
        File.WriteAllText(Path.Combine(packDataB, "looting.json"), "{\"MaxLevel\":3}");

        RegisterEnchantmentDefinition();
        var serverRa = RegistryAccess.Build(_tempDir);
        var raA = serverRa.WithWorldDatapacks(worldADir);
        var raB = serverRa.WithWorldDatapacks(worldBDir);

        var regA = raA.GetOrThrow(s_enchKey);
        var regB = raB.GetOrThrow(s_enchKey);

        Assert.NotNull(regA.Get(ResourceLocation.Parse("omniblock:mending")));
        Assert.Null(regA.Get(ResourceLocation.Parse("omniblock:looting")));

        Assert.NotNull(regB.Get(ResourceLocation.Parse("omniblock:looting")));
        Assert.Null(regB.Get(ResourceLocation.Parse("omniblock:mending")));
    }

    // -------------------------------------------------------------------------
    // Reload — simulates the /reload pipeline: Build() → [WithWorldDatapacks()] → repeat
    // -------------------------------------------------------------------------

    [Fact]
    public void Rebuild_picks_up_new_file_added_after_first_build()
    {
        WriteBaseEnchantment("sharpness", 5);

        RegisterEnchantmentDefinition();
        var first = RegistryAccess.Build(_tempDir);
        Assert.Null(first.GetOrThrow(s_enchKey).Get(ResourceLocation.Parse("omniblock:fortune")));

        // Simulate a datapack author dropping a new file while the server is running.
        WriteBaseEnchantment("fortune", 3);

        var reloaded = RegistryAccess.Build(_tempDir);
        Assert.NotNull(reloaded.GetOrThrow(s_enchKey).Get(ResourceLocation.Parse("omniblock:fortune")));
    }

    [Fact]
    public void Rebuild_picks_up_changed_value_in_existing_file()
    {
        WriteBaseEnchantment("sharpness", 5);

        RegisterEnchantmentDefinition();
        var first = RegistryAccess.Build(_tempDir);
        Assert.Equal(5, first.GetOrThrow(s_enchKey).GetValue(ResourceLocation.Parse("omniblock:sharpness"))!.MaxLevel);

        // Author edits the file to bump max level.
        WriteBaseEnchantment("sharpness", 10);

        var reloaded = RegistryAccess.Build(_tempDir);
        Assert.Equal(10, reloaded.GetOrThrow(s_enchKey).GetValue(ResourceLocation.Parse("omniblock:sharpness"))!.MaxLevel);
    }

    [Fact]
    public void Rebuild_produces_independent_instance_old_instance_unaffected()
    {
        WriteBaseEnchantment("sharpness", 5);

        RegisterEnchantmentDefinition();
        var first = RegistryAccess.Build(_tempDir);

        WriteBaseEnchantment("sharpness", 10);
        var reloaded = RegistryAccess.Build(_tempDir);

        // Callers holding a reference to the old instance must not see the new value.
        Assert.Equal(5, first.GetOrThrow(s_enchKey).GetValue(ResourceLocation.Parse("omniblock:sharpness"))!.MaxLevel);
        Assert.Equal(10, reloaded.GetOrThrow(s_enchKey).GetValue(ResourceLocation.Parse("omniblock:sharpness"))!.MaxLevel);
    }

    [Fact]
    public void Rebuild_then_reapply_world_datapacks_reflects_changes_in_both_tiers()
    {
        WriteBaseEnchantment("sharpness", 5);
        WriteWorldEnchantment("worldpack", "omniblock", "mending", 1);
        var worldDir = Path.Combine(_tempDir, "world");

        RegisterEnchantmentDefinition();
        var serverV1 = RegistryAccess.Build(_tempDir);
        var worldV1 = serverV1.WithWorldDatapacks(worldDir);

        // Both tiers change between reloads.
        WriteBaseEnchantment("sharpness", 10);
        WriteWorldEnchantment("worldpack", "omniblock", "mending", 4);

        var serverV2 = RegistryAccess.Build(_tempDir);
        var worldV2 = serverV2.WithWorldDatapacks(worldDir);

        var reg = worldV2.GetOrThrow(s_enchKey);
        Assert.Equal(10, reg.GetValue(ResourceLocation.Parse("omniblock:sharpness"))!.MaxLevel);
        Assert.Equal(4, reg.GetValue(ResourceLocation.Parse("omniblock:mending"))!.MaxLevel);

        // Old world instance is unaffected.
        var oldReg = worldV1.GetOrThrow(s_enchKey);
        Assert.Equal(5, oldReg.GetValue(ResourceLocation.Parse("omniblock:sharpness"))!.MaxLevel);
        Assert.Equal(1, oldReg.GetValue(ResourceLocation.Parse("omniblock:mending"))!.MaxLevel);
    }

    [Fact]
    public void Rebuild_without_world_does_not_contain_previous_world_entries()
    {
        WriteBaseEnchantment("sharpness", 5);
        WriteWorldEnchantment("worldpack", "omniblock", "mending", 1);
        var worldDir = Path.Combine(_tempDir, "world");

        RegisterEnchantmentDefinition();
        var serverRa = RegistryAccess.Build(_tempDir);
        _ = serverRa.WithWorldDatapacks(worldDir);

        // Reload without re-applying the world layer — simulates a server reload
        // that happens to run before the world is re-attached.
        var reloaded = RegistryAccess.Build(_tempDir);

        Assert.Null(reloaded.GetOrThrow(s_enchKey).Get(ResourceLocation.Parse("omniblock:mending")));
    }

    [Fact]
    public void Datapack_removed_between_reloads_is_absent_after_rebuild()
    {
        WriteBaseEnchantment("sharpness", 5);
        WriteDatapackEnchantment("myaddon", "omniblock", "looting", 3);

        RegisterEnchantmentDefinition();
        var first = RegistryAccess.Build(_tempDir, _tempDir);
        Assert.NotNull(first.GetOrThrow(s_enchKey).Get(ResourceLocation.Parse("omniblock:looting")));

        // Operator removes the datapack between reloads.
        Directory.Delete(Path.Combine(_tempDir, "datapacks", "myaddon"), true);

        var reloaded = RegistryAccess.Build(_tempDir, _tempDir);
        Assert.Null(reloaded.GetOrThrow(s_enchKey).Get(ResourceLocation.Parse("omniblock:looting")));
    }

    // -------------------------------------------------------------------------
    // GetOrThrow
    // -------------------------------------------------------------------------

    [Fact]
    public void GetOrThrow_throws_for_registry_not_added_with_AddDynamic()
    {
        var ra = RegistryAccess.Build(_tempDir);

        Assert.Throws<InvalidOperationException>(() => ra.GetOrThrow(s_enchKey));
    }

    [Fact]
    public void GetOrThrow_returns_registry_when_definition_was_registered()
    {
        RegisterEnchantmentDefinition();
        var ra = RegistryAccess.Build(_tempDir);

        var reg = ra.GetOrThrow(s_enchKey);
        Assert.NotNull(reg);
    }
}
