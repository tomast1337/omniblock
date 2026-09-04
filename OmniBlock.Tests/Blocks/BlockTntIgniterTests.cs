using System.Text.Json;
using OmniBlock.Blocks.Behaviors;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockTntIgniterTests
{
    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using var json = JsonDocument.Parse("""{"Type":"tnt"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("tnt", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownItemName_Throws()
    {
        using var json = JsonDocument.Parse("""{"Type":"tnt","igniter":"not_a_real_item"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("tnt", json.RootElement));
    }
}
