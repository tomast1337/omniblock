namespace OmniBlock.Tests.Catalog;

/// <summary>Prevents runtime code from restoring the item catalog globals removed by the runtime migration.</summary>
public sealed class StaticItemCatalogAccessTests
{
    [Fact]
    public void Production_code_cannot_access_legacy_item_catalog_globals()
    {
        var root = FindRepositoryRoot();
        string[] forbidden = ["Item." + "Items", "Item." + "ByName", "Item" + "Lookup"];
        var violations = Directory.EnumerateDirectories(root, "OmniBlock*")
            .Where(static directory => !directory.EndsWith(".Tests", StringComparison.Ordinal))
            .SelectMany(static directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                  && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => forbidden.Any(entry.line.Contains))
                .Select(entry => $"{Path.GetRelativePath(root, file)}:{entry.number}"))
            .ToArray();

        Assert.True(violations.Length == 0,
            $"Runtime item access must use an injected IItemRuntimeView:{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OmniBlock.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
