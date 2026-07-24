using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockSpongeTests
{
    // Absorb radius is a required constructor param (JSON-configurable per variant, no built-in
    // vanilla fallback). NotifyNeighbors has no externally observable return value in
    // FakeWorldContext, so coverage here is scoped to proving a differently configured radius
    // runs to completion without error (config-injection contract), same as the crash tests
    // added throughout this migration for behaviors whose functional path needs infrastructure
    // heavier than FakeWorldContext provides.
    [Fact]
    public void OnBreak_ConfiguredRadius_RunsWithoutError()
    {
        FakeWorldContext world = new();
        Block sponge = BlockRegistry.Get("sponge");

        SpongeLifecycleBehavior behavior = new(1);
        behavior.OnBreak(sponge, new OnBreakEvent(world, null, 0, 64, 0));
    }

    // No built-in default and no null fallback: an omitted "absorb_radius" in JSON must throw
    // immediately (at BehaviorRegistry.Build, i.e. server boot).
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
