using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockCactusTests
{
    // Valid substrate (soil) and self (stem) are required constructor params
    // (JSON-configurable per variant, no built-in vanilla fallback). Construct a differently
    // configured instance directly (bypassing BlockRegistry) to prove the override actually
    // takes effect rather than silently defaulting.
    [Fact]
    public void CanPlaceAt_OnConfiguredSubstrate_ReturnsTrue()
    {
        FakeWorldContext world = new();
        Block customStem = BlockRegistry.Get("stone");
        Block customSoil = BlockRegistry.Get("gravel");
        world.ReaderWriter.SetInitial(0, 63, 0, customSoil.id);

        CactusBehavior behavior = new(customStem, customSoil, 3);

        Assert.True(behavior.CanPlaceAt(customStem, new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    [Fact]
    public void CanPlaceAt_OnConfiguredSelf_ReturnsTrue()
    {
        FakeWorldContext world = new();
        Block customStem = BlockRegistry.Get("stone");
        Block customSoil = BlockRegistry.Get("gravel");
        world.ReaderWriter.SetInitial(0, 63, 0, customStem.id);

        CactusBehavior behavior = new(customStem, customSoil, 3);

        Assert.True(behavior.CanPlaceAt(customStem, new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    [Fact]
    public void CanPlaceAt_OnVanillaSandWithCustomConfig_ReturnsFalse()
    {
        FakeWorldContext world = new();
        Block customStem = BlockRegistry.Get("stone");
        Block customSoil = BlockRegistry.Get("gravel");
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("sand").id);

        CactusBehavior behavior = new(customStem, customSoil, 3);

        Assert.False(behavior.CanPlaceAt(customStem, new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    [Fact]
    public void CanPlaceAt_SolidHorizontalNeighbor_ReturnsFalse()
    {
        FakeWorldContext world = new();
        Block cactus = BlockRegistry.Get("cactus");
        Block sand = BlockRegistry.Get("sand");
        world.ReaderWriter.SetInitial(0, 63, 0, sand.id);
        world.ReaderWriter.SetInitial(1, 64, 0, BlockRegistry.Get("stone").id);

        Assert.False(cactus.CanPlaceAt(new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    // No built-in default and no null fallback: an omitted or unknown "stem"/"soil" in JSON
    // must throw immediately (at BehaviorRegistry.Build, i.e. server boot).
    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"cactus","soil":"sand"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("cactus", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"cactus","stem":"cactus","soil":"not_a_real_block"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("cactus", json.RootElement));
    }

    // max_height is also a required, JSON-declared constructor param — no built-in vanilla
    // fallback. An omitted value must throw immediately at BehaviorRegistry.Build.
    [Fact]
    public void BehaviorRegistry_Build_MissingMaxHeight_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"cactus","stem":"cactus","soil":"sand"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("cactus", json.RootElement));
    }
}
