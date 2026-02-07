using betareborn.Materials;
using betareborn.NBT;
using betareborn.Worlds;

namespace betareborn.TileEntities
{
    public class TileEntityNote : TileEntity
    {
        public sbyte note = 0;
        public bool previousRedstoneState = false;

        public override void writeNbt(NBTTagCompound var1)
        {
            base.writeNbt(var1);
            var1.setByte("note", note);
        }

        public override void readNbt(NBTTagCompound var1)
        {
            base.readNbt(var1);
            note = var1.getByte("note");
            if (note < 0)
            {
                note = 0;
            }

            if (note > 24)
            {
                note = 24;
            }

        }

        public void changePitch()
        {
            note = (sbyte)((note + 1) % 25);
            markDirty();
        }

        public void triggerNote(World var1, int var2, int var3, int var4)
        {
            if (var1.getBlockMaterial(var2, var3 + 1, var4) == Material.air)
            {
                Material var5 = var1.getBlockMaterial(var2, var3 - 1, var4);
                byte var6 = 0;
                if (var5 == Material.rock)
                {
                    var6 = 1;
                }

                if (var5 == Material.sand)
                {
                    var6 = 2;
                }

                if (var5 == Material.glass)
                {
                    var6 = 3;
                }

                if (var5 == Material.wood)
                {
                    var6 = 4;
                }

                var1.playNoteAt(var2, var3, var4, var6, note);
            }
        }
    }

}