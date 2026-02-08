using betareborn.NBT;

namespace betareborn.Blocks.BlockEntities
{
    public class BlockEntityRecordPlayer : BlockEntity
    {
        public int recordId;

        public override void readNbt(NBTTagCompound nbt)
        {
            base.readNbt(nbt);
            recordId = nbt.getInteger("Record");
        }

        public override void writeNbt(NBTTagCompound nbt)
        {
            base.writeNbt(nbt);
            if (recordId > 0)
            {
                nbt.setInteger("Record", recordId);
            }

        }
    }

}