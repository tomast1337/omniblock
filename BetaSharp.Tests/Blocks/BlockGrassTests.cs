using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockGrassTests
{
    // "Dead" state (dirt) is a required, JSON-declared constructor param — no built-in
    // vanilla fallback. An omitted or unknown name must throw immediately at
    // BehaviorRegistry.Build time (server boot), not silently default or defer to a later
    // tick. OnTick itself is light-level-gated through the real LightingEngine (not easily
    // deterministic against FakeWorldContext without a full sky-light simulation), so
    // coverage here is scoped to the config-injection contract, same as the crash tests
    // added for LeavesBehavior/LogBehavior.
    [Fact]
    public void BehaviorRegistry_Build_MissingDirt_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"grass_ticker"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("grass_ticker", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"grass_ticker","soil":"not_a_real_block"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("grass_ticker", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_ValidDirt_ConstructsBehavior()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"grass_ticker","soil":"dirt"}""");
        object behavior = BehaviorRegistry.Build("grass_ticker", json.RootElement);
        Assert.IsType<GrassTickerBehavior>(behavior);
    }
}
