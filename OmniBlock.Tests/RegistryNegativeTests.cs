namespace OmniBlock.Tests;

/// <summary>
///     Negative tests: verify that state is correctly ABSENT when it should be.
///     These guard against false positives — situations where a system appears to work
///     but is actually leaking data across boundaries it should not cross.
/// </summary>
[Collection("RegistryAccess")]
public class RegistryNegativeTests : IDisposable
{
    private static readonly RegistryKey<TestEnchantment> s_enchKey =
        new(ResourceLocation.Parse("test:enchantment"));

    private static readonly RegistryKey<TestEnchantment> s_enchKey2 =
        new(ResourceLocation.Parse("test:enchantment2"));

    private readonly string _tempDir;

    public RegistryNegativeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
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

    private void WriteEnch(string name, int maxLevel, string subDir = "enchantment")
    {
        var dir = Path.Combine(_tempDir, "assets", subDir);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"), $"{{\"MaxLevel\":{maxLevel}}}");
    }

    private void WriteDatapackEnch(string packDir, string ns, string name, int maxLevel, string subDir = "enchantment")
    {
        var dir = Path.Combine(packDir, "datapacks", "pack", "data", ns, subDir);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"), $"{{\"MaxLevel\":{maxLevel}}}");
    }

    private RegistryAccess Build(string? datapackPath = null) => RegistryAccess.Build(_tempDir, datapackPath);

    // =========================================================================
    // Registry isolation — two different registry keys must never share data
    // =========================================================================

    [Fact]
    public void Entry_in_one_registry_is_not_visible_in_a_different_registry_key()
    {
        WriteEnch("sharpness", 5);

        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey2, "enchantment2"));
        var ra = Build();

        // enchantment2 registry has no files → must be empty, not spill from enchantment.
        var reg2 = ra.GetOrThrow(s_enchKey2);
        Assert.Empty(reg2);
    }

    [Fact]
    public void Get_returns_null_not_throws_for_missing_entry()
    {
        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var ra = Build();

        var result = ra.GetOrThrow(s_enchKey).GetValue(ResourceLocation.Parse("omniblock:nonexistent"));

        Assert.Null(result);
    }

    [Fact]
    public void GetHolder_returns_null_for_missing_entry()
    {
        WriteEnch("sharpness", 5);
        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var ra = Build();

        var holder = ra.GetOrThrow(s_enchKey)
            .Get(ResourceLocation.Parse("omniblock:nonexistent"));

        Assert.Null(holder);
    }

    [Fact]
    public void ContainsKey_returns_false_for_absent_entry()
    {
        WriteEnch("sharpness", 5);
        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var ra = Build();

        Assert.False(ra.GetOrThrow(s_enchKey).ContainsKey(ResourceLocation.Parse("omniblock:looting")));
    }

    // =========================================================================
    // Namespace isolation — omniblock:x and minecraft:x are different keys
    // =========================================================================

    [Fact]
    public void Entry_is_not_visible_under_wrong_namespace()
    {
        WriteEnch("sharpness", 5);
        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var ra = Build();

        // "omniblock:sharpness" exists; "minecraft:sharpness" must not.
        var result = ra.GetOrThrow(s_enchKey)
            .GetValue(ResourceLocation.Parse("minecraft:sharpness"));

        Assert.Null(result);
    }

    [Fact]
    public void ContainsKey_returns_false_for_same_name_different_namespace()
    {
        WriteEnch("sharpness", 5);
        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var ra = Build();

        Assert.False(ra.GetOrThrow(s_enchKey).ContainsKey(ResourceLocation.Parse("minecraft:sharpness")));
    }

    // =========================================================================
    // World-layer boundaries — world entries must not leak to server level
    // =========================================================================

    [Fact]
    public void World_only_entry_is_absent_from_server_level_registry()
    {
        WriteEnch("sharpness", 5);
        WriteDatapackEnch(Path.Combine(_tempDir, "world"), "omniblock", "mending", 1);

        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var serverRa = Build();
        _ = serverRa.WithWorldDatapacks(Path.Combine(_tempDir, "world"));

        Assert.Null(serverRa.GetOrThrow(s_enchKey).Get(ResourceLocation.Parse("omniblock:mending")));
    }

    [Fact]
    public void WithoutWorldDatapacks_removes_world_only_entries()
    {
        WriteEnch("sharpness", 5);
        WriteDatapackEnch(Path.Combine(_tempDir, "world"), "omniblock", "mending", 1);

        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var serverRa = Build();
        var worldRa = serverRa.WithWorldDatapacks(Path.Combine(_tempDir, "world"));

        Assert.NotNull(worldRa.GetOrThrow(s_enchKey).Get(ResourceLocation.Parse("omniblock:mending")));

        var stripped = worldRa.WithoutWorldDatapacks();
        Assert.Null(stripped.GetOrThrow(s_enchKey).Get(ResourceLocation.Parse("omniblock:mending")));
    }

    [Fact]
    public void Rebuild_without_world_does_not_surface_previous_world_only_entry()
    {
        WriteEnch("sharpness", 5);
        WriteDatapackEnch(Path.Combine(_tempDir, "world"), "omniblock", "mending", 1);

        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var v1 = Build();
        _ = v1.WithWorldDatapacks(Path.Combine(_tempDir, "world"));

        // Reload without applying world layer — world entry must stay out.
        var v2 = Build();
        Assert.Null(v2.GetOrThrow(s_enchKey).Get(ResourceLocation.Parse("omniblock:mending")));
    }

    // =========================================================================
    // Snapshot isolation — stale instances must not reflect subsequent builds
    // =========================================================================

