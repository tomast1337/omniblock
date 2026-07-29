using System.Linq;
using BetaSharp.Entities;
using BetaSharp.Registries;
using BetaSharp.Registries.Data;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Integration coverage for <see cref="EntityDefinitionJsonLoader"/> against real files in a temp
/// directory: defaults merging, datapack layering, and loud failure on a protocol id that would not
/// survive the wire. <c>ItemDefinitionJsonLoader</c> has no equivalent coverage.
/// </summary>
[Collection("RegistryAccess")]
public sealed class EntityDefinitionLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public EntityDefinitionLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private void WriteAsset(string name, string json)
    {
        string dir = Path.Combine(_tempDir, "assets", "entity");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"), json);
    }

    private void WriteDatapackAsset(string packName, string ns, string name, string json)
    {
        string dir = Path.Combine(_tempDir, "datapacks", packName, "data", ns, "entity");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"), json);
    }

    private EntityDefinitionJsonLoader Load(LoadLocations locations = LoadLocations.Assets)
    {
        // LoadPacksFrom appends "datapacks" itself, so the pack root is _tempDir, not _tempDir/datapacks.
        EntityDefinitionJsonLoader loader = new("entity", locations);
        loader.LoadFromPaths(_tempDir, _tempDir, null);
        return loader;
    }

    private static EntityDefinition? Get(EntityDefinitionJsonLoader loader, string name) =>
        loader.Get(new ResourceLocation(Namespace.BetaSharp, name))?.Value;

    [Fact]
    public void Loads_a_definition_and_names_it_from_the_filename()
    {
        WriteAsset("zombie", """{"ProtocolId": 54, "Health": 20, "Texture": "/mob/zombie.png"}""");

        EntityDefinition definition = Assert.IsType<EntityDefinition>(Get(Load(), "zombie"));

        Assert.Equal("zombie", definition.Name);
        Assert.Equal(54, definition.ProtocolId);
        Assert.Equal(20, definition.Health);
        Assert.Equal("/mob/zombie.png", definition.Texture);
    }

    [Fact]
    public void Unspecified_fields_fall_back_to_defaults_file()
    {
        WriteAsset("_defaults", """{"Health": 7, "SoundVolume": 0.25, "TalkInterval": 42}""");
        WriteAsset("cow", """{"ProtocolId": 92, "Health": 10}""");

        EntityDefinition definition = Assert.IsType<EntityDefinition>(Get(Load(), "cow"));

        Assert.Equal(10, definition.Health);        // file wins over defaults
        Assert.Equal(0.25f, definition.SoundVolume); // defaults fill the gap
        Assert.Equal(42, definition.TalkInterval);
    }

    [Fact]
    public void Lookup_by_protocol_id_resolves_the_same_instance()
    {
        WriteAsset("pig", """{"ProtocolId": 90}""");
        EntityDefinitionJsonLoader loader = Load();

        Assert.Same(Get(loader, "pig"), loader.Get(90));
        Assert.True(loader.ContainsId(90));
        Assert.Equal(90, loader.GetId(loader.Get(90)!));
    }

    [Theory]
    [InlineData(0)]     // vanilla never uses 0
    [InlineData(128)]   // would truncate to -128 on the wire
    [InlineData(9000)]
    [InlineData(-5)]
    public void Out_of_range_protocol_id_fails_loudly(int protocolId)
    {
        WriteAsset("broken", $$"""{"ProtocolId": {{protocolId}}}""");

        EntityDefinitionJsonLoader loader = Load();

        Assert.True(loader.HasErrors);
        Assert.Contains("broken", loader.FirstErrorMessage);
        Assert.Null(Get(loader, "broken"));
    }

    [Fact]
    public void Missing_protocol_id_fails_loudly_rather_than_defaulting()
    {
        WriteAsset("nameless", """{"Health": 12}""");

        EntityDefinitionJsonLoader loader = Load();

        Assert.True(loader.HasErrors);
        Assert.Contains("missing a ProtocolId", loader.FirstErrorMessage);
    }

    [Fact]
    public void Malformed_json_reports_the_offending_file()
    {
        WriteAsset("bad_syntax", """{"ProtocolId": 54,""");

        EntityDefinitionJsonLoader loader = Load();

        Assert.True(loader.HasErrors);
        Assert.Contains("bad_syntax", loader.FirstErrorMessage);
    }

    [Fact]
    public void Datapack_layer_overrides_the_base_asset()
    {
        WriteAsset("wolf", """{"ProtocolId": 95, "Health": 8}""");
        WriteDatapackAsset("buffed", "betasharp", "wolf", """{"ProtocolId": 95, "Health": 40}""");

        EntityDefinition definition = Assert.IsType<EntityDefinition>(
            Get(Load(LoadLocations.AllInit), "wolf"));

        Assert.Equal(40, definition.Health);
    }

    [Fact]
    public void Every_shipped_mob_asset_loads_without_errors()
    {
        // Guards the real assets/entity/*.json, not a fixture: a hand-edit that breaks one of the
        // 14 shipped files should fail here rather than at server boot.
        Assert.All(EntityRegistryDefinitionTests.MobTypes, type =>
        {
            EntityDefinition definition = type.RequireDefinition();
            Assert.InRange(definition.ProtocolId, 1, sbyte.MaxValue);
            Assert.False(string.IsNullOrWhiteSpace(definition.Texture));
            Assert.Equal(type.Id.ToLowerInvariant(), definition.Name);
        });
    }
}
