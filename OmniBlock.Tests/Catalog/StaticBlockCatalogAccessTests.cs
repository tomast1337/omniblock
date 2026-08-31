using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OmniBlock.Tests.Catalog;

/// <summary>
/// Ensures no consumer bypasses the published content runtime through the legacy block array.
/// </summary>
public sealed class StaticBlockCatalogAccessTests
{
    [Fact]
    public void Direct_static_block_catalog_access_is_forbidden()
    {
        IReadOnlyList<StaticAccess> accesses = FindAccesses();

        Assert.True(accesses.Count == 0,
            $"Use BlockRegistry instead of the legacy static block array:{Environment.NewLine}"
            + string.Join(Environment.NewLine, accesses));
    }

    private static IReadOnlyList<StaticAccess> FindAccesses()
    {
        string sourceRoot = FindRepositoryRoot();
        var accesses = new List<StaticAccess>();

        IEnumerable<string> sourceFiles = Directory.EnumerateDirectories(sourceRoot, "OmniBlock*")
            .SelectMany(static directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories));

        foreach (string file in sourceFiles
                     .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file);
            foreach (MemberAccessExpressionSyntax member in tree.GetRoot()
                         .DescendantNodes()
                         .OfType<MemberAccessExpressionSyntax>())
            {
                if (member.Expression is IdentifierNameSyntax { Identifier.ValueText: "Block" }
                    && member.Name.Identifier.ValueText == "Blocks"
                    && member.Parent is ElementAccessExpressionSyntax)
                {
                    FileLinePositionSpan location = member.GetLocation().GetLineSpan();
                    accesses.Add(new StaticAccess(
                        Path.GetRelativePath(sourceRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                        location.StartLinePosition.Line + 1));
                }
            }
        }

        return accesses;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OmniBlock.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed record StaticAccess(string RelativePath, int Line)
    {
        public override string ToString() => $"{RelativePath}:{Line}";
    }
}
