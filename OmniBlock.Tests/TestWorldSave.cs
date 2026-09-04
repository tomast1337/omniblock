using OmniBlock.Blocks.Entities;
using OmniBlock.NBT;

namespace OmniBlock.Tests;

public class TestWorldSave
{
    [Fact]
    public void TestBlockEntityChestId()
    {
        var chest = new BlockEntityChest();
        var nbt = new NBTTagCompound();
        chest.WriteNbt(nbt);
        Assert.Equal("Chest", nbt.GetString("id"));
    }
}
