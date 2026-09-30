using OmniBlock.Blocks;
using OmniBlock.Items;
using OmniBlock.Network.Messages;

namespace OmniBlock.Tests.Items;

public sealed class BlockItemPlacementTests
{
    [Theory]
    [InlineData(0.25F, 2)]
    [InlineData(0.75F, 10)]
    public void Horizontal_click_places_slab_in_selected_half(float hitY, int expectedMeta)
    {
        FakeWorldContext world = new();
        var stone = world.Content.Blocks.Get("stone");
        var slab = world.Content.Blocks.Get("slab");
        world.ReaderWriter.SetInitial(0, 64, 0, stone.Id);
        ItemStack stack = new(world.Content.Items.Get("slab"), 2, 2);

        Assert.True(stack.useOnBlock(new TestEntityPlayer(world), world, 0, 64, 0, (int)Side.East, hitY));

        Assert.Equal(slab.Id, world.Reader.GetBlockId(1, 64, 0));
        Assert.Equal(expectedMeta, world.Reader.GetBlockMeta(1, 64, 0));
    }

    [Theory]
    [InlineData(2, (int)Side.Up, 0.5F)]
    [InlineData(10, (int)Side.Down, 0.5F)]
    [InlineData(2, (int)Side.East, 0.75F)]
    [InlineData(10, (int)Side.East, 0.25F)]
    public void Matching_opposite_slab_half_merges_in_the_same_cell(int existingMeta, int side, float hitY)
    {
        FakeWorldContext world = new();
        var slab = world.Content.Blocks.Get("slab");
        var doubled = world.Content.Blocks.Get("double_slab");
        world.ReaderWriter.SetInitial(0, 64, 0, slab.Id, existingMeta);
        ItemStack stack = new(world.Content.Items.Get("slab"), 2, 2);

        Assert.True(stack.useOnBlock(new TestEntityPlayer(world), world, 0, 64, 0, side, hitY));

        Assert.Equal(doubled.Id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(2, world.Reader.GetBlockMeta(0, 64, 0));
        Assert.Equal(0, world.Reader.GetBlockId(1, 64, 0));
    }

    [Fact]
    public void Different_slab_materials_do_not_merge()
    {
        FakeWorldContext world = new();
        var slab = world.Content.Blocks.Get("slab");
        world.ReaderWriter.SetInitial(0, 64, 0, slab.Id, 1);
        ItemStack stack = new(world.Content.Items.Get("slab"), 2, 2);

        Assert.True(stack.useOnBlock(new TestEntityPlayer(world), world, 0, 64, 0, (int)Side.Up, 0.5F));

        Assert.Equal(slab.Id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(1, world.Reader.GetBlockMeta(0, 64, 0));
        Assert.Equal(slab.Id, world.Reader.GetBlockId(0, 65, 0));
    }

    [Theory]
    [InlineData(0.25F, 0)]
    [InlineData(0.75F, 4)]
    public void Horizontal_click_selects_stair_half_without_changing_facing_bits(float hitY, int expectedHalf)
    {
        FakeWorldContext world = new();
        var stairs = world.Content.Blocks.Get("cobblestone_stairs");
        world.ReaderWriter.SetInitial(0, 64, 0, world.Content.Blocks.Get("stone").Id);
        ItemStack stack = new(world.Content.Items.Get("cobblestone_stairs"));

        Assert.True(stack.useOnBlock(new TestEntityPlayer(world), world, 0, 64, 0, (int)Side.East, hitY));

        Assert.Equal(stairs.Id, world.Reader.GetBlockId(1, 64, 0));
        Assert.Equal(expectedHalf, world.Reader.GetBlockMeta(1, 64, 0) & 4);
    }

    [Fact]
    public void Interaction_packet_round_trips_hit_height_and_declares_new_schema()
    {
        var items = new FakeWorldContext().Content.Items;
        InteractBlockMessage sent = new() { X = 4, Y = 64, Z = 7, Side = (byte)Side.East, HitY = 192 };
        using MemoryStream wire = new();
        sent.Write(wire);
        Assert.Equal(wire.Length, sent.Size());
        wire.Position = 0;
        InteractBlockMessage received = new(items);
        received.Read(wire);
        Assert.Equal(2, sent.SchemaVersion);
        Assert.Equal(sent.HitY, received.HitY);
    }
}
