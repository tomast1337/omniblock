using OmniBlock.Blocks.Entities;
using OmniBlock.Items;
using OmniBlock.NBT;

namespace OmniBlock.Tests.Items;

public sealed class ItemStackComponentTests
{
    private static readonly ResourceLocation TestComponent = new("example", "target");

    [Fact]
    public void Components_survive_save_copy_split_and_network_round_trips()
    {
        var items = ContentRuntime.Current.Items;
        ItemStack original = new(items.Get("omniblock:spawner"), 4);
        original.SetStringComponent(TestComponent, "omniblock:skeleton");

        ItemStack saved = new(items, original.WriteToNbt(new NBTTagCompound()));
        ItemStack copy = original.Copy();
        ItemStack split = original.Split(1);

        using MemoryStream wire = new();
        wire.WriteItemStack(original);
        wire.Position = 0;
        ItemStack networked = Assert.IsType<ItemStack>(wire.ReadItemStack(items));

        Assert.All([saved, copy, split, networked], stack =>
            Assert.Equal("omniblock:skeleton", stack.GetStringComponent(TestComponent)));
    }

    [Fact]
    public void Components_are_part_of_stack_identity()
    {
        var item = ContentRuntime.Current.Items.Get("omniblock:spawner");
        ItemStack pig = new(item);
        ItemStack skeleton = new(item);
        pig.SetStringComponent(TestComponent, "omniblock:pig");
        skeleton.SetStringComponent(TestComponent, "omniblock:skeleton");

        Assert.False(ItemStack.AreEqual(pig, skeleton));
        Assert.False(pig.IsItemEqual(skeleton));
    }

    [Fact]
    public void Component_insertion_order_is_not_part_of_stack_identity()
    {
        var item = ContentRuntime.Current.Items.Get("omniblock:spawner");
        ResourceLocation firstKey = new("example", "first");
        ResourceLocation secondKey = new("example", "second");
        ItemStack first = new(item);
        first.SetStringComponent(firstKey, "a");
        first.SetStringComponent(secondKey, "b");
        ItemStack second = new(item);
        second.SetStringComponent(secondKey, "b");
        second.SetStringComponent(firstKey, "a");

        Assert.True(ItemStack.AreEqual(first, second));
        Assert.True(first.IsItemEqual(second));
    }

    [Fact]
    public void Spawner_block_entity_owns_its_item_component_schema()
    {
        ItemStack stack = new(ContentRuntime.Current.Items.Get("omniblock:spawner"));
        stack.SetStringComponent(BlockEntityMobSpawner.SpawnedEntityComponent, "omniblock:skeleton");
        BlockEntityMobSpawner spawner = new();

        ((IBlockEntityItemData)spawner).ApplyItemData(stack);

        Assert.Equal("omniblock:skeleton", spawner.GetSpawnedEntityId());
    }

    [Fact]
    public void Placing_a_block_item_transfers_components_to_its_block_entity()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, world.Content.Blocks.Get("omniblock:stone").Id);
        ItemStack stack = new(world.Content.Items.Get("omniblock:spawner"));
        stack.SetStringComponent(BlockEntityMobSpawner.SpawnedEntityComponent, "omniblock:skeleton");
        TestEntityPlayer player = new(world);

        Assert.True(stack.GetItem().useOnBlock(stack, player, world, 0, 63, 0, 1));

        var placed = world.Entities.GetBlockEntity<BlockEntityMobSpawner>(0, 64, 0);
        Assert.NotNull(placed);
        Assert.Equal("omniblock:skeleton", placed.GetSpawnedEntityId());
    }
}
