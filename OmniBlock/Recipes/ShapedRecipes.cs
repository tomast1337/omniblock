using OmniBlock.Inventories;
using OmniBlock.Items;

namespace OmniBlock.Recipes;

internal class ShapedRecipes(int width, int height, ItemStack?[] items, ItemStack output)
    : IRecipe
{
    public ItemStack GetRecipeOutput()
    {
        return output;
    }

    public bool Matches(InventoryCrafting craftingInventory)
    {
        for (int offsetX = 0; offsetX <= 3 - width; ++offsetX)
        {
            for (int offsetY = 0; offsetY <= 3 - height; ++offsetY)
            {
                if (matchesAtOffset(craftingInventory, offsetX, offsetY, true))
                    return true;
                if (matchesAtOffset(craftingInventory, offsetX, offsetY, false))
                    return true;
            }
        }

        return false;
    }

    private bool matchesAtOffset(InventoryCrafting craftingInventory, int offsetX, int offsetY, bool mirrored)
    {
        for (int gridX = 0; gridX < 3; ++gridX)
        {
            for (int gridY = 0; gridY < 3; ++gridY)
            {
                int recipeX = gridX - offsetX;
                int recipeY = gridY - offsetY;
                ItemStack? expected = null;
                if (recipeX >= 0 && recipeY >= 0 && recipeX < width && recipeY < height)
                {
                    expected = mirrored ? items[width - recipeX - 1 + recipeY * width] : items[recipeX + recipeY * width];
                }

                ItemStack actual = craftingInventory.GetStackAt(gridX, gridY);
                if (actual == null && expected == null)
                {
                    continue;
                }

                if (actual == null && expected != null || actual != null && expected == null)
                {
                    return false;
                }

                if (expected.ItemId != actual.ItemId)
                {
                    return false;
                }

                if (expected.GetDamage() != -1 && expected.GetDamage() != actual.GetDamage())
                {
                    return false;
                }
            }
        }

        return true;
    }

    public ItemStack GetCraftingResult(InventoryCrafting craftingInventory)
    {
        return new ItemStack(output.ItemId, output.Count, output.GetDamage());
    }

    public int GetRecipeSize()
    {
        return width * height;
    }

    public override int GetHashCode()
    {
        int hash = 0;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] != null)
                hash += (items[i].ItemId + (items[i].GetDamage() << 8)) * (i + 1);
        }

        hash += (output.ItemId << 12) + (output.GetDamage() << 20) + output.Count;

        return ((width + height * 4) << 28) + hash;
    }
}
