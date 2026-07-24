using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockMushroomTests
{
    private static OnTickEvent Tick(FakeWorldContext world, int x = 0, int y = 64, int z = 0) => new(world, x, y, z, world.Reader.GetBlockMeta(x, y, z), world.Reader.GetBlockId(x, y, z));

    [Fact]
    public void CanGrow_ConfiguredSubstrate_ReturnsTrue()
    {
        FakeWorldContext world = new();
        Block mushroom = BlockRegistry.Get("brown_mushroom");
        Block customGround = BlockRegistry.Get("sand");
        world.ReaderWriter.SetInitial(0, 63, 0, customGround.id);

        MushroomBehavior behavior = new([customGround], 100, 13);

        Assert.True(behavior.CanGrow(mushroom, Tick(world, 0, 64, 0)));
    }

    [Fact]
    public void CanGrow_VanillaSubstrateNotInCustomConfig_ReturnsFalse()
    {
        FakeWorldContext world = new();
        Block mushroom = BlockRegistry.Get("brown_mushroom");
        Block customGround = BlockRegistry.Get("sand");
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("dirt").id);

        MushroomBehavior behavior = new([customGround], 100, 13);

        Assert.False(behavior.CanGrow(mushroom, Tick(world, 0, 64, 0)));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"mushroom"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("mushroom", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"mushroom","valid_ground":["dirt","not_a_real_block"]}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("mushroom", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingSpreadChanceOneIn_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"mushroom","valid_ground":["dirt"]}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("mushroom", json.RootElement));
    }
}
