using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockSpongeTests
{
    [Fact]
    public void OnBreak_ConfiguredRadius_RunsWithoutError()
    {
        FakeWorldContext world = new();
        var sponge = TestBlocks.Get("sponge");

        SpongeLifecycleBehavior behavior = new(1);
        behavior.OnBreak(sponge, new OnBreakEvent(world, null, 0, 64, 0));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingAbsorbRadius_Throws()
    {
        using var json = JsonDocument.Parse("""{"Type":"sponge_lifecycle"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("sponge_lifecycle", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_ValidAbsorbRadius_ConstructsBehavior()
    {
        using var json = JsonDocument.Parse("""{"Type":"sponge_lifecycle","absorb_radius":2}""");
        var behavior = BehaviorRegistry.Build("sponge_lifecycle", json.RootElement);
        Assert.IsType<SpongeLifecycleBehavior>(behavior);
    }
}
