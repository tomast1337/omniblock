namespace OmniBlock.Tests.Entities;

public sealed class EntityRuntimeIsolationArchitectureTests
{
    [Fact]
    public void Runtime_entity_producers_do_not_resolve_through_the_static_registry()
    {
        string root = FindRepositoryRoot();
        string[] directories =
        [
            Path.Combine(root, "OmniBlock", "Entities", "Behaviors"),
            Path.Combine(root, "OmniBlock", "Blocks", "Behaviors"),
            Path.Combine(root, "OmniBlock", "Blocks", "Entities")
        ];

        string[] offenders = directories
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => File.ReadAllText(file).Contains("EntityRegistry.ByName", StringComparison.Ordinal)
                           || File.ReadAllText(file).Contains("EntityRegistry.Create", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file))
            .Order()
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OmniBlock.slnx")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new DirectoryNotFoundException("Could not locate the OmniBlock repository root.");
    }
}