    [Fact]
    public void Stale_instance_does_not_see_entry_added_in_later_build()
    {
        WriteEnch("sharpness", 5);
        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var stale = Build();

        WriteEnch("fortune", 3);
        _ = Build();

        Assert.Null(stale.GetOrThrow(s_enchKey).Get(ResourceLocation.Parse("omniblock:fortune")));
    }

    [Fact]
    public void Stale_world_instance_does_not_see_entry_added_in_later_world_build()
    {
        WriteEnch("sharpness", 5);
        WriteDatapackEnch(Path.Combine(_tempDir, "world"), "omniblock", "mending", 1);
        var worldDir = Path.Combine(_tempDir, "world");

        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var staleWorld = Build().WithWorldDatapacks(worldDir);

        WriteDatapackEnch(worldDir, "omniblock", "looting", 3);
        _ = Build().WithWorldDatapacks(worldDir);

        Assert.Null(staleWorld.GetOrThrow(s_enchKey).Get(ResourceLocation.Parse("omniblock:looting")));
    }

    // =========================================================================
    // Disabled datapacks — packs ending in .disabled must be skipped entirely
    // =========================================================================

    [Fact]
    public void Disabled_datapack_entries_are_not_loaded()
    {
        WriteEnch("sharpness", 5);

        // Create a pack directory but mark it disabled.
        var disabledPack = Path.Combine(_tempDir, "datapacks", "myaddon.disabled", "data", "omniblock", "enchantment");
        Directory.CreateDirectory(disabledPack);
        File.WriteAllText(Path.Combine(disabledPack, "looting.json"), "{\"MaxLevel\":3}");

        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var ra = Build(_tempDir);

        Assert.Null(ra.GetOrThrow(s_enchKey).Get(ResourceLocation.Parse("omniblock:looting")));
    }

    [Fact]
    public void Disabled_world_datapack_entries_are_not_loaded()
    {
        WriteEnch("sharpness", 5);

        var worldDir = Path.Combine(_tempDir, "world");
        var disabledPack = Path.Combine(worldDir, "datapacks", "worldpack.disabled", "data", "omniblock", "enchantment");
        Directory.CreateDirectory(disabledPack);
        File.WriteAllText(Path.Combine(disabledPack, "mending.json"), "{\"MaxLevel\":1}");

        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var ra = Build().WithWorldDatapacks(worldDir);

        Assert.Null(ra.GetOrThrow(s_enchKey).Get(ResourceLocation.Parse("omniblock:mending")));
    }

    // =========================================================================
    // String lookup — TryGet is exact, TryGetByPrefix does not overmatch
    // =========================================================================

    [Fact]
    public void TryGet_exact_name_does_not_match_prefix()
    {
        WriteEnch("sharpness", 5);
        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var ra = Build();

        var found = ra.GetOrThrow(s_enchKey).AsAssetLoader().TryGet("sharp", out _);

        Assert.False(found);
    }

    [Fact]
    public void TryGetByPrefix_does_not_match_entry_with_different_prefix()
    {
        WriteEnch("sharpness", 5);
        WriteEnch("fortune", 3);
        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var ra = Build();

        // "fo" is a prefix of "fortune" but must not match "sharpness".
        var found = ra.GetOrThrow(s_enchKey).AsAssetLoader().TryGetByPrefix("fo", out var asset);

        Assert.True(found);
        Assert.Equal("fortune", asset!.Name);
    }

    // =========================================================================
    // Integer indexing — DataAssetLoader does not support id-based lookup
    // =========================================================================

    [Fact]
    public void Get_by_integer_id_always_returns_null_for_data_asset_loader()
    {
        WriteEnch("sharpness", 5);
        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var ra = Build();

        var reg = ra.GetOrThrow(s_enchKey);
        Assert.Null(reg.Get(0));
    }

    [Fact]
    public void GetId_always_returns_negative_one_for_data_asset_loader()
    {
        WriteEnch("sharpness", 5);
        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var ra = Build();

        var reg = ra.GetOrThrow(s_enchKey);
        var sharpness = reg.GetOrThrow(ResourceLocation.Parse("omniblock:sharpness"));

        Assert.Equal(-1, reg.GetId(sharpness));
    }

    // =========================================================================
    // Replace semantics — "Replace": true must discard all base fields
    // =========================================================================

    [Fact]
    public void Replace_true_in_datapack_discards_base_asset_fields()
    {
        // Base defines MaxLevel=5, AllowedOnBooks=false (non-default).
        var dir = Path.Combine(_tempDir, "assets", "enchantment");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "sharpness.json"),
            "{\"MaxLevel\":5,\"AllowedOnBooks\":false}");

        // Datapack fully replaces the entry — only MaxLevel=1 specified, Replace=true.
        var packDir = Path.Combine(_tempDir, "datapacks", "pack", "data", "omniblock", "enchantment");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "sharpness.json"),
            "{\"Replace\":true,\"MaxLevel\":1}");

        RegistryAccess.AddDynamic(new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment"));
        var ra = Build(_tempDir);

        var sharpness = ra.GetOrThrow(s_enchKey)
            .GetOrThrow(ResourceLocation.Parse("omniblock:sharpness"));

        Assert.Equal(1, sharpness.MaxLevel);
        // AllowedOnBooks reverts to the C# default (true) because Replace discards
        // the base value — it must NOT retain the false from the base asset.
        Assert.True(sharpness.AllowedOnBooks);
    }

    // =========================================================================
    // RegistryAccess.Get — unregistered key returns null, not throws
    // =========================================================================

    [Fact]
    public void Get_returns_null_not_throws_for_unregistered_key()
    {
        var ra = Build();

        var result = ra.Get(s_enchKey);

        Assert.Null(result);
    }

    [Fact]
    public void Get_returns_null_on_empty_registry_access() => Assert.Null(RegistryAccess.Empty.Get(s_enchKey));
}
