namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodMovementScenarioTests
{
    [Theory]
    [InlineData("")]
    [InlineData("-prepare")]
    public void Larger_horizon_preserves_the_same_route_assertions_and_budgets(string suffix)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OmniBlock.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var scripts = Path.Combine(directory.FullName, "tests", "e2e");
        var baseline = File.ReadAllText(Path.Combine(scripts, $"terrain-lod-scale-512-movement{suffix}.luau"));
        var larger = File.ReadAllText(Path.Combine(scripts, $"terrain-lod-scale-1024-movement{suffix}.luau"));

        // These are standalone startup scripts (the sandbox does not load arbitrary modules).
        // Permit only the session profile, required spatial level, and fixture label to differ;
        // increasing a timeout or byte/queue ceiling only at 1024 must not hide a scale regression.
        var expected = baseline
            .Replace("configureTerrainLodScaleProfile(512)", "configureTerrainLodScaleProfile(1024)")
            .Replace("prepareTerrainLodFixture(512,", "prepareTerrainLodFixture(1024,")
            .Replace("prepared persistent 512 movement", "prepared persistent 1024 movement")
            .Replace(">= 7, \"L7 was never presented\"", ">= 8, \"L8 was never presented\"");
        Assert.Equal(expected, larger);
    }
}
