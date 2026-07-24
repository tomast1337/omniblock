using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockSpongeTests
{
    [Fact]
    public void OnBreak_ConfiguredRadius_RunsWithoutError()
    {
        FakeWorldContext world = new();
        Block sponge = BlockRegistry.Get("sponge");

        SpongeLifecycleBehavior behavior = new(1);
        behavior.OnBreak(sponge, new OnBreakEvent(world, null, 0, 64, 0));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingAbsorbRadius_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"sponge_lifecycle"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("sponge_lifecycle", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_ValidAbsorbRadius_ConstructsBehavior()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"sponge_lifecycle","absorb_radius":2}""");
        object behavior = BehaviorRegistry.Build("sponge_lifecycle", json.RootElement);
        Assert.IsType<SpongeLifecycleBehavior>(behavior);
    }
}
