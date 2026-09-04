namespace OmniBlock.Tests.Blocks;

// Regression for BlockFactory.AttachBehaviors' array-based schema: BlockDefinition.Behaviors is
// a list of {"Slots": [...], "Type": ..., ...params} entries, not a dict keyed by slot. Exactly
// one instance is built per entry (one BehaviorRegistry.Build call) and wired into every slot
// named in that entry's "Slots" array — so slots sharing an entry share the literal same object,
// not just equal config. Verified against the real vanilla block JSONs (leaves/log/fire/wheat/
// farmland), not a synthetic BlockDefinition — constructing a throwaway Block permanently
// occupies a slot in the bootstrap block store (the constructor throws if the slot is
// already taken and there's no unregister), so reusing already-loaded blocks avoids polluting
// shared test-process state.
public sealed class BlockFactoryArrayTests
{
    [Fact]
    public void Leaves_MultiSlotEntry_SharesSameInstanceAcrossAllListedSlots()
    {
        var leaves = TestBlocks.Get("leaves");
        Assert.Same(leaves.Ticker, leaves.Lifecycle);
        Assert.Same(leaves.Ticker, leaves.Visuals);
    }

    [Fact]
    public void Log_MultiSlotEntry_SharesSameInstanceAcrossAllListedSlots()
    {
        var log = TestBlocks.Get("log");
        Assert.Same(log.Visuals, log.Lifecycle);
    }

    [Fact]
    public void Fire_MultiSlotEntry_SharesSameInstanceAcrossAllListedSlots()
    {
        var fire = TestBlocks.Get("fire");
        Assert.Same(fire.Ticker, fire.Physics);
        Assert.Same(fire.Ticker, fire.Lifecycle);
    }

    [Fact]
    public void Wheat_MultiSlotEntry_SharesSameInstanceAcrossAllListedSlots()
    {
        var wheat = TestBlocks.Get("wheat");
        Assert.Same(wheat.Ticker, wheat.Physics);
        Assert.Same(wheat.Ticker, wheat.Lifecycle);
        Assert.Same(wheat.Ticker, wheat.Visuals);
    }

    [Fact]
    public void Farmland_MultiSlotEntry_SharesSameInstanceAcrossAllListedSlots()
    {
        var farmland = TestBlocks.Get("farmland");
        Assert.Same(farmland.Ticker, farmland.Physics);
        Assert.Same(farmland.Ticker, farmland.Interactable);
        Assert.Same(farmland.Ticker, farmland.Lifecycle);
        Assert.Same(farmland.Ticker, farmland.Visuals);
    }
}
