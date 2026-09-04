using OmniBlock.Items;
using OmniBlock.Registries.Data;

namespace OmniBlock.Tests;

internal static class TestItemCatalog
{
    public static IReadOnlyList<ItemDefinition> LoadDefinitions()
    {
        var loader = new ItemDefinitionJsonLoader(RegistryDefinitions.Items.AssetPath, LoadLocations.Assets);
        loader.LoadFromPaths(null, null, null);
        if (loader.HasErrors)
            throw new AssetLoadException(loader.FirstErrorMessage ?? "Failed to load item definitions.");
        return ContentIdAllocator.AssignItemIds(loader).ToArray();
    }
}
