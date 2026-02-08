using betareborn.Blocks.Materials;
using betareborn.NBT;
using betareborn.Worlds;

namespace betareborn.Blocks.BlockEntities
{
    public class BlockEntityNote : BlockEntity
    {
        public sbyte note = 0;
        public bool powered = false;

        public override void writeNbt(NBTTagCompound nbt)
        {
            base.writeNbt(nbt);
            nbt.setByte("note", note);
        }

        public override void readNbt(NBTTagCompound nbt)
        {
            base.readNbt(nbt);
            note = nbt.getByte("note");
            if (note < 0)
            {
                note = 0;
            }

            if (note > 24)
            {
                note = 24;
            }

        }

        public void cycleNote()
        {
            note = (sbyte)((note + 1) % 25);
            markDirty();
        }

        public void playNote(World world, int x, int y, int z)
        {
            if (world.getMaterial(x, y + 1, z) == Material.AIR)
            {
                Material var5 = world.getMaterial(x, y - 1, z);
                byte var6 = 0;
                if (var5 == Material.STONE)
                {
                    var6 = 1;
                }

                if (var5 == Material.SAND)
                {
                    var6 = 2;
                }

                if (var5 == Material.GLASS)
                {
                    var6 = 3;
                }

                if (var5 == Material.WOOD)
                {
                    var6 = 4;
                }

                world.playNoteBlockActionAt(x, y, z, var6, note);
            }
        }
    }

}