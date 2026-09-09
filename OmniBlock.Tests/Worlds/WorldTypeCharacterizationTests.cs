using OmniBlock.NBT;
using OmniBlock.Worlds;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Generation;

namespace OmniBlock.Tests.Worlds;

public sealed class WorldTypeCharacterizationTests
{
    public static TheoryData<WorldType, string, string, ResourceLocation> ShippedTypes => new()
    {
        { WorldType.Default, "default", "/gui/world_types/default.png", BuiltInWorldGeneratorProviders.Overworld },
        { WorldType.Flat, "flat", "/gui/world_types/flat.png", BuiltInWorldGeneratorProviders.Flat },
        { WorldType.Sky, "sky", "/gui/world_types/sky.png", BuiltInWorldGeneratorProviders.Sky }
    };

    [Theory]
    [MemberData(nameof(ShippedTypes))]
    public void Shipped_world_type_catalog_is_stable(
        WorldType type,
        string name,
        string iconPath,
        ResourceLocation generatorProvider)
    {
        Assert.Equal(name, type.Name);
        Assert.Equal(iconPath, type.IconPath);
        Assert.Equal(generatorProvider, type.GeneratorProviderType);
        Assert.True(type.CanBeCreated);
        Assert.Equal($"generator.{name}", type.GetTranslateName());
    }

    [Theory]
    [InlineData("default", "default")]
    [InlineData("DEFAULT", "default")]
    [InlineData("Flat", "flat")]
    [InlineData("sky", "sky")]
    [InlineData("missing:generator", "default")]
    public void Saved_names_are_resolved_case_insensitively_with_a_default_fallback(
        string input,
        string expected)
    {
        Assert.Equal(expected, WorldType.ParseWorldType(input).Name);
    }

    [Theory]
    [MemberData(nameof(ShippedTypes))]
    public void World_type_and_provider_options_survive_the_level_nbt_round_trip(
        WorldType type,
        string name,
        string iconPath,
        ResourceLocation generatorProvider)
    {
        _ = iconPath;
        _ = generatorProvider;
        var original = new WorldProperties(
            new WorldSettings(123456789L, type, "provider-specific-options"),
            "characterization");

        var loaded = new WorldProperties(original.getNBTTagCompound());

        Assert.Equal(123456789L, loaded.RandomSeed);
        Assert.Equal("characterization", loaded.LevelName);
        Assert.Equal(name, loaded.TerrainType.Name);
        Assert.Equal("provider-specific-options", loaded.GeneratorOptions);
    }

    [Fact]
    public void Legacy_level_without_a_generator_name_uses_the_default_type()
    {
        NBTTagCompound nbt = new();
        nbt.SetLong("RandomSeed", 42L);
        nbt.SetString("LevelName", "legacy");

        var loaded = new WorldProperties(nbt);

        Assert.Same(WorldType.Default, loaded.TerrainType);
        Assert.Equal(string.Empty, loaded.GeneratorOptions);
    }
}
