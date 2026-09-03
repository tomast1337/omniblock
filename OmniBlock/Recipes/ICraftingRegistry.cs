namespace OmniBlock.Recipes;

using OmniBlock.Registries;

public interface ICraftingRegistry
{
    public string Name { get; }
    public int Count { get; }
    public void Clear();
    public void BuildRecipe(RecipeDefinition def, RuntimeItemRegistry items);
}
