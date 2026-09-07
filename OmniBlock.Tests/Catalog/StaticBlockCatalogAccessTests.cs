using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OmniBlock.Tests.Catalog;

/// <summary>
///     Ensures no consumer bypasses the published content runtime through the legacy block array.
/// </summary>
public sealed class StaticBlockCatalogAccessTests
{
    private static readonly string[] s_forbiddenDerivedMetadataArrays =
    [
        "BlocksOpaque",
        "BlocksRandomTick",
        "BlocksWithEntity",
        "BlockLightOpacity",
        "BlocksAllowVision",
        "BlocksLightLuminance",
        "BlocksIgnoreMetaUpdate"
    ];

    private static readonly string[] s_forbiddenBootstrapStoreIdentifiers =
    [
        "s_" + "bootstrapBlocks",
        "GetDuring" + "Bootstrap"
    ];

    [Fact]
    public void Direct_static_block_catalog_access_is_forbidden()
    {
        var accesses = FindAccesses();

        Assert.True(accesses.Count == 0,
            $"Use BlockRegistry instead of the legacy static block array:{Environment.NewLine}"
            + string.Join(Environment.NewLine, accesses));
    }

    [Fact]
    public void Parallel_static_block_metadata_arrays_are_forbidden()
    {
        var violations = FindSourceFiles()
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => s_forbiddenDerivedMetadataArrays.Any(entry.line.Contains))
                .Select(entry => $"{Path.GetRelativePath(FindRepositoryRoot(), file)}:{entry.number}"))
            .ToArray();

        Assert.True(violations.Length == 0,
            $"Derived block metadata belongs on runtime blocks:{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Block_cannot_own_a_bootstrap_catalog()
    {
        var violations = FindSourceFiles()
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => s_forbiddenBootstrapStoreIdentifiers.Any(entry.line.Contains))
                .Select(entry => $"{Path.GetRelativePath(FindRepositoryRoot(), file)}:{entry.number}"))
            .ToArray();

        Assert.True(violations.Length == 0,
            $"ContentRuntimeBuilder must own blocks during bootstrap:{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Block_registry_cannot_bridge_items_or_write_the_legacy_item_array()
    {
        var blockRegistry = Path.Combine(FindRepositoryRoot(), "OmniBlock", "Blocks", "BlockRegistry.cs");
        var source = File.ReadAllText(blockRegistry);

        Assert.DoesNotContain("BridgeTo" + "Items", source);
        Assert.DoesNotContain("Item." + "Items", source);
    }

    [Fact]
    public void Runtime_block_behaviors_cannot_access_global_content_registries()
    {
        var behaviorDirectory = Path.Combine(FindRepositoryRoot(), "OmniBlock", "Blocks", "Behaviors");
        string[] forbidden = ["BlockRegistry.", "MaterialRegistry.", "Item.ByName", "ItemLookup", "Atlases.", "SoundGroupRegistry."];
        var violations = Directory.EnumerateFiles(behaviorDirectory, "*.cs")
            .Where(static file => Path.GetFileName(file) != "BehaviorBuildContext.cs")
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => forbidden.Any(entry.line.Contains))
                .Select(entry => $"{Path.GetRelativePath(FindRepositoryRoot(), file)}:{entry.number}"))
            .ToArray();

        Assert.True(violations.Length == 0,
            $"Runtime block behaviors must use resolved dependencies or their bound runtime view:{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Runtime_item_behaviors_cannot_access_the_static_block_registry()
    {
        string root = FindRepositoryRoot();
        string behaviorDirectory = Path.Combine(root, "OmniBlock", "Items", "Behaviors");
        string[] violations = Directory.EnumerateFiles(behaviorDirectory, "*.cs")
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(static entry => entry.line.Contains("BlockRegistry.")
                                       && !entry.line.Contains("RuntimeBlockRegistry."))
                .Select(entry => $"{Path.GetRelativePath(root, file)}:{entry.number}"))
            .ToArray();

        Assert.True(violations.Length == 0,
            $"Runtime item behaviors must use resolved blocks or world.Content.Blocks:{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void World_core_cannot_access_the_static_block_registry()
    {
        var root = FindRepositoryRoot();
        string[] directories =
        [
            Path.Combine(root, "OmniBlock", "Worlds", "Core"),
            Path.Combine(root, "OmniBlock", "Blocks", "Entities"),
            Path.Combine(root, "OmniBlock", "Entities")
        ];
        var violations = directories
            .SelectMany(static directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(static entry => entry.line.Contains("BlockRegistry."))
                .Select(entry => $"{Path.GetRelativePath(root, file)}:{entry.number}"))
            .ToArray();

        Assert.True(violations.Length == 0,
            $"World-scoped code must resolve blocks through world.Content.Blocks:{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Migrated_hot_path_world_systems_cannot_access_the_static_block_registry()
    {
        var root = FindRepositoryRoot();
        string[] files =
        [
            Path.Combine(root, "OmniBlock", "Worlds", "Chunks", "Chunk.cs"),
            Path.Combine(root, "OmniBlock", "Worlds", "Lighting", "LightUpdate.cs"),
            Path.Combine(root, "OmniBlock", "Worlds", "Core", "Systems", "LightingEngine.cs"),
            Path.Combine(root, "OmniBlock", "Worlds", "Mechanics", "Explosion.cs"),
            Path.Combine(root, "OmniBlock", "PathFinding", "PathFinder.cs")
        ];
        var violations = files
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1, file))
                .Where(static entry => entry.line.Contains("BlockRegistry.")
                                       && !entry.line.Contains("RuntimeBlockRegistry."))
                .Select(entry => $"{Path.GetRelativePath(root, entry.file)}:{entry.number}"))
            .ToArray();

        Assert.True(violations.Length == 0,
            $"Migrated world systems must resolve blocks through their owning runtime:{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Migrated_shared_runtime_consumers_cannot_access_the_static_block_registry()
    {
        var root = FindRepositoryRoot();
        string[] paths =
        [
            Path.Combine(root, "OmniBlock", "Bootstrap.cs"),
            Path.Combine(root, "OmniBlock", "Blocks", "Block.cs"),
            Path.Combine(root, "OmniBlock", "Blocks", "Behaviors", "BehaviorBuildContext.cs"),
            Path.Combine(root, "OmniBlock", "Registries", "DefaultRegistries.cs"),
            Path.Combine(root, "OmniBlock", "Screens"),
            Path.Combine(root, "OmniBlock", "Stats"),
            Path.Combine(root, "OmniBlock", "Worlds", "Generation", "Biomes")
        ];
        var files = paths.SelectMany(static path => Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
            : [path]);
        var violations = files
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1, file))
                .Where(static entry => entry.line.Contains("BlockRegistry.")
                                       && !entry.line.Contains("RuntimeBlockRegistry."))
                .Select(entry => $"{Path.GetRelativePath(root, entry.file)}:{entry.number}"))
            .ToArray();

        Assert.True(violations.Length == 0,
            $"Shared runtime consumers must use injected block views:{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations));
    }

    private static IReadOnlyList<StaticAccess> FindAccesses()
    {
        var sourceRoot = FindRepositoryRoot();
        var accesses = new List<StaticAccess>();

        foreach (var file in FindSourceFiles())
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file);
            foreach (var member in tree.GetRoot()
                         .DescendantNodes()
                         .OfType<MemberAccessExpressionSyntax>())
            {
                if (member.Expression is IdentifierNameSyntax { Identifier.ValueText: "Block" }
                    && member.Name.Identifier.ValueText == "Blocks"
                    && member.Parent is ElementAccessExpressionSyntax)
                {
                    var location = member.GetLocation().GetLineSpan();
                    accesses.Add(new StaticAccess(
                        Path.GetRelativePath(sourceRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                        location.StartLinePosition.Line + 1));
                }
            }
        }

        return accesses;
    }

    private static IEnumerable<string> FindSourceFiles()
    {
        var sourceRoot = FindRepositoryRoot();
        return Directory.EnumerateDirectories(sourceRoot, "OmniBlock*")
            .SelectMany(static directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                  && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                  && Path.GetFileName(file) != nameof(StaticBlockCatalogAccessTests) + ".cs");
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
