namespace OmniBlock.Tests.Recipes;

public sealed class StaticRecipeConsumerAccessTests
{
    [Fact]
    public void Runtime_consumers_do_not_access_legacy_static_recipe_stores()
    {
        string root = FindRepositoryRoot();
        string runtimeRoot = Path.Combine(root, "OmniBlock");
        foreach (string path in Directory.EnumerateFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(path);
            Assert.DoesNotContain("RecipesCrafting", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RecipesSmelting", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RecipeManager.CraftingTypes", source, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(Path.Combine(runtimeRoot, "Recipes", "RecipeManager.cs")));
        Assert.False(File.Exists(Path.Combine(runtimeRoot, "Recipes", "ICraftingRegistry.cs")));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OmniBlock.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the OmniBlock repository root.");
    }
}
