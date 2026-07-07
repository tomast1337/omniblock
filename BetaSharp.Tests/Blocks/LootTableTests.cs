using BetaSharp.Blocks;

namespace BetaSharp.Tests.Blocks;

public sealed class LootTableTests
{
    [Fact]
    public void SingleEntry_AlwaysRollsThatItem()
    {
        LootTable table = new(new LootEntry(() => 42));
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(42, table.Roll(Random.Shared));
        }
    }

    [Fact]
    public void ZeroWeightEntry_NeverRolled()
    {
        LootTable table = new(new LootEntry(() => 1, weight: 0), new LootEntry(() => 2, weight: 1));
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(2, table.Roll(Random.Shared));
        }
    }

    [Fact]
    public void WeightedEntries_BothOutcomesReachable()
    {
        LootTable table = new(new LootEntry(() => 1, weight: 9), new LootEntry(() => 2, weight: 1));
        bool sawFirst = false;
        bool sawSecond = false;
        for (int i = 0; i < 200; i++)
        {
            int rolled = table.Roll(Random.Shared);
            sawFirst |= rolled == 1;
            sawSecond |= rolled == 2;
        }

        Assert.True(sawFirst);
        Assert.True(sawSecond);
    }

    [Fact]
    public void ItemIdProvider_IsInvokedLazily()
    {
        int callCount = 0;
        LootTable table = new(new LootEntry(() =>
        {
            callCount++;
            return 7;
        }));

        Assert.Equal(0, callCount);
        table.Roll(Random.Shared);
        Assert.Equal(1, callCount);
    }
}
