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
        Assert.Equal("maximum spatial level 11 is unsupported",
            (compatible with { MaximumSpatialLevel = 11 })
            .GetPresentationIncompatibility(world.Dimension.Id, world.Content, materials));
        Assert.Equal("quality-policy version 999 is unsupported",
            (compatible with { QualityPolicyVersion = 999 })
            .GetPresentationIncompatibility(world.Dimension.Id, world.Content, materials));
    }

    [Fact]
    public void Cache_contract_participates_in_identity_and_negotiates_the_lower_level()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var levelSix = TerrainLodCacheIdentity.FromWorld(
            world, materials, "/saves/world", TerrainLodSpatialPolicy.CreateDefault());
        var levelTen = TerrainLodCacheIdentity.FromWorld(
            world, materials, "/saves/world",
            TerrainLodSpatialPolicy.CreateForMaximumHorizon(
                TerrainLodSpatialPolicy.MaximumGeneratedHorizonChunks));

        Assert.Equal(6, levelSix.MaximumSpatialLevel);
        Assert.Equal(10, levelTen.MaximumSpatialLevel);
        Assert.Equal(TerrainLodSpatialPolicy.CurrentQualityPolicyVersion,
            levelTen.QualityPolicyVersion);
        Assert.NotEqual(levelSix.CompatibilityFingerprint, levelTen.CompatibilityFingerprint);
        Assert.Equal(levelSix.RecordFingerprint, levelTen.RecordFingerprint);
        Assert.Equal(6, levelTen.NegotiateMaximumSpatialLevel(6));
        Assert.Equal(4, levelSix.NegotiateMaximumSpatialLevel(4));
        Assert.Null(levelSix.GetRequestIncompatibility(
            4, TerrainLodSpatialPolicy.CurrentQualityPolicyVersion, 6));
        Assert.Equal("maximum spatial level 7 exceeds the server maximum 6",
            levelTen.GetRequestIncompatibility(
                7, TerrainLodSpatialPolicy.CurrentQualityPolicyVersion, 6));
        Assert.Equal($"quality-policy version 1 is unsupported; server policy is {TerrainLodSpatialPolicy.CurrentQualityPolicyVersion}",
            levelSix.GetRequestIncompatibility(6, 1, 6));
    }

    [Fact]
    public void Near_quality_upgrade_invalidates_old_cache_and_peer_identity_not_world_content()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var current = TerrainLodCacheIdentity.FromWorld(world, materials, "/saves/world");
        var previous = current with { QualityPolicyVersion = 1 };
        Assert.Equal(2, current.QualityPolicyVersion);
        Assert.Equal(previous.WorldFingerprint, current.WorldFingerprint);
        Assert.Equal(previous.ContentFingerprint, current.ContentFingerprint);
        Assert.NotEqual(previous.RecordFingerprint, current.RecordFingerprint);
        Assert.NotEqual(previous.CompatibilityFingerprint, current.CompatibilityFingerprint);
        Assert.Equal("quality-policy version 1 is unsupported",
            previous.GetPresentationIncompatibility(world.Dimension.Id, world.Content, materials));
    }
}
