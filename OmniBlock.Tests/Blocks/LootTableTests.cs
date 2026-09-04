using OmniBlock.Blocks;

namespace OmniBlock.Tests.Blocks;

public sealed class LootTableTests
{
    [Fact]
    public void SingleEntry_AlwaysRollsThatItem()
    {
        LootTable table = new(new LootEntry(() => 42));
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(42, table.Roll(Random.Shared));
        }
    }

    [Fact]
    public void ZeroWeightEntry_NeverRolled()
    {
        LootTable table = new(new LootEntry(() => 1, 0), new LootEntry(() => 2));
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(2, table.Roll(Random.Shared));
        }
    }

    [Fact]
    public void WeightedEntries_BothOutcomesReachable()
    {
        LootTable table = new(new LootEntry(() => 1, 9), new LootEntry(() => 2));
        var sawFirst = false;
        var sawSecond = false;
        for (var i = 0; i < 200; i++)
        {
            var rolled = table.Roll(Random.Shared);
            sawFirst |= rolled == 1;
            sawSecond |= rolled == 2;
        }

        Assert.True(sawFirst);
        Assert.True(sawSecond);
    }

    [Fact]
    public void ItemIdProvider_IsInvokedLazily()
    {
        var callCount = 0;
        LootTable table = new(new LootEntry(() =>
        {
            callCount++;
            return 7;
        }));

        Assert.Equal(0, callCount);
        table.Roll(Random.Shared);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void GetPrimaryItemId_SingleEntry_ReturnsThatItem()
    {
        LootTable table = new(new LootEntry(() => 42));
        Assert.Equal(42, table.GetPrimaryItemId());
    }

    [Fact]
    public void GetPrimaryItemId_WeightedEntries_ReturnsHighestWeight()
    {
        LootTable table = new(new LootEntry(() => 1, 9), new LootEntry(() => 2));
        Assert.Equal(1, table.GetPrimaryItemId());
    }
}
