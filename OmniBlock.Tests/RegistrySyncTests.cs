using System.Collections;
using System.Net;
using System.Net.Sockets;
using OmniBlock.Client.Network;
using OmniBlock.Network.Messages;
using OmniBlock.Registries;
using OmniBlock.Registries.Data;

namespace OmniBlock.Tests;

// ---------------------------------------------------------------------------
// Helpers shared across all three test classes in this file
// ---------------------------------------------------------------------------

/// <summary>
/// Minimal in-memory implementation of <see cref="IReadableRegistry{T}"/> for testing.
/// Keys are built from the entry's <see cref="IDataAsset.Name"/> in the OmniBlock namespace.
/// </summary>
file sealed class StubRegistry<T>(RegistryKey<T> key, IEnumerable<T> items) : IReadableRegistry<T>
    where T : class, IDataAsset
{
    private readonly Dictionary<ResourceLocation, T> _entries =
        items.ToDictionary(item => new ResourceLocation(Namespace.OmniBlock, item.Name));

    public ResourceLocation RegistryKey => key.Location;

    public Holder<T>? Get(ResourceLocation location)
        => _entries.TryGetValue(location, out T? v) ? new Holder<T>(v) : null;

    public T? Get(int id) => null;

    public int GetId(T value) => -1;

    public ResourceLocation? GetKey(T value)
        => _entries.FirstOrDefault(kv => kv.Value == value).Key;

    public bool ContainsKey(ResourceLocation location) => _entries.ContainsKey(location);

    public IEnumerable<ResourceLocation> Keys => _entries.Keys;

    public IEnumerator<T> GetEnumerator() => _entries.Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// ---------------------------------------------------------------------------
// BuildSyncMessages / IsReloadable tests — mutate s_dynamicEntries
// ---------------------------------------------------------------------------

[Collection("RegistryAccess")]
public sealed class RegistrySyncTests : IDisposable
{
    private readonly string _tempDir;

    private static readonly RegistryKey<TestEnchantment> s_enchKey =
        new(ResourceLocation.Parse("test:enchantment"));

    public RegistrySyncTests()
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
            Directory.Delete(_tempDir, recursive: true);
    }

    private void WriteBaseEnchantment(string name, int maxLevel)
    {
        string dir = Path.Combine(_tempDir, "assets", "enchantment");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"),
            $"{{\"MaxLevel\":{maxLevel}}}");
    }

    [Fact]
    public void BuildSyncMessages_yields_message_for_reloadable_registry()
    {
        WriteBaseEnchantment("sharpness", maxLevel: 5);

        var def = new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment", isReloadable: true);
        RegistryAccess.AddDynamic(def);
        RegistryAccess ra = RegistryAccess.Build(basePath: _tempDir);

        List<RegistryDataMessage> messages = ra.BuildSyncMessages().ToList();
        Assert.Single(messages);
        Assert.Equal(s_enchKey.Location, messages[0].RegistryId);
    }

    [Fact]
    public void BuildSyncMessages_skips_non_reloadable_registry()
    {
        WriteBaseEnchantment("sharpness", maxLevel: 5);

        var def = new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment", isReloadable: false);
        RegistryAccess.AddDynamic(def);
        RegistryAccess ra = RegistryAccess.Build(basePath: _tempDir);

        List<RegistryDataMessage> messages = ra.BuildSyncMessages().ToList();
        Assert.Empty(messages);
    }

    [Fact]
    public void BuildSyncMessages_message_contains_entry_for_each_loaded_asset()
    {
        WriteBaseEnchantment("sharpness", maxLevel: 5);
        WriteBaseEnchantment("fortune", maxLevel: 3);

        var def = new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment", isReloadable: true);
        RegistryAccess.AddDynamic(def);
        RegistryAccess ra = RegistryAccess.Build(basePath: _tempDir);

        RegistryDataMessage message = ra.BuildSyncMessages().Single();
        IEnumerable<string> names = message.Entries.Select(e => e.Key.Path).OrderBy(n => n);
        Assert.Equal(["fortune", "sharpness"], names);
    }

    [Fact]
    public void BuildSyncMessages_only_reloadable_of_mixed_definitions_are_included()
    {
        WriteBaseEnchantment("sharpness", maxLevel: 5);

        var reloadable = new RegistryKey<TestEnchantment>(ResourceLocation.Parse("test:reloadable_ench"));
        var nonReloadable = new RegistryKey<TestEnchantment>(ResourceLocation.Parse("test:baked_ench"));

        RegistryAccess.AddDynamic(
            new RegistryDefinition<TestEnchantment>(reloadable, "enchantment", isReloadable: true));
        RegistryAccess.AddDynamic(
            new RegistryDefinition<TestEnchantment>(nonReloadable, "enchantment", isReloadable: false));

        RegistryAccess ra = RegistryAccess.Build(basePath: _tempDir);

        List<ResourceLocation> ids = ra.BuildSyncMessages().Select(p => p.RegistryId).ToList();
        Assert.Single(ids);
        Assert.Equal(reloadable.Location, ids[0]);
    }

    [Fact]
    public void Rebuild_reloadable_definition_produces_updated_sync_message()
    {
        WriteBaseEnchantment("sharpness", maxLevel: 5);

        var def = new RegistryDefinition<TestEnchantment>(s_enchKey, "enchantment", isReloadable: true);
        RegistryAccess.AddDynamic(def);
        RegistryAccess first = RegistryAccess.Build(basePath: _tempDir);

        WriteBaseEnchantment("fortune", maxLevel: 3);
        RegistryAccess rebuilt = first.Rebuild();

        RegistryDataMessage message = rebuilt.BuildSyncMessages().Single();
        IEnumerable<string> names = message.Entries.Select(e => e.Key.Path).OrderBy(n => n);
        Assert.Equal(["fortune", "sharpness"], names);
    }
}

