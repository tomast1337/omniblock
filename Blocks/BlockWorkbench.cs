using betareborn.Entities;
using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockWorkbench : Block
    {

        public BlockWorkbench(int var1) : base(var1, Material.WOOD)
        {
            blockIndexInTexture = 59;
        }

        public override int getBlockTextureFromSide(int var1)
        {
            return var1 == 1 ? blockIndexInTexture - 16 : (var1 == 0 ? Block.planks.getBlockTextureFromSide(0) : (var1 != 2 && var1 != 4 ? blockIndexInTexture : blockIndexInTexture + 1));
        }

        public override bool blockActivated(World var1, int var2, int var3, int var4, EntityPlayer var5)
        {
            if (var1.multiplayerWorld)
            {
                return true;
            }
            else
            {
                var5.displayWorkbenchGUI(var2, var3, var4);
                return true;
            }
        }
    }

}