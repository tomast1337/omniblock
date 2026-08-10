using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockReedTests
{
    [Fact]
    public void CanPlaceAt_ConfiguredGroundNextToWater_ReturnsTrue()
    {
        FakeWorldContext world = new();
        Block reeds = BlockRegistry.Get("sugar_cane");
        Block customGround = BlockRegistry.Get("stone");
        world.ReaderWriter.SetInitial(0, 63, 0, customGround.Id);
        world.ReaderWriter.SetInitial(1, 63, 0, BlockRegistry.Get("water").Id);

        ReedBehavior behavior = new([customGround]);

        Assert.True(behavior.CanPlaceAt(reeds, new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    [Fact]
    public void CanPlaceAt_ConfiguredGroundWithoutWater_ReturnsFalse()
    {
        FakeWorldContext world = new();
        Block reeds = BlockRegistry.Get("sugar_cane");
        Block customGround = BlockRegistry.Get("stone");
        world.ReaderWriter.SetInitial(0, 63, 0, customGround.Id);

        ReedBehavior behavior = new([customGround]);

        Assert.False(behavior.CanPlaceAt(reeds, new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    [Fact]
    public void CanPlaceAt_VanillaGroundNotInCustomConfig_ReturnsFalse()
    {
        FakeWorldContext world = new();
        Block reeds = BlockRegistry.Get("sugar_cane");
        Block customGround = BlockRegistry.Get("stone");
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("dirt").Id);
        world.ReaderWriter.SetInitial(1, 63, 0, BlockRegistry.Get("water").Id);

        ReedBehavior behavior = new([customGround]);

        Assert.False(behavior.CanPlaceAt(reeds, new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"reed"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("reed", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"reed","valid_ground":["omniblock:dirt","not_a_real_block"]}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("reed", json.RootElement));
    }
}