// ---------------------------------------------------------------------------
// Message serialization tests — no global state mutations
// ---------------------------------------------------------------------------

public sealed class PacketSerializationTests
{
    /// <summary>
    /// Creates a connected loopback stream pair for packet round-trip tests.
    /// </summary>
    private static (NetworkStream write, NetworkStream read, Action cleanup) MakeLoopbackPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        client.Connect((IPEndPoint)listener.LocalEndpoint);
        TcpClient server = listener.AcceptTcpClient();
        listener.Stop();
        return (client.GetStream(), server.GetStream(),
            () => { client.Dispose(); server.Dispose(); }
        );
    }

    // ---- RegistryDataMessage ----

    [Fact]
    public void RegistryDataMessage_FromRegistry_sets_registry_id()
    {
        var mode = new GameMode { Name = "survival" };
        var registry = new StubRegistry<GameMode>(RegistryKeys.GameModes, [mode]);

        RegistryDataMessage message = RegistryDataMessage.FromRegistry(RegistryKeys.GameModes, registry);

        Assert.Equal(RegistryKeys.GameModes.Location, message.RegistryId);
    }

    [Fact]
    public void RegistryDataMessage_FromRegistry_serializes_each_entry_to_json()
    {
        var survival = new GameMode { Name = "survival" };
        var creative = new GameMode { Name = "creative" };
        var registry = new StubRegistry<GameMode>(RegistryKeys.GameModes, [survival, creative]);

        RegistryDataMessage message = RegistryDataMessage.FromRegistry(RegistryKeys.GameModes, registry);

        Assert.Equal(2, message.Entries.Count);
        Assert.All(message.Entries, e => Assert.NotNull(e.JsonData));
    }

    [Fact]
    public void RegistryDataMessage_WhenWritingNull_preserves_boolean_false_values()
    {
        // GameMode properties all default to true; creative sets several to false.
        var creative = new GameMode
        {
            Name = "creative",
            DisallowFlying = false,
            FiniteResources = false,
            BlockDrops = false,
            CanBeTargeted = false,
            CanReceiveDamage = false,
            NeedsAir = false,
            BreakSpeed = 0f
        };
        var registry = new StubRegistry<GameMode>(RegistryKeys.GameModes, [creative]);

        RegistryDataMessage message = RegistryDataMessage.FromRegistry(RegistryKeys.GameModes, registry);

        string json = message.Entries.Single().JsonData!;
        Assert.Contains("\"DisallowFlying\":false", json);
        Assert.Contains("\"FiniteResources\":false", json);
        Assert.Contains("\"BlockDrops\":false", json);
        Assert.Contains("\"CanBeTargeted\":false", json);
        Assert.Contains("\"CanReceiveDamage\":false", json);
        Assert.Contains("\"NeedsAir\":false", json);
        Assert.Contains("\"BreakSpeed\":0", json);
    }

    [Fact]
    public void RegistryDataMessage_roundtrip_preserves_registry_id_and_entries()
    {
        var enchantment = new TestEnchantment { Name = "sharpness", MaxLevel = 5, Rarity = "common" };
        var key = new RegistryKey<TestEnchantment>(ResourceLocation.Parse("test:enchantment"));
        var registry = new StubRegistry<TestEnchantment>(key, [enchantment]);

        RegistryDataMessage sent = RegistryDataMessage.FromRegistry(key, registry);

        (NetworkStream? writeStream, NetworkStream? readStream, Action? cleanup) = MakeLoopbackPair();
        try
        {
            sent.Write(writeStream);

            var received = new RegistryDataMessage();
            received.Read(readStream);

            Assert.Equal(key.Location, received.RegistryId);
            Assert.Single(received.Entries);
            Assert.Equal("sharpness", received.Entries[0].Key.Path);
            Assert.NotNull(received.Entries[0].JsonData);
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public void RegistryDataMessage_roundtrip_null_json_entry_reads_back_as_null()
    {
        // Construct a message directly via round-trip: pack a null entry by sending
        // known bytes over a loopback stream.
        (NetworkStream? writeStream, NetworkStream? readStream, Action? cleanup) = MakeLoopbackPair();
        try
        {
            // Manually write the wire format: registryId, count=1 (varint), name, hasData=false
            writeStream.WriteResourceLocation("test:enchantment");
            writeStream.WriteVarInt(1);
            writeStream.WriteResourceLocation("test:silk_touch");
            writeStream.WriteBoolean(false); // no JSON data

            var received = new RegistryDataMessage();
            received.Read(readStream);

            Assert.Equal("test:enchantment", received.RegistryId);
            Assert.Single(received.Entries);
            Assert.Equal("test:silk_touch", received.Entries[0].Key);
            Assert.Null(received.Entries[0].JsonData);
        }
        finally
        {
            cleanup();
        }
    }

    // ---- PlayerGameModeUpdateMessage ----

    [Fact]
    public void PlayerGameModeUpdateMessage_roundtrip_preserves_name()
    {
        var sent = new PlayerGameModeUpdateMessage
        {
            GameModeNamespace = Namespace.OmniBlock.ToString(),
            GameModeName = "survival"
        };

        (NetworkStream? writeStream, NetworkStream? readStream, Action? cleanup) = MakeLoopbackPair();
        try
        {
            sent.Write(writeStream);

            var received = new PlayerGameModeUpdateMessage();
            received.Read(readStream);

            Assert.Equal("survival", received.GameModeName);
        }
        finally
        {
            cleanup();
        }
    }
}

// ---------------------------------------------------------------------------
// ClientRegistryAccess tests — no global state mutations
// ---------------------------------------------------------------------------

public sealed class ClientRegistryAccessTests
{
    private static readonly RegistryKey<GameMode> s_gameModeKey = RegistryKeys.GameModes;

    private static RegistryDataMessage BuildMessage(params GameMode[] modes)
    {
        var registry = new StubRegistry<GameMode>(s_gameModeKey, modes);
        return RegistryDataMessage.FromRegistry(s_gameModeKey, registry);
    }

    [Fact]
    public void Accumulate_and_GetAll_deserializes_all_entries()
    {
        var survival = new GameMode { Name = "survival" };
        var creative = new GameMode { Name = "creative" };

        var access = new ClientRegistryAccess();
        access.Accumulate(BuildMessage(survival, creative));

        IReadOnlyDictionary<ResourceLocation, Holder<GameMode>> all = access.GetAll(s_gameModeKey);
        Assert.Equal(2, all.Count);
        Assert.True(all.ContainsKey("survival"));
        Assert.True(all.ContainsKey("creative"));
    }

    [Fact]
    public void Get_returns_correct_entry_by_name()
    {
        var survival = new GameMode { Name = "survival" };                    // BreakSpeed = 1f
        var creative = new GameMode { Name = "creative", BreakSpeed = 0.5f }; // distinct value

        var access = new ClientRegistryAccess();
        access.Accumulate(BuildMessage(survival, creative));

        GameMode? found = access.Get(s_gameModeKey, "creative")?.Value;
        Assert.NotNull(found);
        Assert.Equal(0.5f, found.BreakSpeed);
    }

    [Fact]
    public void Get_returns_null_for_unknown_name()
    {
        var access = new ClientRegistryAccess();
        access.Accumulate(BuildMessage(new GameMode { Name = "survival" }));

        Assert.Null(access.Get(s_gameModeKey, "spectator"));
    }

    [Fact]
    public void GetAll_for_unknown_registry_returns_empty_dictionary()
    {
        var access = new ClientRegistryAccess();
        // Accumulate nothing for this registry.

        IReadOnlyDictionary<ResourceLocation, Holder<GameMode>> all = access.GetAll(s_gameModeKey);
        Assert.Empty(all);
    }

    [Fact]
    public void Re_accumulate_invalidates_cache_and_reflects_new_data()
    {
        var initial = new GameMode { Name = "survival", BreakSpeed = 1f };
        var access = new ClientRegistryAccess();
        access.Accumulate(BuildMessage(initial));

        // Force the cache to populate.
        GameMode? first = access.Get(s_gameModeKey, "survival")?.Value;
        Assert.NotNull(first);
        Assert.Equal(1f, first.BreakSpeed);

        // Re-accumulate with changed data.
        var updated = new GameMode { Name = "survival", BreakSpeed = 0.5f };
        access.Accumulate(BuildMessage(updated));

        GameMode? second = access.Get(s_gameModeKey, "survival")?.Value;
        Assert.NotNull(second);
        Assert.Equal(0.5f, second.BreakSpeed);
    }

    [Fact]
    public void Boolean_false_values_survive_full_accumulate_and_get_pipeline()
    {
        // This is the regression test for the WhenWritingNull vs WhenWritingDefault bug:
        // boolean false and float 0 must not be stripped during JSON serialization
        // and must therefore deserialize to false/0 on the client side.
        var creative = new GameMode
        {
            Name = "creative",
            DisallowFlying = false,
            FiniteResources = false,
            BlockDrops = false,
            CanBeTargeted = false,
            CanReceiveDamage = false,
            NeedsAir = false,
            BreakSpeed = 0f
        };

        var access = new ClientRegistryAccess();
        access.Accumulate(BuildMessage(creative));

        GameMode? result = access.Get(s_gameModeKey, "creative")?.Value;
        Assert.NotNull(result);
        Assert.False(result.DisallowFlying);
        Assert.False(result.FiniteResources);
        Assert.False(result.BlockDrops);
        Assert.False(result.CanBeTargeted);
        Assert.False(result.CanReceiveDamage);
        Assert.False(result.NeedsAir);
        Assert.Equal(0f, result.BreakSpeed);
    }

    [Fact]
    public void Re_accumulate_without_entry_invalidates_its_holder()
    {
        var survival = new GameMode { Name = "survival" };
        var creative = new GameMode { Name = "creative" };
        var access = new ClientRegistryAccess();
        access.Accumulate(BuildMessage(survival, creative));

        // Retain a holder reference before the resync.
        Holder<GameMode>? holder = access.Get(s_gameModeKey, "creative");
        Assert.NotNull(holder);
        Assert.False(holder.IsInvalid);

        // Resync drops "creative" entirely.
        access.Accumulate(BuildMessage(survival));
        access.GetAll(s_gameModeKey); // trigger the merge

        Assert.True(holder.IsInvalid);
        Assert.Throws<InvalidOperationException>(() => _ = holder.Value);
    }

    [Fact]
    public void Deserialized_entry_has_Name_set_to_the_registry_entry_name()
    {
        var mode = new GameMode { Name = "survival" };
        var access = new ClientRegistryAccess();
        access.Accumulate(BuildMessage(mode));

        GameMode? result = access.Get(s_gameModeKey, "survival")?.Value;
        Assert.NotNull(result);
        Assert.Equal("survival", result.Name);
    }
}
