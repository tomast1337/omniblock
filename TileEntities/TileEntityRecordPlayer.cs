using betareborn.NBT;

namespace betareborn.TileEntities
{
    public class TileEntityRecordPlayer : TileEntity
    {
        public int record;

        public override void readNbt(NBTTagCompound var1)
        {
            base.readNbt(var1);
            record = var1.getInteger("Record");
        }

        public override void writeNbt(NBTTagCompound var1)
        {
            base.writeNbt(var1);
            if (record > 0)
            {
                var1.setInteger("Record", record);
            }

        }
    }

}