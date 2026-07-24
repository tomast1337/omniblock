using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Tests.Blocks;

// Igniter tool is a required constructor param (JSON-configurable per variant, no built-in
// vanilla fallback). OnBlockBreakStart itself needs a real EntityPlayer (no lightweight fake
// exists in this test project), so coverage here is scoped to the config-injection contract —
// same as the crash tests added throughout this migration for behaviors whose functional path
// needs infrastructure heavier than FakeWorldContext provides.
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
