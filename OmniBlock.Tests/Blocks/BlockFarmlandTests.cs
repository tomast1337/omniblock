using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockFarmlandTests
{
    [Fact]
    public void GetDroppedItemId_CustomDirt_ReturnsConfiguredBlocksDrop()
    {
        Block customRevertTarget = BlockRegistry.Get("sand");
        FarmlandBehavior behavior = new(customRevertTarget, BlockRegistry.Get("wheat"), 4, 5, 4, wet: 0, dry: 0, side: 0);

        Assert.Equal(customRevertTarget.GetDroppedItemId(0), behavior.GetDroppedItemId(BlockRegistry.Get("farmland"), 0, 0));
    }

    [Fact]
    public void NeighborUpdate_SolidBlockAbove_RevertsToConfiguredBlockNotVanillaDirt()
    {
        FakeWorldContext world = new();
        Block farmlandBlock = BlockRegistry.Get("farmland");
        Block customRevertTarget = BlockRegistry.Get("sand");

        world.ReaderWriter.SetInitial(0, 64, 0, farmlandBlock.Id);
        world.ReaderWriter.SetInitial(0, 65, 0, BlockRegistry.Get("stone").Id);

        FarmlandBehavior behavior = new(customRevertTarget, BlockRegistry.Get("wheat"), 4, 5, 4, wet: 0, dry: 0, side: 0);
        behavior.NeighborUpdate(farmlandBlock, new OnTickEvent(world, 0, 64, 0, 0, farmlandBlock.Id));

        Assert.Equal(customRevertTarget.Id, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"farmland","revert_block":"omniblock:dirt"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("farmland", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"farmland","revert_block":"not_a_real_block","crop":"omniblock:wheat"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("farmland", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingTrampleChanceOneIn_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"farmland","revert_block":"omniblock:dirt","crop":"omniblock:wheat"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("farmland", json.RootElement));
    }
}
