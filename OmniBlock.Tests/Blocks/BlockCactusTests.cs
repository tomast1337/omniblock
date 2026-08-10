using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockCactusTests
{
    [Fact]
    public void CanPlaceAt_OnConfiguredSubstrate_ReturnsTrue()
    {
        FakeWorldContext world = new();
        Block customStem = BlockRegistry.Get("stone");
        Block customSoil = BlockRegistry.Get("gravel");
        world.ReaderWriter.SetInitial(0, 63, 0, customSoil.Id);

        CactusBehavior behavior = new(customStem, customSoil, 3, top: 0, side: 0, bottom: 0);

        Assert.True(behavior.CanPlaceAt(customStem, new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    [Fact]
    public void CanPlaceAt_OnConfiguredSelf_ReturnsTrue()
    {
        FakeWorldContext world = new();
        Block customStem = BlockRegistry.Get("stone");
        Block customSoil = BlockRegistry.Get("gravel");
        world.ReaderWriter.SetInitial(0, 63, 0, customStem.Id);

        CactusBehavior behavior = new(customStem, customSoil, 3, top: 0, side: 0, bottom: 0);

        Assert.True(behavior.CanPlaceAt(customStem, new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    [Fact]
    public void CanPlaceAt_OnVanillaSandWithCustomConfig_ReturnsFalse()
    {
        FakeWorldContext world = new();
        Block customStem = BlockRegistry.Get("stone");
        Block customSoil = BlockRegistry.Get("gravel");
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("sand").Id);

        CactusBehavior behavior = new(customStem, customSoil, 3, top: 0, side: 0, bottom: 0);

        Assert.False(behavior.CanPlaceAt(customStem, new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    [Fact]
    public void CanPlaceAt_SolidHorizontalNeighbor_ReturnsFalse()
    {
        FakeWorldContext world = new();
        Block cactus = BlockRegistry.Get("cactus");
        Block sand = BlockRegistry.Get("sand");
        world.ReaderWriter.SetInitial(0, 63, 0, sand.Id);
        world.ReaderWriter.SetInitial(1, 64, 0, BlockRegistry.Get("stone").Id);

        Assert.False(cactus.CanPlaceAt(new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"cactus","soil":"omniblock:sand"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("cactus", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"cactus","stem":"omniblock:cactus","soil":"not_a_real_block"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("cactus", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingMaxHeight_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"cactus","stem":"omniblock:cactus","soil":"omniblock:sand"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("cactus", json.RootElement));
    }
}
