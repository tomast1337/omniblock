using System.Collections.Generic;
using System.Linq;
using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Loot;
using BetaSharp.Loot.Conditions;

namespace BetaSharp.Tests.Loot;

/// <summary>
/// Covers the shared pool-based loot model: additive pools, weighted entries within a pool,
/// conditions (including the killer gate behind creeper discs), and context-aware entries.
/// </summary>
public sealed class LootTableTests
{
    private static readonly Item s_arrow = Item.ByName("arrow");
    private static readonly Item s_bone = Item.ByName("bone");

    private static LootContext Context(Entity? self = null, Entity? killer = null) =>
        new(self, killer, 0, System.Random.Shared);

    [Fact]
    public void Single_entry_pool_always_yields_that_item()
    {
        LootTable table = LootTable.Single(s_arrow, 1, 1);

        for (int i = 0; i < 20; i++)
        {
            ItemStack stack = Assert.Single(table.Roll(Context()));
            Assert.Equal(s_arrow.Id, stack.ItemId);
        }
    }

    [Fact]
    public void Count_range_is_respected()
    {
        LootTable table = LootTable.Single(s_arrow, 2, 4);
        HashSet<int> seenCounts = [];

        for (int i = 0; i < 300; i++)
        {
            int count = table.Roll(Context()).Count();
            Assert.InRange(count, 2, 4);
            seenCounts.Add(count);
        }

        Assert.Equal([2, 3, 4], seenCounts.OrderBy(c => c));
    }

    [Fact]
    public void Pools_are_additive_so_both_contribute()
    {
        LootTable table = new(
            new LootPool([LootEntry.Of(s_arrow)], 1, 1),
            new LootPool([LootEntry.Of(s_bone)], 1, 1));

        List<int> ids = table.Roll(Context()).Select(s => s.ItemId).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Contains(s_arrow.Id, ids);
        Assert.Contains(s_bone.Id, ids);
    }

    [Fact]
    public void Entries_within_a_pool_are_exclusive_and_weighted()
    {
        LootTable table = new(new LootPool([LootEntry.Of(s_arrow, weight: 9), LootEntry.Of(s_bone, weight: 1)], 1, 1));
        int arrows = 0;
        int bones = 0;

        for (int i = 0; i < 400; i++)
        {
            ItemStack stack = Assert.Single(table.Roll(Context()));
            if (stack.ItemId == s_arrow.Id) arrows++;
            else bones++;
        }

        Assert.True(arrows > 0);
        Assert.True(bones > 0);
        // Only ever one item per roll, and the 9:1 weighting clearly favours arrows.
        Assert.Equal(400, arrows + bones);
        Assert.True(arrows > bones);
    }

    [Fact]
    public void Zero_weight_entry_is_never_picked()
    {
        LootTable table = new(new LootPool([LootEntry.Of(s_arrow, weight: 0), LootEntry.Of(s_bone, weight: 1)], 1, 1));

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(s_bone.Id, Assert.Single(table.Roll(Context())).ItemId);
        }
    }

    [Fact]
    public void Failing_condition_suppresses_only_its_own_pool()
    {
        LootTable table = new(
            new LootPool([LootEntry.Of(s_arrow)], 1, 1),
            new LootPool([LootEntry.Of(s_bone)], 1, 1, new NeverCondition()));

        ItemStack stack = Assert.Single(table.Roll(Context()));
        Assert.Equal(s_arrow.Id, stack.ItemId);
    }

    [Fact]
    public void Killed_by_condition_gates_on_the_killer_type()
    {
        FakeWorldContext world = new();
        LootTable table = new(new LootPool([LootEntry.Of(s_bone)], 1, 1, new KilledByCondition<EntitySkeleton>()));

        Assert.Empty(table.Roll(Context(killer: null)));
        Assert.Empty(table.Roll(Context(killer: (EntityAnimal)EntityRegistry.ByName("pig").Create(world))));
        Assert.Single(table.Roll(Context(killer: new EntitySkeleton(world))));
    }

    [Fact]
    public void On_fire_condition_reads_the_dropping_entity()
    {
        FakeWorldContext world = new();
        BurningPig pig = new(world);
        LootTable table = new(new LootPool([LootEntry.Of(s_bone)], 1, 1, new OnFireCondition()));

        Assert.Empty(table.Roll(Context(self: pig)));

        pig.Ignite();
        Assert.Single(table.Roll(Context(self: pig)));
    }

    [Fact]
    public void Entry_can_resolve_its_stack_from_the_context()
    {
        LootTable table = new(new LootPool(
            [new LootEntry(context => new ItemStack(s_arrow.Id, 1, context.BlockMeta))], 1, 1));

        ItemStack stack = Assert.Single(table.Roll(new LootContext(null, null, 7, System.Random.Shared)));
        Assert.Equal(7, stack.getDamage());
    }

    /// <summary>Local stand-in for a gate that never opens; production conditions are all named types.</summary>
    private sealed class NeverCondition : ILootCondition
    {
        public bool Test(in LootContext context) => false;
    }

    private sealed class BurningPig(BetaSharp.Worlds.Core.Systems.IWorldContext world) : EntityAnimal(world, EntityRegistry.ByName("pig"))
    {
        public void Ignite() => FireTicks = 100;
    }
}
