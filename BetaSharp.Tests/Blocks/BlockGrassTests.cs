using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockGrassTests
{
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
        using JsonDocument json = JsonDocument.Parse("""{"Type":"grass_ticker","soil":"omniblock:dirt","die_light_threshold":4,"die_chance_one_in":4,"spread_light_threshold":9}""");
        object behavior = BehaviorRegistry.Build("grass_ticker", json.RootElement);
        Assert.IsType<GrassTickerBehavior>(behavior);
    }

    // Numeric tuning params (die_light_threshold, die_chance_one_in, spread_light_threshold) are
    // also required, no default: an omitted value must throw immediately at
    // BehaviorRegistry.Build.
    [Fact]
    public void BehaviorRegistry_Build_MissingDieLightThreshold_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"grass_ticker","soil":"omniblock:dirt"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("grass_ticker", json.RootElement));
    }
}
