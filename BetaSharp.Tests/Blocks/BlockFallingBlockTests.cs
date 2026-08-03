using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockFallingBlockTests
{
    private static OnTickEvent Tick(FakeWorldContext world, int x = 0, int y = 64, int z = 0) => new(world, x, y, z, world.Reader.GetBlockMeta(x, y, z), world.Reader.GetBlockId(x, y, z));

    [Fact]
    public void CanFallThrough_ConfiguredObstacle_ReturnsTrue()
    {
        FakeWorldContext world = new();
        Block customPassable = BlockRegistry.Get("torch");
        world.ReaderWriter.SetInitial(0, 63, 0, customPassable.Id);

        FallingBlockBehavior behavior = new([customPassable], 32);

        Assert.True(behavior.CanFallThrough(Tick(world, 0, 63, 0)));
    }

    [Fact]
    public void CanFallThrough_VanillaFireNotInCustomConfig_ReturnsFalse()
    {
        FakeWorldContext world = new();
        Block customPassable = BlockRegistry.Get("torch");
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("fire").Id);

        FallingBlockBehavior behavior = new([customPassable], 32);

        Assert.False(behavior.CanFallThrough(Tick(world, 0, 63, 0)));
    }

    [Fact]
    public void CanFallThrough_Air_AlwaysReturnsTrue()
    {
        FakeWorldContext world = new();
        FallingBlockBehavior behavior = new([], 32);

        Assert.True(behavior.CanFallThrough(Tick(world, 0, 63, 0)));
    }

    [Fact]
    public void CanFallThrough_WaterMaterial_AlwaysReturnsTrue()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("water").Id);
        FallingBlockBehavior behavior = new([], 32);

        Assert.True(behavior.CanFallThrough(Tick(world, 0, 63, 0)));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"falling_block"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("falling_block", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"falling_block","passable":["not_a_real_block"]}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("falling_block", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingRegionLoadCheckRadius_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"falling_block","passable":["betasharp:fire"]}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("falling_block", json.RootElement));
    }
}
