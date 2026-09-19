using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Worlds;

public sealed class TerrainLodCacheIdentityTests
{
    [Fact]
    public void Same_seed_in_different_saves_has_a_different_world_identity()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);

        var first = TerrainLodCacheIdentity.FromWorld(world, materials, "/saves/first");
        var second = TerrainLodCacheIdentity.FromWorld(world, materials, "/saves/second");

        Assert.NotEqual(first.WorldFingerprint, second.WorldFingerprint);
        Assert.NotEqual(first.CompatibilityFingerprint, second.CompatibilityFingerprint);
    }

    [Fact]
    public void Presentation_compatibility_reports_the_specific_changed_dependency()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var compatible = TerrainLodCacheIdentity.FromWorld(world, materials, "/saves/world");

        Assert.Null(compatible.GetPresentationIncompatibility(
            world.Dimension.Id, world.Content, materials));
        Assert.Equal("content catalog fingerprint differs",
            (compatible with { ContentFingerprint = "changed" })
            .GetPresentationIncompatibility(world.Dimension.Id, world.Content, materials));
        Assert.Equal("reduction schema 999 is unsupported",
            (compatible with { ReductionSchemaVersion = 999 })
            .GetPresentationIncompatibility(world.Dimension.Id, world.Content, materials));
        Assert.Equal("material rules fingerprint differs",
            (compatible with { MaterialRulesFingerprint = "changed" })
            .GetPresentationIncompatibility(world.Dimension.Id, world.Content, materials));
    }
}
