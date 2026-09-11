using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainAmbientLightingTests
{
    [Fact]
    public void Region_snapshot_exposes_raw_light_independent_of_ambient_darkness()
    {
        LightTestWorld world = new();
        var chunk = world.Chunks.Add(0, 0, populateLight: false);
        chunk.SkyLight.SetNibble(8, 64, 8, 12);
        chunk.BlockLight.SetNibble(8, 64, 8, 7);

        world.Environment.AmbientDarkness = 0;
        using var day = new WorldRegionSnapshot(world, 7, 63, 7, 9, 65, 9);

        world.Environment.AmbientDarkness = 11;
        using var night = new WorldRegionSnapshot(world, 7, 63, 7, 9, 65, 9);

        Assert.Equal(day.GetLightLevels(8, 64, 8, 0), night.GetLightLevels(8, 64, 8, 0));
        Assert.Equal(12, day.GetLightLevels(8, 64, 8, 0).Sky);
        Assert.Equal(7, day.GetLightLevels(8, 64, 8, 0).Block);

        // The compatibility brightness API still collapses through ambient darkness. This proves
        // the test would catch chunk rendering accidentally returning to that baked-light path.
        Assert.NotEqual(day.GetLightValue(8, 64, 8), night.GetLightValue(8, 64, 8));
    }

    [Fact]
    public void Ambient_darkness_notification_cannot_schedule_a_global_chunk_remesh()
    {
        var root = FindRepositoryRoot();
        var file = Path.Combine(root, "OmniBlock.Client", "Rendering", "WorldRenderer.cs");
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file).GetRoot();
        var method = syntax.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == "NotifyAmbientDarknessChanged");

        var forbiddenCalls = method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression.ToString().Contains("UpdateAllRenderers", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(forbiddenCalls);
    }

    [Fact]
    public void Pure_light_invalidation_cannot_advance_or_dispatch_geometry()
    {
        var root = FindRepositoryRoot();
        var file = Path.Combine(root, "OmniBlock.Client", "Rendering", "Chunks", "ChunkRenderer.cs");
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file).GetRoot();
        var method = syntax.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == "MarkLightDirty");

        var calls = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call => call.Expression.ToString()).ToArray();
        Assert.DoesNotContain(calls, call => call.Contains("MarkDirty", StringComparison.Ordinal));
        Assert.DoesNotContain(calls, call => call.Contains("MeshChunk", StringComparison.Ordinal));
    }

    [Fact]
    public void Network_light_snapshots_use_the_light_only_invalidation_path()
    {
        var root = FindRepositoryRoot();
        var file = Path.Combine(root, "OmniBlock.Client", "Network", "ClientNetworkHandler.cs");
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file).GetRoot();
        var method = syntax.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == "onLightSections");

        var calls = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call => call.Expression.ToString()).ToArray();
        Assert.Contains(calls, call => call.EndsWith("setLightDirty", StringComparison.Ordinal));
        Assert.DoesNotContain(calls, call => call.EndsWith("setBlocksDirty", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OmniBlock.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException("Could not locate the OmniBlock repository root.");
    }
}
