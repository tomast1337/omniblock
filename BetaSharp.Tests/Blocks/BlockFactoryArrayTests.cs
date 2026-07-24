using BetaSharp.Blocks;

namespace BetaSharp.Tests.Blocks;

// Regression for BlockFactory.AttachBehaviors' array-based schema: BlockDefinition.Behaviors is
// a list of {"Slots": [...], "Type": ..., ...params} entries, not a dict keyed by slot. Exactly
// one instance is built per entry (one BehaviorRegistry.Build call) and wired into every slot
// named in that entry's "Slots" array — so slots sharing an entry share the literal same object,
// not just equal config. Verified against the real vanilla block JSONs (leaves/log/fire/wheat/
// farmland), not a synthetic BlockDefinition — constructing a throwaway Block permanently
// occupies a slot in the global Block.Blocks[] array (the constructor throws if the slot is
// already taken and there's no unregister), so reusing already-loaded blocks avoids polluting
// shared test-process state.
public sealed class BlockFactoryArrayTests
{
    [Fact]
    public void Leaves_MultiSlotEntry_SharesSameInstanceAcrossAllListedSlots()
    {
        Block leaves = BlockRegistry.Get("leaves");
        Assert.Same((object?)leaves.Ticker, (object?)leaves.Lifecycle);
        Assert.Same((object?)leaves.Ticker, (object?)leaves.Visuals);
    }

    [Fact]
    public void Log_MultiSlotEntry_SharesSameInstanceAcrossAllListedSlots()
    {
        Block log = BlockRegistry.Get("log");
        Assert.Same((object?)log.Visuals, (object?)log.Lifecycle);
    }

    [Fact]
    public void Fire_MultiSlotEntry_SharesSameInstanceAcrossAllListedSlots()
    {
        Block fire = BlockRegistry.Get("fire");
        Assert.Same((object?)fire.Ticker, (object?)fire.Physics);
        Assert.Same((object?)fire.Ticker, (object?)fire.Lifecycle);
    }

    [Fact]
    public void Wheat_MultiSlotEntry_SharesSameInstanceAcrossAllListedSlots()
    {
        Block wheat = BlockRegistry.Get("wheat");
        Assert.Same((object?)wheat.Ticker, (object?)wheat.Physics);
        Assert.Same((object?)wheat.Ticker, (object?)wheat.Lifecycle);
        Assert.Same((object?)wheat.Ticker, (object?)wheat.Visuals);
    }

    [Fact]
    public void Farmland_MultiSlotEntry_SharesSameInstanceAcrossAllListedSlots()
    {
        Block farmland = BlockRegistry.Get("farmland");
        Assert.Same((object?)farmland.Ticker, (object?)farmland.Physics);
        Assert.Same((object?)farmland.Ticker, (object?)farmland.Interactable);
        Assert.Same((object?)farmland.Ticker, (object?)farmland.Lifecycle);
        Assert.Same((object?)farmland.Ticker, (object?)farmland.Visuals);
    }
}
