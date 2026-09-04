using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockMushroomTests
{
    private static OnTickEvent Tick(FakeWorldContext world, int x = 0, int y = 64, int z = 0) => new(world, x, y, z, world.Reader.GetBlockMeta(x, y, z), world.Reader.GetBlockId(x, y, z));

    [Fact]
    public void CanGrow_ConfiguredSubstrate_ReturnsTrue()
    {
        FakeWorldContext world = new();
        var mushroom = TestBlocks.Get("brown_mushroom");
        var customGround = TestBlocks.Get("sand");
        world.ReaderWriter.SetInitial(0, 63, 0, customGround.Id);

        MushroomBehavior behavior = new([customGround], 100, 13);

        Assert.True(behavior.CanGrow(mushroom, Tick(world)));
    }

    [Fact]
    public void CanGrow_VanillaSubstrateNotInCustomConfig_ReturnsFalse()
    {
        FakeWorldContext world = new();
        var mushroom = TestBlocks.Get("brown_mushroom");
        var customGround = TestBlocks.Get("sand");
        world.ReaderWriter.SetInitial(0, 63, 0, TestBlocks.Get("dirt").Id);

        MushroomBehavior behavior = new([customGround], 100, 13);

        Assert.False(behavior.CanGrow(mushroom, Tick(world)));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using var json = JsonDocument.Parse("""{"Type":"mushroom"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("mushroom", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using var json = JsonDocument.Parse("""{"Type":"mushroom","valid_ground":["omniblock:dirt","not_a_real_block"]}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("mushroom", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingSpreadChanceOneIn_Throws()
    {
        using var json = JsonDocument.Parse("""{"Type":"mushroom","valid_ground":["omniblock:dirt"]}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("mushroom", json.RootElement));
    }
}
