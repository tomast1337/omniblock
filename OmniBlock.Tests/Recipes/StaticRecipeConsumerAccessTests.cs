namespace OmniBlock.Tests.Recipes;

public sealed class StaticRecipeConsumerAccessTests
{
    [Fact]
    public void Runtime_consumers_do_not_access_legacy_static_recipe_stores()
    {
        string root = FindRepositoryRoot();
        string[] consumers =
        [
            "OmniBlock/Screens/CraftingScreenHandler.cs",
            "OmniBlock/Screens/PlayerScreenHandler.cs",
            "OmniBlock/Blocks/Entities/BlockEntityFurnace.cs",
            "OmniBlock/Stats/Stats.cs"
        ];

        foreach (string relativePath in consumers)
        {
            string source = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.DoesNotContain("RecipesCrafting", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RecipesSmelting", source, StringComparison.Ordinal);
        }
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
