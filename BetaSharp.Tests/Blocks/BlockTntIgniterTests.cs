using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockTntIgniterTests
{
    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"tnt"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("tnt", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownItemName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"tnt","igniter":"not_a_real_item"}""");
        Assert.Throws<ArgumentException>(() => BehaviorRegistry.Build("tnt", json.RootElement));
    }
}
