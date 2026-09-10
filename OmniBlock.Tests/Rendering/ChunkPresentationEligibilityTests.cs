using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkPresentationEligibilityTests
{
    [Fact]
    public void Readiness_diagnostics_cannot_filter_resident_mesh_presentation()
    {
        var root = FindRepositoryRoot();
        var file = Path.Combine(root, "OmniBlock.Client", "Rendering", "Chunks", "ChunkRenderer.cs");
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file).GetRoot();
        var visit = syntax.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == "Visit");

        Assert.DoesNotContain("MeshReadyRadius", visit.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("_meshReadyRadius", visit.ToString(), StringComparison.Ordinal);

        var addedCollections = visit.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(call => call.Expression.ToString())
            .ToArray();
        Assert.Contains("_visibleRenderers.Add", addedCollections);
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
