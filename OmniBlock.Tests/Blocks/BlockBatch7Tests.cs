using System.Text.Json;
using OmniBlock.Blocks.Behaviors;

namespace OmniBlock.Tests.Blocks;

// Batch 7: DispenserBehavior, PortalBehavior, RedstoneWireBehavior, TallGrassBehavior,
// SnowBehavior. Existing tests (BlockDispenserTests, BlockRedstoneWireTests,
// BlockRedstoneRepeaterTests) already exercise these end-to-end through real vanilla config via
// BlockRegistry and kept passing unchanged, proving the parameterization preserved behavior.
// Coverage here is scoped to what's new: the crash contract (no defaults, no fallback) plus the
// two behaviors whose public API shape changed (PortalBehavior.Create gained explicit block
// params; RedstoneWireBehavior.IsPowerProviderOrWire went from static to instance).
public sealed class BlockBatch7Tests
{
    [Fact]
    public void PortalBehavior_Create_ConfiguredBlocks_BuildsPortalInFrame()
    {
        FakeWorldContext world = new();
        var obsidian = TestBlocks.Get("obsidian");
        var fire = TestBlocks.Get("fire");
        var netherPortal = TestBlocks.Get("nether_portal");

        // 4-wide, 5-tall obsidian frame enclosing a 2x3 empty interior at x=1..2, y=1..3.
        for (var x = 0; x <= 3; x++)
        {
            for (var y = 0; y <= 4; y++)
            {
                var isFrame = x == 0 || x == 3 || y == 0 || y == 4;
                if (isFrame) world.ReaderWriter.SetInitial(x, y, 0, obsidian.Id);
            }
        }

        var created = PortalBehavior.Create(world.Reader, world.Writer, 1, 1, 0, obsidian, fire, netherPortal);

        Assert.True(created);
        Assert.Equal(netherPortal.Id, world.Reader.GetBlockId(1, 1, 0));
        Assert.Equal(netherPortal.Id, world.Reader.GetBlockId(2, 3, 0));
    }

    [Fact]
    public void PortalBehavior_Create_NoObsidianFrame_ReturnsFalse()
    {
        FakeWorldContext world = new();
        var obsidian = TestBlocks.Get("obsidian");
        var fire = TestBlocks.Get("fire");
        var netherPortal = TestBlocks.Get("nether_portal");

        Assert.False(PortalBehavior.Create(world.Reader, world.Writer, 1, 1, 0, obsidian, fire, netherPortal));
    }

    [Fact]
    public void RedstoneWireBehavior_IsPowerProviderOrWire_ConfiguredConductor_ReturnsTrue()
    {
        FakeWorldContext world = new();
        var wire = TestBlocks.Get("redstone_wire");
        var customConductor = TestBlocks.Get("torch");
        var repeater = TestBlocks.Get("repeater");
        var poweredRepeater = TestBlocks.Get("powered_repeater");
        world.ReaderWriter.SetInitial(0, 64, 0, customConductor.Id);

        RedstoneWireBehavior behavior = new(wire, [customConductor], repeater, poweredRepeater);
        behavior.BindRuntime(ContentRuntime.Current.Blocks);

        Assert.True(behavior.IsPowerProviderOrWire(world.Reader, 0, 64, 0, -1));
    }

    [Fact]
    public void RedstoneWireBehavior_IsPowerProviderOrWire_VanillaLeverNotInCustomConfig_ChecksCanEmitInstead()
    {
        FakeWorldContext world = new();
        var wire = TestBlocks.Get("redstone_wire");
        var customConductor = TestBlocks.Get("torch");
        var repeater = TestBlocks.Get("repeater");
        var poweredRepeater = TestBlocks.Get("powered_repeater");
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("lever").Id);

        RedstoneWireBehavior behavior = new(wire, [customConductor], repeater, poweredRepeater);
        behavior.BindRuntime(ContentRuntime.Current.Blocks);

        // Lever isn't in the custom conductor list, but it still falls through to
        // Blocks.GetByProtocolId(id).canEmitRedstonePower(), which is independently true for levers.
        Assert.True(behavior.IsPowerProviderOrWire(world.Reader, 0, 64, 0, -1));
    }

    [Theory]
    [InlineData("dispenser", """{"Type":"dispenser","egg":"omniblock:egg","snowball":"omniblock:snowball"}""")]
    [InlineData("portal", """{"Type":"portal"}""")]
    [InlineData("redstone_wire", """{"Type":"redstone_wire","conductors":["omniblock:button"],"repeater":"omniblock:repeater","powered_repeater":"omniblock:powered_repeater"}""")]
    [InlineData("tall_grass", """{"Type":"tall_grass"}""")]
    [InlineData("snow", """{"Type":"snow"}""")]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws(string type, string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build(type, doc.RootElement));
    }

    // Unknown BLOCK name: CanonicalRegistry.Get throws KeyNotFoundException.
    [Theory]
    [InlineData("portal", """{"Type":"portal","portal_base":"not_a_real_block"}""")]
    [InlineData("redstone_wire", """{"Type":"redstone_wire","wire":"omniblock:redstone_wire","conductors":["omniblock:button"],"repeater":"not_a_real_block","powered_repeater":"omniblock:powered_repeater"}""")]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws(string type, string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build(type, doc.RootElement));
    }

    // Unknown item name: the runtime registry throws for its own catalog lookup, not
    // CanonicalRegistry.Get's).
    [Theory]
    [InlineData("dispenser", """{"Type":"dispenser","arrow":"omniblock:arrow","egg":"omniblock:egg","snowball":"not_a_real_item"}""")]
    [InlineData("tall_grass", """{"Type":"tall_grass","seeds":"not_a_real_item"}""")]
    [InlineData("snow", """{"Type":"snow","drop_item":"not_a_real_item"}""")]
    public void BehaviorRegistry_Build_UnknownItemName_Throws(string type, string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build(type, doc.RootElement));
    }
}
