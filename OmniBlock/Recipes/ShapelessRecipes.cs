using OmniBlock.Inventories;
using OmniBlock.Items;
namespace OmniBlock.Recipes;

internal class ShapelessRecipes(ItemStack output, List<ItemStack> items) : IRecipe
{
    public ItemStack GetRecipeOutput()
    {
        return output;
    }

    public bool Matches(InventoryCrafting craftingInventory)
    {
        List<ItemStack> remainingIngredients = [.. items];

        for (int row = 0; row < 3; ++row)
        {
            for (int col = 0; col < 3; ++col)
            {
                ItemStack? gridStack = craftingInventory.GetStackAt(col, row);
                if (gridStack == null)
                {
                    continue;
                }

                bool foundMatch = false;
                List<ItemStack>.Enumerator iterator = remainingIngredients.GetEnumerator();

                while (iterator.MoveNext())
                {
                    ItemStack recipeItem = iterator.Current;
                    if (gridStack.ItemId != recipeItem.ItemId || (recipeItem.GetDamage() != -1 && gridStack.GetDamage() != recipeItem.GetDamage()))
                    {
                        continue;
                    }

                    foundMatch = true;
                    remainingIngredients.Remove(recipeItem);
                    break;
                }

                if (!foundMatch)
                {
                    return false;
                }
            }
        }

        return remainingIngredients.Count == 0;
    }

    public ItemStack GetCraftingResult(InventoryCrafting craftingInventory)
    {
        return output.Copy();
    }

    public int GetRecipeSize()
    {
        return items.Count;
    }

    public override int GetHashCode()
    {
        int hash = 0;
        for (int i = 0; i < items.Count; i++)
        {
            hash += (items[i].ItemId + (items[i].GetDamage() << 8)) * (i + 1);
        }

        return hash + (output.ItemId << 12) + (output.GetDamage() << 20) + output.Count;
    }
}
